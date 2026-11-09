# Architecture Decision Records

Short, dated records of *why* — the thing interviewers probe. Format: Context → Decision → Consequences.

## ADR-001 Microservices instead of a modular monolith (2026-11-09)
**Context.** A solo developer building a portfolio project. A modular monolith would ship faster and be cheaper to operate.
**Decision.** Split into services along bounded contexts (Claims, Disruption, Notification, …) because demonstrating distributed-systems competence is the explicit goal. Keep the number of services small and each internally modular.
**Consequences.** Higher operational cost (mitigated by compose + Kustomize + shared BuildingBlocks). In a real startup we would start as a modular monolith with the same module boundaries and extract services when team/scale demands it — say this in interviews.

## ADR-002 Kafka for events, RabbitMQ for commands (2026-11-09)
**Context.** Need fan-out of domain facts to several contexts with replay, *and* reliable single-consumer job processing with retries/DLQ.
**Decision.** Kafka (Event Hubs in Azure) for integration events; RabbitMQ via MassTransit 8 (Service Bus in Azure) for commands.
**Consequences.** Two brokers to run locally. Clear semantic split; each tool used for its strength. MassTransit pinned to v8 (Apache-2.0) because v9 is commercially licensed.

## ADR-003 Event sourcing only in Claims, on SQL Server with Dapper (2026-11-09)
**Context.** Claims need a full audit trail and temporal queries; other contexts do not.
**Decision.** Hand-rolled event store (single table, unique `(StreamId, StreamVersion)`), inline projection to `ClaimSummaries` in the same transaction, outbox for integration events. No EventStoreDB/Marten.
**Consequences.** Full understanding of every line (strong interview material); fewer features than a dedicated store (no subscriptions → polling by `GlobalPosition` for async projections).

## ADR-004 Qdrant for RAG vectors, Elasticsearch for product search (2026-11-09)
**Context.** Elasticsearch supports kNN; a second store adds ops cost.
**Decision.** Use ES (BM25 + geo + kNN hybrid) for hotel/route search; Qdrant for the RAG corpus, which has a different lifecycle (re-embedding, versioned collections, payload filters).
**Consequences.** One more container. Acceptable for the learning goal; collapsing RAG into ES or Azure AI Search is a documented cost optimisation.

## ADR-005 Kustomize over Helm (2026-11-09)
**Context.** Need per-environment differences (replicas, images, secrets) for ~6 deployments.
**Decision.** Kustomize base + overlays.
**Consequences.** Plain YAML, native to `kubectl`, easy to review. If we ever distribute the platform to others, wrap in a Helm chart.

## ADR-006 BFF (cookie session) instead of tokens in the SPA (2026-11-09)
**Context.** SPAs storing access tokens in JS-accessible storage are vulnerable to token exfiltration via XSS.
**Decision.** Gateway performs OIDC Authorization Code + PKCE, keeps tokens server-side, issues `__Host-` HttpOnly cookie, relays bearer tokens to services. CSRF mitigated via SameSite=Lax + required `X-CSRF` header.
**Consequences.** Gateway is stateful-ish (encrypted cookie holds tokens; Data Protection keys shared via Redis for multi-replica). Services remain stateless JWT resource servers.

## ADR-007 Own CQRS dispatcher instead of MediatR (2026-11-09)
**Context.** MediatR v13+ requires a commercial licence for some organisations; we need only send + decorators.
**Decision.** ~100 lines in `Reroute.BuildingBlocks/Cqrs` with explicit decorator pipeline.
**Consequences.** No reflection-heavy magic; explicit, debuggable. Slightly more registration code.
