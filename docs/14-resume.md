# Part 14 — Resume Impact

## 14.1 Golden rules
1. **Only write numbers you have measured** (k6, Grafana, test counts). Placeholders below are marked `[measure]` — replace them with your real results. A fabricated metric dies in the first follow-up question.
2. Format: **Action verb + what + how (tech) + measurable result** (Google's XYZ formula).
3. Lead with the *problem and scale*, then the tech. Recruiters keyword-scan; engineers read for judgement.
4. Link the repo + a 60-second demo GIF/video. Pin it on GitHub. Put the architecture diagram at the top of the README.

## 14.2 Project entry (pick 4–6 bullets; tailor per company)

**Reroute — Real-time Travel Disruption & EU261 Claims Platform** · *C#/.NET 10, React/TypeScript, Kafka, RabbitMQ, Redis, SQL Server, Elasticsearch, Azure (AKS), OpenAI* · github.com/<you>/reroute

- Architected an event-driven platform of **8 ASP.NET Core microservices** that detects flight disruptions from a Kafka stream and notifies affected travellers in real time via SignalR, achieving **p95 end-to-end latency of [measure] s** at **[measure] events/s**.
- Designed an **event-sourced claims domain (DDD + CQRS)** on SQL Server with optimistic concurrency, transactional outbox and idempotent consumers, guaranteeing **zero lost or duplicated claim events** across [measure] chaos tests (broker/DB restarts under load).
- Implemented the **EU261 compensation engine** as a pure domain policy covered by **[measure] table-driven unit tests (100% branch coverage)**, separating deterministic money calculations from LLM-generated explanations.
- Built a **RAG copilot** (Azure OpenAI, Qdrant, structure-aware chunking, citations) with a nightly evaluation harness reaching **[measure]% retrieval hit@8**; cut LLM cost by **[measure]%** with a Redis semantic cache.
- Secured the system with **OAuth 2.0/OIDC (Authorization Code + PKCE) through a YARP BFF gateway**, JWT validation and resource-based authorization in every service, and per-user Redis rate limiting.
- Delivered **hybrid semantic + geo search** over hotels with Elasticsearch (BM25 + kNN, Reciprocal Rank Fusion), improving relevance on [measure]-query judgement set from [x] to [y] nDCG@10.
- Instrumented every service with **OpenTelemetry** (traces propagated across HTTP, Kafka and RabbitMQ), Prometheus SLO burn-rate alerts and Grafana dashboards; reduced p99 latency of the claims list endpoint from [measure] to [measure] ms using covering indexes and two-level caching.
- Automated delivery with **GitHub Actions** (Testcontainers integration tests, Trivy scanning, OIDC-federated deploys) to **AKS, Azure SQL, Service Bus, Event Hubs and Azure OpenAI** provisioned via Bicep, keeping idle cloud cost under **€[measure]/month**.

## 14.3 Tailoring per company (swap in the bullet that matches)

| Company | Lead with | Extra keyword bullet |
|---|---|---|
| **Booking.com** | Search & real-time travel domain, React performance | "Built a feature-sliced React 19 + TypeScript SPA with TanStack Query and real-time cache updates; LCP [measure] s on mid-range mobile." |
| **Adyen** | Event sourcing, idempotency, ledger | "Implemented a double-entry payout ledger with idempotency keys and zero-sum invariants enforced in both domain and database." |
| **Uber** | Kafka streaming, latency, scale | "Partitioned Kafka topics by flight key to preserve per-entity ordering; atomic Redis Lua compare-and-set discards out-of-order events across replicas." |
| **Microsoft** | Azure-native, .NET 10, AKS, Bicep, Entra ID | "Used Workload Identity and managed identities end-to-end — zero secrets in code, pipelines or manifests." |
| **Amazon** | Ownership, operational excellence | "Defined SLOs with multi-window burn-rate alerts and runbooks; ran game days killing Kafka/Redis under load." |
| **Databricks** | Data + AI | "Designed Kafka topics as replayable data products and an offline evaluation loop for RAG and ranking models." |
| **Optiver** | Latency, concurrency correctness | "Profiled the hot path (dotnet-counters/trace) and removed allocations, reducing p99 from [x] to [y] ms." |
| **Picnic** | Event-driven ops | "Event-driven architecture with outbox/inbox patterns, DLQ replay tooling and consumer-lag-based autoscaling (KEDA)." |
| **ING / Rabobank** | Regulated domain, audit, security | "Immutable audit trail via event sourcing; GDPR-compliant design keeping PII out of events and vector stores." |

## 14.4 Interview stories this project gives you (STAR-ready)
| Prompt | Story |
|---|---|
| "Hardest bug?" | Dual-write bug (DB commit succeeded, Kafka publish failed) → introduced transactional outbox; show the test that reproduces it. |
| "A trade-off you made?" | Inline vs async projections (read-your-writes vs write latency). Kafka vs RabbitMQ. Modular monolith vs microservices. |
| "How do you handle failure?" | Retry/DLQ/circuit breaker matrix; game-day results. |
| "Design a system…" (system design round) | You've *built* one: walk through Part 2 with real numbers. |
| "Security?" | BFF + PKCE, IDOR prevention via resource-based authz, no secrets via Workload Identity. |
| "AI responsibly?" | Code computes money, LLM explains; citations, eval harness, injection mitigations. |

## 14.5 Where to put it
- Resume: **Projects** section directly under Experience (or above it if applying for a level jump).
- LinkedIn: Featured section with demo video + architecture image.
- GitHub README: badges (CI passing, coverage), diagram, GIF, "How to run in 1 command", ADR links, measured numbers.
- Write **one technical blog post** (e.g. "Event sourcing on SQL Server with Dapper — no framework required"). Posts get shared internally at companies and recruiters do read them.

Next: [Roadmap](15-roadmap.md)
