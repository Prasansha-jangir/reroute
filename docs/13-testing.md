# Part 13 — Testing Strategy

```
            ▲  few, slow, expensive
           / \      E2E (Playwright)              ~10 journeys
          /   \     Contract tests (OpenAPI diff)  per API
         /     \    Integration (Testcontainers)   ~40 tests
        /       \   Component (WebApplicationFactory, Vitest+MSW)
       /_________\  Unit (domain, handlers, hooks)  ~200 tests, < 5 s
            many, fast, cheap
```

## 13.1 Unit tests (xUnit v3 + Shouldly + NSubstitute)
**Focus: the domain.** It contains the money logic and has zero dependencies → fast and deterministic.

| Target | Technique | Example |
|---|---|---|
| `Eu261CompensationPolicy` | Table-driven `[Theory]` covering every distance band, delay boundary (179 vs 180 min), notice period, extraordinary circumstances, rerouting reduction | [`Eu261CompensationPolicyTests.cs`](../tests/Reroute.Claims.UnitTests/Eu261CompensationPolicyTests.cs) |
| `Claim` aggregate | **Given-When-Then on events**: given past events, when command, then expected new events / error | [`ClaimAggregateTests.cs`](../tests/Reroute.Claims.UnitTests/ClaimAggregateTests.cs) |
| Handlers | Fake repository (in-memory), `FakeTimeProvider` | |
| React hooks/components | Vitest + Testing Library + MSW | `ClaimTimeline.test.tsx` |

Rules: no mocking of what you own when a fake is simpler; test behaviour (events), not implementation (private fields); time is injected (`TimeProvider`), never `DateTime.UtcNow`.

## 13.2 Integration tests (Testcontainers)
- `ClaimsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` starts a **real SQL Server container**, runs migrations, replaces JWT auth with a test auth handler.
- Tests go through HTTP: `POST /api/claims` → `GET /api/claims/{id}` → assert read model, assert event stream rows, assert outbox row exists.
- **Concurrency test**: two parallel commands on the same claim → exactly one succeeds, the other gets `409`.
- Kafka test: start `KafkaContainer`, run the outbox publisher, consume the topic, assert message + `traceparent` header.
- Containers are shared per test collection (`ICollectionFixture`) → startup cost paid once.

[`ClaimsApiTests.cs`](../tests/Reroute.Claims.IntegrationTests/ClaimsApiTests.cs)

## 13.3 Architecture tests
Reflection-based tests that fail the build if `Reroute.Claims.Domain` references Infrastructure, Dapper, ASP.NET, etc. → architecture can't silently rot. [`LayerDependencyTests.cs`](../tests/Reroute.ArchitectureTests/LayerDependencyTests.cs)

## 13.4 E2E tests (Playwright)
- Run against `docker compose up` in CI (nightly + on `main`).
- Journey: log in (Keycloak test user) → add trip → trigger simulated cancellation via ops endpoint → toast appears (SignalR) → file claim → timeline shows `ClaimFiled` + `EligibilityAssessed`.
- Accessibility assertions with `@axe-core/playwright`.
- Traces/videos uploaded as artifacts on failure.

## 13.5 Non-functional tests
| Type | Tool | Target |
|---|---|---|
| Load | **k6** (`tests/load/claims.js`) | 500 RPS on `GET /api/claims`, p99 < 300 ms; publish results in README |
| Resilience | Kill Kafka/Redis container during load test | No data loss, recovery < 30 s |
| Contract | `oasdiff` on generated OpenAPI in CI | Breaking changes fail PR |
| Security | CodeQL, `dotnet list package --vulnerable`, Trivy, OWASP ZAP baseline (stretch) | |
| RAG quality | Nightly eval (Part 8) | hit@8 ≥ 0.9, faithfulness ≥ 0.85 |

Next: [Part 14 — Resume Impact](14-resume.md)
