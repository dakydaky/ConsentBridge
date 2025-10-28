# ADR 0002 – Signed Consent Tokens & Key Rotation

| Field   | Value |
|---------|-------|
| Status  | Accepted |
| Date    | 2025-10-28 |
| Owners  | Platform Team |
| Tags    | security, auth, tokens, compliance |

## Context
ConsentBridge currently issues consent tokens (`ctok:`) as opaque GUID strings whenever a candidate approves a consent flow. These placeholders unblock demo scenarios but fall short of the protocol commitments in `docs/spec/consent-apply-v0.1.md`:
- Boards must be able to verify which tenant issued a consent, its scopes, and its expiry without a round-trip.
- Tokens must be tamper-evident and revocable, with deterministic identifiers (`jti`) that gateway services can audit.
- Tenants expect key rotation and per-tenant signing to fulfil compliance obligations and regional cryptography regulations.

Downstream components (gateway application ingestion, MockBoard adapter, auditing pipeline) are being expanded to rely on trustworthy consent attestations. Continuing with opaque tokens blocks this work and erodes our security posture.

## Problem Statement
We need a production-ready consent token that:
- Encodes consent assertions (subject, agent, board, scopes, expiry, consent ID) in a verifiable format.
- Supports per-tenant validation and rotation of signing material.
- Remains compatible with offline verification by boards and adapters.
- Provides deterministic identifiers for audit and revocation workflows.

## Decision Drivers
- **Security & Compliance** — Tokens must be cryptographically verifiable, traceable, and support regional key management policies.
- **Ecosystem Interoperability** — Boards/ATS partners need a standard JWT/JWS they can inspect without bespoke integration.
- **Operational Simplicity** — Developers should be able to rotate keys, invalidate tokens, and seed demo tenants without complex tooling.
- **Backward Compatibility** — Existing demo flows must continue to operate during migration.

## Options Considered
1. **Status quo (`ctok:{guid}` opaque token).** Minimal work but no verification or claims; fails security requirements.
2. **Platform-signed JWT using a single global key.** Improves verification but concentrates risk, complicates regional tenancy, and creates serial rotation outages.
3. **Per-tenant JWT with detached JWS signatures and rotating keypairs.** Introduces stronger isolation and aligns with roadmap expectations (Chosen).

## Decision
Adopt option 3: consent tokens become JWTs signed with per-tenant asymmetric keys managed by Gateway. Tokens include consent metadata claims and are distributed with detached JWS signatures to support verification by boards.

Key elements:
- **Token format** — Compact JWT (`alg=ES256`, `typ=JWT`) with claims: `iss`, `sub`, `aud`, `agent`, `board`, `cid`, `scope`, `iat`, `exp`, `jti`, `ver`.
- **Detached JWS** — Tokens are issued alongside detached payload signatures so boards can validate without contacting Gateway.
- **Key hierarchy** — Each tenant gets a keyset (current + next) maintained in `tenant_keys` table with versioning and rotation dates.
- **Rotation policy** — Rolling rotations with overlap: `current` and `next` keys valid simultaneously, enabling token issuance on new key while honouring existing tokens during grace period.
- **Storage** — Private keys encrypted at rest (DPAPI/local dev; KMS/HSM in prod). Public keys exposed via tenant JWKS endpoints as already referenced in roadmap.
- **Revocation** — `jti` stored for each issued token to support revocation lists and audit.

## Architecture Overview
- Gateway Certification Authority service issues tenant key pairs (ES256). Keys are persisted and exposed through JWKS endpoints.
- Consent issuance flow uses the tenant’s active signing key to create the JWT and returns both compact token and detached JWS header/signature.
- Application submission endpoint validates incoming consent tokens against tenant JWKS and revocation table before accepting applications.
- Background job monitors rotation schedules, promoting `next` keys to `current`, generating replacements, and notifying tenants.

## Consequences
### Positive
- Boards and adapters can independently verify consent provenance, de-risking integrations.
- Enables downstream audit trail hashing (`jti`, `cid`) and revocation capabilities.
- Isolation per tenant reduces blast radius of key compromise.

### Risks / Mitigations
- **Key management complexity** — Mitigated with automation scripts, seed tooling, and documentation.
- **Migration downtime** — Provide dual validation (accept `ctok:` GUIDs for limited window) and progressive rollout.
- **Secret leakage risk in dev** — Store keys encrypted even in local environments; reuse existing `certs/` patterns.

## Implementation Plan
1. **Schema updates** — Add `tenant_keys` table, `consent_tokens` ledger (consentId, jti, kid, issuedAt, expiresAt).
2. **Key lifecycle tooling** — Extend `Gateway.CertAuthority` to mint and rotate tenant keys; seed demo tenants.
3. **Consent issuance update** — Replace GUID generator with JWT builder, include detached JWS envelope in responses.
4. **Validation path** — Update `/v1/applications` pipeline to parse JWT, validate signature via tenant JWKS, enforce revocation checks.
5. **Migration and compatibility** — Support both GUID and JWT tokens during rollout behind feature flag, retire GUID once partners migrated.
6. **Monitoring & alerting** — Emit metrics for signing failures, expiring keys, and revocation hits.

## Follow-up Actions
- Document operational runbook for key rotation and emergency key revocation.
- Update SDKs and samples to consume JWT consent tokens.
- Coordinate ADR 0003 (Audit trail persistence) to leverage new `jti` fields.

## References
- `docs/spec/consent-apply-v0.1.md`
- `docs/design/consent-ux-auth-foundation.md`
- NIST 800-57 key management guidelines
