# Part 12 — CI/CD with GitHub Actions

```mermaid
flowchart LR
    PR[Pull request] --> CI
    subgraph CI["ci.yml (every PR + push)"]
        direction TB
        CH[Detect changed paths] --> BE[Backend: restore, build -warnaserror, unit tests, coverage]
        CH --> FE[Frontend: lint, typecheck, vitest, build]
        BE --> IT[Integration tests<br/>Testcontainers: SQL Server, Kafka, Redis]
        BE --> SEC[Security: CodeQL, dependency review, secret scan]
        FE --> E2E[Playwright E2E (main only, nightly)]
    end
    CI -->|merge to main| CD
    subgraph CD["cd.yml"]
        direction TB
        IMG[Build images (matrix per service)<br/>tag = git SHA, push to ACR] --> SCAN[Trivy scan: fail on CRITICAL]
        SCAN --> SBOM[SBOM + provenance attestation]
        SBOM --> DEV[Deploy dev: migrations Job → kustomize apply → rollout status → smoke test]
        DEV --> APPR{Manual approval<br/>GitHub Environment 'prod'}
        APPR --> PROD[Deploy prod: same digests]
        DEV --> SPA[Deploy SPA to App Service staging slot → swap]
    end
```

Files: [`.github/workflows/ci.yml`](../.github/workflows/ci.yml), [`.github/workflows/cd.yml`](../.github/workflows/cd.yml), [`.github/workflows/nightly.yml`](../.github/workflows/nightly.yml) (E2E + k6; RAG eval is a roadmap item), [`.github/dependabot.yml`](../.github/dependabot.yml).

## Key decisions

| Decision | Why |
|---|---|
| **Path filters** (`dorny/paths-filter`) | Monorepo: only build what changed → fast feedback. |
| **Build once, deploy many** — promote the same image **digest** dev → prod | What you tested is what you ship. |
| **OIDC federation** (`azure/login` with `id-token: write`) | No long-lived Azure secrets in GitHub. |
| **Testcontainers in CI** | GitHub-hosted Ubuntu runners have Docker → real SQL Server/Kafka in tests, no mocks for infra. |
| **Concurrency groups** | Cancel superseded runs on the same branch; serialise deploys per environment. |
| **Required checks + branch protection** | `main` is always deployable. |
| **Caching** NuGet (`actions/setup-dotnet` cache) and npm | Halves CI time. |
| **Test reports** (`dorny/test-reporter`) + coverage summary in job summary | Visible quality in every PR — screenshots of this are great for the README. |
| **Dependabot** for NuGet, npm, Actions, Docker | Supply-chain hygiene. |
| **Smoke test after deploy** | `curl /health/ready` + one synthetic API call; auto-rollback with `kubectl rollout undo` on failure. |

## Release flow for a solo developer
- Trunk-based: short-lived branches, PR to `main` (yes, PR to yourself — it gives you CI history and a clean narrative).
- Conventional commits → automatic changelog (`release-please`) — optional polish.

Next: [Part 13 — Testing](13-testing.md)
