# Part 9 — Docker, Docker Compose & Kubernetes

## 9.1 Docker
Every service uses the same multi-stage pattern ([`src/Services/Claims/Reroute.Claims.Api/Dockerfile`](../src/Services/Claims/Reroute.Claims.Api/Dockerfile)):

| Stage | Base | Purpose |
|---|---|---|
| `build` | `mcr.microsoft.com/dotnet/sdk:10.0` | Restore (layer-cached by copying only `*.csproj` + props first), publish Release. |
| `final` | `mcr.microsoft.com/dotnet/aspnet:10.0` (or `-noble-chiseled`) | Runtime only, **non-root** (`USER $APP_UID`), port 8080, no shell in chiseled image → smaller attack surface (~110 MB vs ~800 MB). |

Practices: `.dockerignore`, deterministic builds, `HEALTHCHECK` delegated to K8s probes, image labels (`org.opencontainers.image.source/revision`) set by CI, Trivy scan in CI.

Frontend: Node build stage → `nginx:alpine` serving static files with SPA fallback and long-cache headers for hashed assets (in Azure it is deployed to App Service instead).

## 9.2 Docker Compose (local dev)
[`docker-compose.yml`](../docker-compose.yml) brings up **everything** with one command:

| Group | Containers |
|---|---|
| Infra | `sqlserver`, `redis`, `kafka` (KRaft, no ZooKeeper), `kafka-ui`, `rabbitmq` (+management UI), `elasticsearch`, `qdrant`, `keycloak` (realm auto-imported), `mailpit` |
| Observability | `otel-collector`, `prometheus`, `tempo`, `loki`, `grafana` (datasources + dashboards provisioned) |
| App | `gateway`, `claims-api`, `disruption-api`, `notification-api`, `copilot-api`, `search-api` |

```bash
docker compose up -d --build        # everything
docker compose up -d sqlserver redis kafka rabbitmq keycloak   # infra only, run services from IDE
```

Design choices: health-check-gated `depends_on`, named volumes, one shared network, `.env` for secrets (git-ignored; `.env.example` committed), memory limits on Elasticsearch so a laptop survives.

| URL | What |
|---|---|
| http://localhost:5173 | React app (Vite dev) |
| http://localhost:8080 | Gateway/BFF |
| http://localhost:8081 | Keycloak (admin/admin) |
| http://localhost:3000 | Grafana |
| http://localhost:9090 | Prometheus |
| http://localhost:15672 | RabbitMQ UI |
| http://localhost:8085 | Kafka UI |
| http://localhost:8025 | Mailpit (captured emails) |

## 9.3 Kubernetes
Manifests live in [`deploy/k8s`](../deploy/k8s) using **Kustomize** (built into `kubectl`, no templating language to learn; Helm is the alternative — ADR-005).

```
deploy/k8s/
├── base/
│   ├── kustomization.yaml
│   ├── namespace.yaml
│   ├── claims-api.yaml          # Deployment + Service + HPA + PDB
│   ├── gateway.yaml
│   ├── disruption-api.yaml
│   ├── notification-api.yaml
│   ├── ingress.yaml             # NGINX ingress / App Gateway for Containers
│   ├── network-policies.yaml    # default-deny + explicit allows
│   └── migrations-job.yaml
└── overlays/
    ├── dev/kustomization.yaml    # 1 replica, local images
    └── prod/kustomization.yaml   # ACR images by digest, 2+ replicas, Key Vault CSI
```

### Production-grade details in every Deployment
| Feature | Setting | Why |
|---|---|---|
| Probes | `startupProbe` `/health/startup`, `readinessProbe` `/health/ready` (checks DB, Redis, Kafka), `livenessProbe` `/health/live` (process only) | Liveness must **not** check dependencies — otherwise a DB blip restarts every pod (cascading failure). |
| Resources | requests + limits (memory limit, no CPU limit) | CPU limits cause throttling latency spikes; memory limits prevent noisy neighbours. |
| HPA | CPU 70 % + custom metric (Kafka lag via KEDA in stretch) | Scale consumers on lag, not CPU. |
| PodDisruptionBudget | `minAvailable: 1` | Node upgrades don't take the service down. |
| Topology spread | across zones/nodes | Survive node loss. |
| Security context | `runAsNonRoot`, `readOnlyRootFilesystem`, drop ALL capabilities | Pass Pod Security "restricted". |
| Graceful shutdown | `terminationGracePeriodSeconds: 30` + `preStop` sleep 5 | Let ingress drain before SIGTERM; Kafka consumer commits & leaves group. |
| Config | ConfigMap for non-secrets; secrets via **Azure Key Vault CSI driver** + **Workload Identity** | No secrets in Git or images. |
| NetworkPolicy | default deny; gateway → services; services → data | Zero-trust inside the cluster. |

Stateful dependencies (SQL, Redis, Service Bus/Event Hubs, OpenAI) are **managed Azure services in prod**, not pods — running databases on K8s alone at night is a reliability anti-pattern. For the local K8s (kind) demo, the dev overlay points to compose-hosted infra.

Next: [Part 10 — Azure](10-azure.md)
