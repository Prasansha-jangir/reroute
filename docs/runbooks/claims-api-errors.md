# Runbook — ClaimsApiErrorBudgetFastBurn

**Meaning:** the Claims API is returning 5xx fast enough to exhaust its 30-day error budget in ~2 days.

1. **Scope** — Grafana "Reroute — Service & Pipeline Overview": is it all routes or one? One replica or all?
2. **Trace** — click an exemplar on the p99 panel → Tempo trace → which span failed (SQL? Redis? Kafka producer?).
3. **Dependencies** — `kubectl -n reroute get pods`; readiness failing means `/health/ready` sees SQL or Redis down.
   - SQL: Azure portal → database → is it auto-paused / DTU-throttled?
   - Redis down: reads fall back to SQL (HybridCache L1 still serves); expect higher latency, not errors.
4. **Recent change?** — `kubectl -n reroute rollout history deploy/claims-api`; roll back with `kubectl rollout undo` if it correlates.
5. **Concurrency spike** — 409s are *not* 5xx; if `ConcurrencyException` appears as 500, the exception handler is not registered.
6. **After** — write a short post-incident note in `docs/journal.md` (great interview material).
