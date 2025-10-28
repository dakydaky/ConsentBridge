# ADR 0001 – Consent UX & Tenant Authentication

| Field   | Value |
|---------|-------|
| Status  | Accepted |
| Date    | 2025-08-15 |
| Owners  | Consent Experience Squad |
| Tags    | consent, auth, ux, tenants |

## Context
Early ConsentBridge scaffolding exposed minimal APIs without any candidate-facing experience or hardened tenant authentication. Requirements captured across `docs/spec/consent-apply-v0.1.md`, `docs/spec/whitepaper.md`, and `docs/spec-multi.md` mandate:
- Candidates must explicitly authorise agents via a branded consent flow with verified contact info.
- Agents and boards act as tenants with scoped credentials and signing keys.
- Consent tokens contain scope, expiry, tenant binding, and revocation linkage.

The prototype issued tokens from bare JSON POSTs, accepted any application submission, and skipped tenant onboarding entirely. Product and compliance stakeholders required a rapid path to a legitimate consent UX with authenticated tenants while preserving the existing MockBoard demo surface.

## Problem Statement
We needed to introduce end-to-end consent issuance and tenant authentication that:
- Gives candidates a minimal but auditable experience (OTP verification, scope confirmation, explicit allow/deny).
- Provides agents/boards with persistent tenant identities, credentials, and signing configuration.
- Protects application APIs with OAuth-based access control.
- Remains achievable within MVP timelines and developer ergonomics.

## Decision Drivers
- **Regulatory expectations** — Explicit consent capture with evidence, revocation, and audit trails.
- **Demo readiness** — Enable investor/partner demos with a believable UX and secured APIs.
- **Incremental delivery** — Avoid blocking downstream teams while allowing evolution towards production identity providers.
- **Operational simplicity** — Seed tenants and credentials easily for local/dev environments.

## Options Considered
1. **Maintain API-only workflow** — Continue issuing tokens via JSON POST and insecure acceptance (Rejected).
2. **Full external IdP integration (Auth0/AAD)** — Rich feature set but heavy lift for MVP (Deferred).
3. **Gateway-hosted consent UX with tenant persistence and OAuth client credentials** (Chosen).

## Decision
Implement a phased Consent UX & Auth capability hosted in Gateway.Api, covering tenant onboarding, consent issuance, and secure API access while keeping MockBoard integration functional.

### Architecture Overview
- **Tenant provisioning** — Persist agent/board metadata, hashed client secrets, and JWKS endpoints in the gateway database. Initial tenants are seeded via CLI/scripts.
- **OAuth client credentials** — Expose `/oauth/token` issuing JWT access tokens with scope enforcement (`apply.submit`). Secrets stored hashed; tokens include tenant/board claims.
- **Consent web experience** — Razor-based flow (`/consent/{agent}/{board}`) handling email OTP verification, scope display, and explicit allow/deny responses. Successful approvals persist consent records and emit consent tokens (placeholder `ctok:` pre-ADR 0002).
- **Protected APIs** — `/v1/applications` and related endpoints require bearer tokens plus consent tokens; detached submission signatures validated via tenant JWKS.
- **Revocation and audit** — Consent lifecycle events captured to support revoke endpoints and audit reporting.

## Consequences
### Positive
- Aligns implementation with protocol expectations (auditable consent UI + tenant-authenticated APIs).
- Enables credible partner demos and unlocks downstream receipt/audit features.
- Establishes a foundation for later enhancements (key rotation, SDK tooling, revocation UX).

### Risks / Trade-offs
- Adds web UI, identity endpoints, and credential storage complexity to the gateway footprint.
- Requires coordination between Gateway.Api and Gateway.CertAuthority for signing operations.
- Introduces more configuration (client secrets, OTP settings) that teams must manage.

Mitigations include automated tenant seeding, dev scripts to bootstrap secrets, and staged rollout of additional security controls.

## Implementation Summary
1. Scaffold database tables for tenants, credentials, and consents; seed demo data.
2. Build consent Razor pages with OTP flow and success/denial outcomes.
3. Deliver `/oauth/token` client credential endpoint and update API authorisation policies.
4. Enforce bearer token validation on `/v1/applications` and wire detached submission signature verification.
5. Capture consent lifecycle events for revocation and audit endpoints.

## Follow-up Actions
- Execute ADR 0002 to replace placeholder `ctok:` tokens with signed per-tenant JWTs.
- Add rate limiting/throttling for OTP verification attempts.
- Introduce self-service tenant onboarding tooling when roadmap permits.

## References
- `docs/spec/consent-apply-v0.1.md`
- `docs/spec/whitepaper.md`
- `docs/spec-multi.md`
- `docs/design/consent-ux-auth-foundation.md`
