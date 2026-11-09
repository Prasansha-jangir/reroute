# Part 7 — Redis, Kafka, RabbitMQ & Caching Strategies

## 7.1 Kafka vs RabbitMQ — the decision you will be asked about

| Question | Kafka | RabbitMQ |
|---|---|---|
| Message meaning | **Fact** that happened (`FlightCancelled`) | **Command / job** to do (`SendEmail`) |
| Consumers | Many independent consumer groups, each reads everything | Competing consumers share one queue |
| Retention | Days/forever; **replayable** | Deleted on ack |
| Ordering | Per partition (by key) | Per queue (lost with competing consumers + retries) |
| Retries/DLQ | Manual (retry topics) | Built-in (MassTransit retry + `_error` queue) |
| Routing | Topic only | Exchanges: direct/topic/fanout/headers |
| Throughput | Very high (sequential disk I/O) | High, lower than Kafka |
| Azure equivalent | **Event Hubs (Kafka protocol)** | **Azure Service Bus** |

**Rule in this codebase:** *if more than one context cares about it, or you might want to replay it → Kafka. If exactly one handler must do it, with retries → RabbitMQ.*

## 7.2 Kafka implementation

### Topics

| Topic | Key | Partitions (dev / prod) | Retention | Producer | Consumers |
|---|---|---|---|---|---|
| `flight-status.raw.v1` | `flightKey` | 3 / 24 | 3 days | feed/simulator | disruption |
| `disruption.detected.v1` | `flightKey` | 3 / 12 | 30 days | disruption | notification, claims, rebooking, copilot |
| `itinerary.events.v1` | `tripId` | 3 / 12 | compacted | itinerary | disruption |
| `claims.events.v1` | `claimId` | 3 / 12 | 90 days | claims (outbox) | ledger, notification, analytics |
| `*.dlq` | original key | 1 / 3 | 14 days | any consumer | ops tooling |

- **Partition count** = max useful consumer parallelism. Keys guarantee per-entity ordering.
- **Versioned topic names** (`.v1`) → breaking schema change = new topic + dual-publish window.
- **Schemas**: JSON with a `type` + `schemaVersion` envelope (CloudEvents-style headers: `ce-id`, `ce-type`, `ce-source`, `traceparent`). Stretch: Avro + Schema Registry.

### Producer settings (`KafkaProducer.cs`)
```
EnableIdempotence = true    // no duplicates from producer retries, keeps order
Acks = All                  // wait for all in-sync replicas
LingerMs = 5, CompressionType = Lz4   // batching for throughput
```

### Consumer pattern (`KafkaConsumerService<T>`)
```
EnableAutoCommit = false            // commit only after successful handling
AutoOffsetReset = Earliest
loop:
   msg = Consume()
   if Inbox.AlreadyProcessed(msg.id) → commit, continue
   try   Handle(msg); Inbox.Mark(msg.id); Commit(msg)
   catch transient → retry with backoff (bounded) 
   catch poison    → produce to <topic>.dlq with error headers; Commit(msg)
```
- Trace context propagated via the `traceparent` header → one trace spans HTTP → outbox → Kafka → consumer.
- Consumer lag exported as a metric; alert when lag > 10 000 for 5 min.

### Transactional outbox
```mermaid
sequenceDiagram
    participant H as Command handler
    participant DB as SQL Server
    participant P as OutboxPublisher (BackgroundService)
    participant K as Kafka
    H->>DB: BEGIN TRAN; INSERT Events; UPSERT ClaimSummaries; INSERT OutboxMessages; COMMIT
    loop every 500 ms (or wake on signal)
        P->>DB: SELECT TOP 100 ... WITH (UPDLOCK, READPAST, ROWLOCK) WHERE ProcessedAt IS NULL
        P->>K: produce (idempotent)
        P->>DB: UPDATE ProcessedAt = now
    end
```
`UPDLOCK + READPAST` lets multiple replicas publish concurrently without stepping on each other — a neat SQL Server trick worth explaining.

## 7.3 RabbitMQ implementation (MassTransit 8)
- Commands: `SendNotification`, `GenerateClaimLetter`.
- Topology: MassTransit creates an exchange per message type + a queue per consumer endpoint (`send-notification`), `_error` and `_skipped` queues automatically.
- **Retry**: `UseMessageRetry(r => r.Intervals(1s, 5s, 30s))` for transient failures; **redelivery** (delayed, survives restarts) for longer outages: `UseDelayedRedelivery(5m, 15m, 1h)`.
- **Concurrency**: `PrefetchCount = 16`, `ConcurrentMessageLimit = 8` → back-pressure.
- **Transport swap**: same consumers run on Azure Service Bus by switching `UsingRabbitMq` → `UsingAzureServiceBus` (configured by environment) — show this in the interview as "infrastructure is a detail".

## 7.4 Redis implementation

| Use case | Data structure / command | Where |
|---|---|---|
| Read-model cache (L2) | String (via `HybridCache`) + tags | Claims queries |
| Hot flight state | Hash `flight:{key}` + Lua compare-and-set | Disruption |
| Idempotency keys | `SET idem:{user}:{key} {response} NX EX 86400` | All POST endpoints |
| Rebooking holds / distributed lock | `SET hold:{option} {traveller} NX PX 600000` + Lua release-if-owner | Rebooking |
| Rate limiting (distributed) | Sorted set sliding window / `INCR`+`EXPIRE` fixed window | Gateway |
| SignalR backplane | Pub/Sub | Notification |
| Semantic cache for LLM answers | String keyed by hash(normalized question + context) | Copilot |

### Lua compare-and-set (Disruption)
```lua
-- KEYS[1] = flight:{key}; ARGV[1] = newStatus, ARGV[2] = newDelay, ARGV[3] = eventTimestamp
local ts = redis.call('HGET', KEYS[1], 'ts')
if ts and tonumber(ts) >= tonumber(ARGV[3]) then return 0 end   -- stale/out-of-order event
local prev = redis.call('HGET', KEYS[1], 'status')
redis.call('HSET', KEYS[1], 'status', ARGV[1], 'delay', ARGV[2], 'ts', ARGV[3])
redis.call('EXPIRE', KEYS[1], 172800)
return prev or ''
```
Atomic in Redis → exactly one replica observes each transition, out-of-order events are dropped.

## 7.5 Caching strategies — when to use which

| Strategy | Used for | Consistency | Notes |
|---|---|---|---|
| **Cache-aside** (lazy) | Claim summaries, airport directory | Eventually consistent, bounded by TTL + explicit invalidation | `HybridCache.GetOrCreateAsync` gives **stampede protection** (only one caller recomputes). |
| **Write-through-ish invalidation** | Claim detail after a command | Read-your-writes | Handler removes tag `claim:{id}` after commit. |
| **Read-through with stale-while-revalidate** | Hotel search results | Seconds-stale OK | Short TTL (30 s) + background refresh. |
| **Write-behind** | *Not used* | — | Risk of data loss; explain why you avoided it for money/claims. |
| **Two-level (L1 memory + L2 Redis)** | Hot, small, read-heavy data | — | `HybridCache` handles both; L1 cut Redis round-trips. |
| **HTTP caching** | Static assets, airports list | — | `Cache-Control: public, max-age=86400, immutable` for hashed assets via CDN. |

**Invalidation in a distributed system:** after a `ClaimStatusChanged` event, every service instance must drop its L1 copy → HybridCache tag invalidation + Redis pub/sub to broadcast. "There are only two hard things…" — be ready to discuss TTL vs explicit invalidation trade-offs.

**Cache key design:** `reroute:{service}:{entity}:{id}:v{schemaVersion}` → bumping schema version invalidates all old shapes safely after a deploy.

Next: [Part 8 — AI Features](08-ai.md)
