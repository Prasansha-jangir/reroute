# Part 1 — Project Idea: **Reroute**

> **Reroute is a real-time travel-disruption platform.** It watches every flight, train and hotel booking a traveller has, detects disruptions within seconds (delays, cancellations, strikes), tells the traveller what their rights are, proposes rebooking alternatives, and files + tracks **EU261 compensation claims** end-to-end — up to €600 per passenger — with an AI copilot that explains the law using the actual regulation text.

## 1.1 The real-world problem

| Fact | Why it matters |
|---|---|
| ~**1 in 4** European flights is delayed >15 min in a typical summer; tens of thousands are cancelled per year. | Huge, recurring event volume → genuine streaming / distributed-systems problem. |
| **EU Regulation 261/2004** entitles passengers to €250–€600 for long delays/cancellations, but the vast majority never claim. | Clear user value; a real multi-billion-euro market (AirHelp, Flightright, ClaimCompass exist — proof it is real). |
| Rules are complex (distance bands, "extraordinary circumstances", rerouting reductions, 14-day notice). | Rich **domain** → DDD is justified, not decoration. |
| Disruption is **time-critical**: the first traveller to rebook gets the last seat / last hotel room near the airport. | Low-latency event processing, caching, holds with TTLs, distributed locks. |
| Claims involve money, legal deadlines and audit trails. | **Event sourcing**, idempotency, outbox, double-entry ledger (Adyen/ING signal). |

It is **not** an e-commerce app, social clone, stock/hospital/todo app. It is the kind of system that sits *behind* Booking.com's "your flight is delayed" email, Uber's "your driver is rerouting" event, or Adyen's payout ledger.

## 1.2 Core capabilities (bounded contexts)

1. **Itinerary** — travellers import trips (manual, email forward → AI extraction later).
2. **Disruption detection** — ingest a flight-status firehose (Kafka), detect state transitions in < 1 s.
3. **Impact & notification** — find affected travellers, notify in real time (SignalR) and async (email/push via RabbitMQ).
4. **Rebooking & search** — alternative flights/trains/hotels near the airport (Elasticsearch, geo + hybrid search).
5. **Claims** — EU261 eligibility engine + event-sourced claim lifecycle + payout ledger.
6. **AI Copilot** — RAG over EU261, airline Conditions of Carriage and the traveller's own trip; personalised ranking of alternatives (recommendation engine).

## 1.3 Why recruiters / hiring managers will find it impressive

A typical 1.5-YOE resume says *"Built CRUD APIs in ASP.NET Core and React screens."* Reroute lets you say things only mid-level+ engineers usually can:

| Signal a Staff Engineer looks for | How Reroute demonstrates it |
|---|---|
| **You can model a messy domain** | EU261 compensation rules as a pure, 100%-unit-tested domain policy; aggregates with invariants; ubiquitous language (*Claim*, *Disruption*, *ExtraordinaryCircumstance*). |
| **You understand consistency trade-offs** | Event-sourced aggregate with optimistic concurrency; transactional outbox; idempotent consumers (inbox); read-your-writes via inline projection vs. eventual consistency elsewhere — *documented in ADRs*. |
| **You can choose tools, not just use them** | Kafka **and** RabbitMQ, each for a reason you can defend in an interview (facts/streams vs. commands/work-queues). Elasticsearch **and** a vector DB, with a written trade-off. |
| **You think about failure** | Retries with jitter, DLQs, circuit breakers, poison messages, back-pressure, consumer lag alerts, SLOs. |
| **You can operate what you build** | OpenTelemetry traces across HTTP → Kafka → RabbitMQ → SignalR; Grafana dashboards; Prometheus alerts; K8s probes, HPA, PDBs. |
| **Security maturity** | OAuth 2.0 / OIDC with Authorization Code + PKCE through a **BFF** (tokens never reach the browser), JWT validation in every service, policy-based authZ, rate limiting. |
| **Modern AI done responsibly** | RAG with citations, grounding checks, evaluation set, prompt-injection mitigations, cost/latency telemetry per request. |
| **Shipping discipline** | GitHub Actions CI/CD with Testcontainers integration tests, image scanning, OIDC-federated deploys to AKS — no secrets in the repo. |

**The interview hook:** every one of the 32 topics maps to a *concrete file in your repo* you can open on a screen-share. "Tell me about a time you handled eventual consistency" → open `ClaimProjection.cs` and `OutboxPublisher.cs`.

## 1.4 Which European companies value which skills

| Company (NL/EU) | What they build | Reroute features that resonate | Talking point |
|---|---|---|---|
| **Booking.com** (Amsterdam) | Travel marketplace, search, availability at massive scale | Travel domain, Elasticsearch search/ranking, caching, experimentation-friendly React | "I built disruption-aware rebooking with geo + hybrid search over hotels near airports." |
| **Adyen** (Amsterdam) | Payments, ledgers, extreme correctness | Event sourcing, idempotency keys, double-entry payout ledger, outbox | "Every payout is idempotent and every euro is traceable in an append-only ledger." |
| **Uber** (Amsterdam EU HQ) | Real-time marketplaces, Kafka at scale | Kafka streaming, partitioning by key, real-time push, SLO-driven ops | "Disruptions go from Kafka to the traveller's screen in < 2 s p99." |
| **Microsoft** (Dublin, Prague, NL sales/eng) | Azure, .NET, enterprise | ASP.NET Core, AKS, Azure OpenAI, Bicep, Entra ID, App Service | Full Azure-native deployment with IaC and federated identity. |
| **Amazon** (Amsterdam, Berlin, Dublin, Luxembourg) | Distributed services, ownership | Service boundaries, ops, metrics, cost awareness | Leadership Principles stories: *Dive Deep* (tracing), *Frugality* (cost-tuned AKS). |
| **Databricks** (Amsterdam R&D) | Data + AI platforms | Event log as data product, RAG, embeddings, evaluation | Kafka topics designed as replayable data products; RAG evaluation harness. |
| **Optiver** (Amsterdam) | Low-latency trading | Latency budgets, back-pressure, correctness under concurrency | Optimistic concurrency, p99 dashboards, lock-free hot path in Redis. |
| **Picnic** (Amsterdam) | Event-driven grocery logistics | Event-driven architecture, real-time ops | Same patterns as their order/delivery event pipeline. |
| **ING / Rabobank** (Amsterdam / Utrecht) | Banking platforms, Kafka-heavy, regulated | Audit trails, event sourcing, security, compliance-friendly design | "Regulated-domain mindset: immutable audit log, GDPR data-erasure strategy." |

## 1.5 Honest advice from the "Staff Engineer" panel

1. **Depth beats breadth.** A recruiter skims; an interviewer drills. Build **one service (Claims) to production quality** first, then widen. This repo's scaffold follows that: Claims is the reference implementation; other services follow its pattern.
2. **Every tech needs a "why".** If you can't explain why RabbitMQ *and* Kafka, an interviewer will assume résumé-driven development. The ADRs in [`docs/adr`](adr) give you those answers.
3. **Make it demoable in 60 seconds.** A recorded GIF in the README: simulate a cancellation → toast appears → copilot explains rights → claim filed → event timeline. That GIF does more than the architecture diagram.
4. **Publish numbers.** "p99 120 ms at 500 RPS (k6)" beats "scalable".
5. **Cost-control Azure.** Run AKS only for demos; keep the always-on footprint tiny (see Part 10).

Next: [Part 2 — Architecture](02-architecture.md)
