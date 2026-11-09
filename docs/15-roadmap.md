# Solo Build Roadmap (evenings + weekends, ~10–12 h/week)

The scaffold in this repo already contains phase 0 and most of phase 1's structure. Each phase ends in something **demoable and resume-worthy** on its own — if you stop after phase 2 you still have a strong project.

| Phase | Weeks | Goal | Done when | Resume line unlocked |
|---|---|---|---|---|
| **0. Foundations** | 1 | Repo, CI green, compose infra up, Keycloak realm, Grafana shows a trace | `docker compose up` + CI badge | — |
| **1. Claims vertical slice** ⭐ | 2–5 | Claims service end-to-end: event store, CQRS, outbox → Kafka, read model, HybridCache, BFF login, React claims pages, unit + integration tests | Log in → file claim → see timeline; tests green in CI | DDD/CQRS/ES, Clean Architecture, OAuth/JWT, Redis, Testcontainers |
| **2. Real-time disruptions** | 6–8 | Disruption service (Kafka consumer + Redis Lua), Notification service (RabbitMQ + SignalR), Itinerary minimal CRUD, simulator | Simulated cancellation → toast in < 2 s | Kafka, RabbitMQ, SignalR, distributed systems |
| **3. Observability & K8s** | 9–10 | Dashboards + alerts, k6 load test, kind cluster with Kustomize | Published p99 numbers & dashboard screenshot | OTel, Prometheus, Grafana, K8s |
| **4. AI** | 11–13 | Copilot RAG + eval, semantic search in ES, recommendation ranking | Copilot answers with citations; eval in CI | RAG, embeddings, vector DB, Elasticsearch |
| **5. Azure** | 14–15 | Bicep, AKS, Azure SQL, Service Bus, Event Hubs, Azure OpenAI, CD pipeline | Public demo URL (start on demand) | Azure, CI/CD, IaC |
| **6. Polish** | 16 | README, demo GIF, ADRs, blog post, ledger service (stretch) | Pinned repo looks like a product | — |

## Weekly rhythm
- **Weeknight (2 × 1.5 h):** one small vertical change + test.
- **Weekend (2 × 3 h):** one bigger capability; end with a commit + short entry in `docs/journal.md` (great raw material for interviews).

## Scope guards (cut before you burn out)
- Rebooking, Ledger, Itinerary email import = **stretch**. Document them; build only if time allows.
- Use mocked flight data (the simulator) — do not integrate paid flight APIs.
- One frontend design system — don't hand-craft CSS for days; Tailwind or plain CSS modules are fine.
