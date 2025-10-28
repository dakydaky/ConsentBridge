# ADR 0006 – Baseline Deployment Topology

| Field   | Value |
|---------|-------|
| Status  | Proposed |
| Date    | 2025-10-28 |
| Owners  | Platform Team |
| Tags    | deployment, kubernetes, infrastructure |

## Context
ConsentBridge currently runs via docker-compose for demos. As we prepare an MVP for pilot customers, we need a production-capable topology that supports:
- Multi-tenant gateway APIs with persistence, background jobs, and adapters.
- Secure management of signing keys (ADR 0002) and audit trails (ADR 0003).
- Observability stack (ADR 0004) and configuration approach (ADR 0005).

The team aims to remain cloud-agnostic until go-to-market commitments solidify, while leveraging industry-standard tooling.

## Problem Statement
Select a baseline deployment architecture that:
- Provides redundancy and scalability for stateless services.
- Handles stateful dependencies (PostgreSQL, Redis/queue) with managed offerings where possible.
- Supports GitOps/CI pipelines with predictable promotion paths.
- Minimises operational burden for a startup, yet leaves room for compliance attestation.

## Decision Drivers
- **Reliability** — Ensure core APIs remain available with rolling updates and health checks.
- **Security** — Isolate secrets, secure networking, and support future compliance controls.
- **Cost & simplicity** — Favour managed databases/message brokers to reduce toil.
- **Portability** — Keep ability to run locally (kind/minikube) and migrate between clouds.

## Options Considered
1. **Scale docker-compose** — Simple but lacks orchestration, self-healing, or production-grade networking (Rejected).
2. **Fully managed PaaS per component (Azure App Service, RDS, etc.)** — Reduces ops but fragments deployment story and complicates cross-cloud strategy (Deferred).
3. **Kubernetes-based topology with managed database/cache and GitOps pipeline** (Chosen).

## Decision
Adopt Kubernetes as the orchestration layer for ConsentBridge services, paired with managed PostgreSQL and Redis, deployed via Helm charts under a GitOps workflow.

### Architecture Overview
- **Control plane** — Managed Kubernetes service (AKS/EKS/GKE); development clusters run via `kind` or `k3d`.
- **Services**
  - `gateway-api` (ASP.NET Core) – stateless deployment, horizontal pod autoscaling.
  - `gateway-background` – hangfire/worker jobs for sweeps and key rotation.
  - `gateway-certauthority` – issues signing keys, exposed internally.
  - `mockboard-adapter` – optional demo namespace.
  - `otel-collector`, `seq`, `prometheus`, `grafana` per ADR 0004.
- **Stateful dependencies**
  - PostgreSQL managed service (e.g., Azure Database for PostgreSQL Flexible Server or AWS RDS) accessed over TLS; schema migrations run via CI/CD job.
  - Redis (managed) for distributed caching/OTP throttling.
  - Object storage bucket for audit hash exports and receipts archive (S3-compatible).
- **Networking**
  - Ingress controller (NGINX) with TLS termination; internal services behind Kubernetes services.
  - mTLS between gateway components considered stretch goal; start with network policies isolating namespaces.
- **Deployment workflow**
  - CI builds container images, pushes to registry.
  - GitOps repo stores Helm chart values; changes merged to `main` trigger Argo CD / Flux reconcile.
  - Secrets referenced via SOPS-encrypted manifests (ADR 0005).
- **Environments**
  - `dev` (shared cluster), `staging`, `production` with promotion pipeline. Feature environments optional via namespace templates.

## Consequences
### Positive
- Provides scalable, self-healing infrastructure aligned with industry norms.
- Clear separation of stateless services and managed stateful resources.
- Compatible with observability, configuration, and security ADRs.

### Risks / Mitigations
- **Operational overhead of Kubernetes** — Mitigate with managed control plane, automated Helm charts, and documented runbooks.
- **GitOps complexity** — Start with a single Argo CD app per environment; invest in tooling gradually.
- **Cost** — Run minimal node pools (spot/standard mix) and right-size managed services in early stages.

## Implementation Plan
1. Produce Helm charts for each service and shared infrastructure components.
2. Stand up `kind` dev cluster configuration mirroring production manifests for local testing.
3. Provision managed PostgreSQL/Redis in staging; configure network rules and TLS.
4. Integrate GitOps controller (Argo CD) and pipeline steps for image build → chart update → deploy.
5. Document deployment runbooks, including disaster recovery and scaling procedures.

## Follow-up Actions
- Evaluate service mesh (Linkerd/Istio) once zero-trust networking becomes mandatory.
- Automate blue/green deployments or canary rollout once traffic volumes justify.
- Certify infrastructure against compliance requirements (SOC 2) as part of growth plan.

## References
- ADR 0002 – Signed Consent Tokens & Key Rotation
- ADR 0003 – Immutable Audit Trail Persistence
- ADR 0004 – Observability & Telemetry Stack
- ADR 0005 – Configuration & Secrets Management
