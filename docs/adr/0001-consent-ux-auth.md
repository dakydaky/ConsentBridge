# ADR 0001 – Consent UX & Tenant Auth

## Status
Proposed – awaiting stakeholder buy‑in before implementation.

## Context
- Current scaffold exposes minimal API endpoints without any candidate-facing consent flow or tenant authentication (see `README.md` “Next steps”).
- Domain specs (`docs/spec/consent-apply-v0.1.md`, `docs/spec/whitepaper.md`, `docs/spec-multi.md`) assume:
  - Candidates grant explicit, auditable consent via a UI branded to the agent.
  - Agents are authenticated tenants with scoped credentials and JWKS for signing / verification.
  - Consent tokens carry scope, expiry, tenant binding, and revocation awareness.
- Today the API issues consent tokens from a plain JSON POST and trusts every request via an `AcceptAllVerifier`.
- To ship an MVP that meets compliance and investor demo expectations we must introduce a proper consent UX, tenant onboarding, and token-based auth while keeping the existing demo surface operable.

## Decision
We will implement a phased Consent UX & Auth capability that covers tenant onboarding, consent issuance, and secure API access while retaining the existing MockBoard integration.

1. **Tenant Provisioning**
   - Persist tenant (agent/board) metadata, credentials, and JWKS endpoints in the gateway database.
   - Provide an internal admin CLI/seed path for now; promote to self-serve onboarding later.
2. **Client Credentials OAuth Flow**
   - Issue per-tenant client_id/client_secret and expose `/oauth/token` for machine-to-machine access (Client Credentials Grant).
   - Store hashed secrets; enforce scope (`apply.submit`) on issued tokens.
3. **Consent Web Experience**
   - Build an ASP.NET Core Razor/Blazor module (or small SPA) served from Gateway.Api under `/consent/{agentTenantId}/{boardTenantId}`.
   - Flow: candidate email verification → scope display → explicit allow/deny → persisted consent + signed consent token.
4. **Consent Tokens**
   - Replace the current `ctok:{guid}` with JWT/JWS tokens signed by the platform (`Gateway.CertAuthority`).
   - Token claims: `sub` (candidate), `agent`, `board`, `scopes`, `exp`, `jti`, plus consent ID reference.
5. **Protected APIs**
   - Require bearer access token + consent token on `/v1/applications`.
   - Inject `IJwsVerifier` implementation that validates detached signatures using tenant JWKS.
6. **Revocation & Audit**
   - Track consent lifecycle events, expose `/v1/consents/{id}` GET, and ensure revoked consents block new applies even with cached tokens.

We will iterate from CLI-seeded tenants + simple Razor pages, and evolve towards production-ready identity (Azure AD/Entra, Auth0, etc.) once MVP is validated.

## Consequences
### Positive
- Aligns implementation with spec expectations (auditable consent, tenant auth, scoped tokens).
- Enables real partner demos showcasing branded consent UI + secure API usage.
- Establishes foundation for future features: DSR endpoints, JWKS rotation, multi-tenant analytics.

### Neutral / Trade-offs
- Introduces additional components (web UI, identity endpoints) increasing deployment complexity.
- Requires coordination between Gateway.Api and Gateway.CertAuthority for signing keys; initial version will likely use static keys with rotation backlog.
- Adds configuration/secret management needs (client secrets, signing keys).

### Risks / Mitigations
- **Security debt** if we stub cryptography again – mitigate by prioritizing real signing pipeline before public exposure.
- **UI scope creep** – keep the consent experience minimal (responsive form, confirmation page) until UX resources are available.
- **Operational overhead** – ensure docker-compose/dev scripts bootstrap default tenants and keys to keep developer workflow simple.
