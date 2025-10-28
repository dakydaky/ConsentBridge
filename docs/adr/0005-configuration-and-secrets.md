# ADR 0005 – Configuration & Secrets Management

| Field   | Value |
|---------|-------|
| Status  | Proposed |
| Date    | 2025-10-28 |
| Owners  | Platform Reliability |
| Tags    | configuration, security, operations |

## Context
Services in ConsentBridge currently pull configuration from `appsettings.json` and, in local demos, rely on in-repo sample secrets. As we move toward production, the platform will run across multiple environments (local → staging → production) with sensitive material including:
- Database connection strings and credential secrets.
- Tenant signing keys (ADR 0002), board JWKS endpoints, and OAuth client secrets.
- SMTP/OTP providers, telemetry exporters, third-party API keys.

Persisting secrets in source control or static JSON is unacceptable. We need a cohesive approach that balances developer ergonomics with production-grade security.

## Problem Statement
Define how configuration is sourced, overridden, and protected across environments so that:
- Sensitive values never live in plaintext inside the repo or container images.
- Configuration changes are auditable and version-controlled when appropriate.
- Developers have a simple workflow for local testing without elevated access to production secrets.
- The solution aligns with our Kubernetes-based deployment roadmap (ADR 0006).

## Decision Drivers
- **Security & compliance** — Protect tenant keys and credentials; support rotation.
- **Developer velocity** — Minimise friction for local setup and CI pipelines.
- **Portability** — Avoid locking into cloud-specific secret managers before necessity.
- **Observability & traceability** — Be able to audit configuration changes and know which version is deployed.

## Options Considered
1. **All configuration in appsettings + git-crypt** — Keeps repo-based workflow but couples secrets to git and complicates rotation (Rejected).
2. **Adopt cloud-secret manager now (AWS/GCP/Azure)** — Strong security but ties us prematurely to a cloud provider and complicates local/kind setups (Deferred).
3. **Layered configuration: appsettings for non-sensitive defaults, `.env` for local secrets, Kubernetes Secrets for deployments, SOPS-encrypted manifests stored in git** (Chosen).

## Decision
Adopt layered configuration with environment-variable precedence, backed by SOPS-encrypted Kubernetes secrets for managed environments, and `.env` files for local development.

### Architecture Overview
- **Configuration layers**
  1. `appsettings.json` / `appsettings.{Environment}.json` – non-sensitive defaults.
  2. Environment variables – canonical override mechanism (Docker/Kubernetes).
  3. `.env` files – local development only (`.env.development`), ignored by git, loaded via `dotenv.net`.
  4. Command-line arguments – final override for tooling/scripts.
- **Secret management**
  - **Local/dev**: `.env.development` populated via `wire-up.ps1` & `make bootstrap`. Tenant demo keys stored under `/certs` with gitignored private material.
  - **CI/Staging/Production**: Secrets authored as YAML manifests encrypted with Mozilla SOPS (age). Stored in `deploy/secrets/` and decrypted only during deployment pipeline using environment-specific keys.
  - Kubernetes Secrets created by CI pipeline post-decryption; pods mount secrets as environment variables or files.
  - Long-term plan allows migration to managed secret store by swapping SOPS backend (e.g., AWS KMS) without restructuring applications.
- **Rotation**
  - Rotation playbooks define regeneration flow (update SOPS file, re-encrypt, apply). For tenant signing keys see ADR 0002.
  - CI validates SOPS files decrypt before applying; deployments fail fast on mismatch.
- **Auditability**
  - SOPS files remain versioned (encrypted), providing change history without exposing plaintext.
  - Terraform/Helm charts reference secret names, not values, keeping infra code declarative.

## Consequences
### Positive
- Sensitive data stays out of source control while maintaining GitOps-friendly workflows.
- Consistent configuration story across services, aligning with .NET configuration providers.
- Easy local developer onboarding via `.env` templates and scripts.

### Risks / Mitigations
- **SOPS key loss** — Back up age private keys in password manager; document recovery steps.
- **Manual rotation overhead** — Provide scripts (`make rotate-secret`) to regenerate and re-encrypt common secrets.
- **Developer confusion** — Document load order clearly and supply sample `.env.example`.

## Implementation Plan
1. Integrate `dotenv.net` into services and adjust bootstrap scripts to generate `.env.development`.
2. Create `.env.example` documenting required variables; update README setup instructions.
3. Introduce SOPS tooling into repo (`.sops.yaml`), bootstrap age keys, and author encrypted secret manifests for staging.
4. Update deployment pipeline to decrypt with environment-specific keys and create Kubernetes Secrets.
5. Document rotation playbooks and runbook for onboarding new environments.

## Follow-up Actions
- Evaluate migration to managed cloud secret stores once we commit to a primary cloud provider.
- Add automated checks to ensure no plaintext secrets are committed (secret scanning).
- Extend observability (ADR 0004) with alerts on configuration reload failures.

## References
- ADR 0002 – Signed Consent Tokens & Key Rotation
- ADR 0004 – Observability & Telemetry Stack
- ADR 0006 – Deployment Topology
