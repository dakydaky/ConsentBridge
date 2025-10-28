# ADR 0003 – Immutable Audit Trail Persistence

| Field   | Value |
|---------|-------|
| Status  | Proposed |
| Date    | 2025-10-28 |
| Owners  | Platform Team |
| Tags    | compliance, persistence, auditing |

## Context
ConsentBridge handles consent issuance, application submissions, and receipt provenance across multiple tenants. Regulatory frameworks (GDPR, eIDAS) and our own security posture require immutable audit evidence demonstrating:
- When a candidate granted or revoked consent.
- Which tenant submitted an application, under which consent token, and with which signature.
- The lifecycle of key material used to sign or validate submissions.

Current persistence stores operational data (consents, applications) but lacks append-only audit trails with tamper-evident hashing. Ad-hoc log aggregation is insufficient for compliance evidence or forensic analysis.

## Problem Statement
We need an audit data model and storage strategy that:
- Records critical lifecycle events as append-only entries.
- Guarantees tamper evidence via hashing/digest chains.
- Supports efficient retrieval for subject access requests, regulator inquiries, and incident response.
- Scales with multi-tenant workload without degrading transactional throughput.

## Decision Drivers
- **Regulatory compliance** — Demonstrate unaltered event history with strong integrity guarantees.
- **Operational forensics** — Enable quick reconstruction of consent/application timelines.
- **Performance & cost** — Avoid overloading the transactional database while keeping retrieval practical.
- **Simplicity & maintainability** — Prefer relational patterns that align with existing EF stack unless compelling reason to adopt specialised storage.

## Options Considered
1. **Extend existing tables with audit columns** (soft deletes, timestamps). Minimal change but no tamper evidence; easy to overwrite history (Rejected).
2. **Event sourcing store in NoSQL/queue** (e.g., Kafka streams + S3). Strong audit properties but introduces significant operational burden and eventual-consistency complexities for MVP (Deferred).
3. **Dedicated immutable audit tables with hashing, stored in PostgreSQL alongside main schema**. Leverages familiar tooling while ensuring append-only behaviour (Chosen).

## Decision
Implement dedicated audit tables (`audit_events`, `audit_event_hashes`) within the primary database, enforced via DB constraints and application logic to remain append-only. Each event captures metadata, payload snapshot hash, and links into a per-tenant hash chain.

### Architecture Overview
- **Tables**
  - `audit_events`: Columns `id`, `tenant_id`, `category`, `action`, `entity_type`, `entity_id`, `payload_hash`, `created_at`, `actor_type`, `actor_id`, `jti`, `metadata`.
  - `audit_event_hashes`: Columns `event_id`, `previous_hash`, `current_hash`, `tenant_chain_id`, `created_at`.
- **Hashing strategy** — Each event’s `current_hash` = SHA-256(`previous_hash` + canonical event representation). `previous_hash` references the prior event hash in the tenant chain; the first event uses a per-chain genesis hash.
- **Append-only enforcement** — Application layer prohibits updates/deletes; database role for services lacks UPDATE/DELETE privileges on audit tables. Partitioning by month/tenant to maintain manageable table sizes.
- **Event producers** — Consent issuance/revocation, token issuance (ADR 0002), application submission, receipt verification, key rotations.
- **Retrieval** — Query views summarise chain integrity; DSR exports include relevant audit events ordered by `created_at`.

## Consequences
### Positive
- Provides tamper-evident audit trails across critical workflows.
- Aligns with compliance expectations without introducing new datastores.
- Enables correlation using shared identifiers (`jti`, `cid`, `applicationId`).

### Risks / Mitigations
- **Table growth** — Mitigated via partitioning and retention policies (archival to cold storage after SLA).
- **Integrity compromise via privileged account** — Restrict DB roles, add periodic offline hash verification jobs exporting digests to WORM storage.
- **Implementation complexity** — Provide canonical serialization helper to avoid hash drift and unit-test chain generation.

## Implementation Plan
1. **Schema migration** — Create audit tables and supporting indexes (tenant, category, created_at).
2. **Canonicalization utilities** — Build helper for canonical JSON encoding used for hashes.
3. **Emit audit events** — Hook into consent issuance/revocation, token issuance, application submission, receipt verification, key rotation.
4. **Integrity verification job** — Background job recalculates hashes, ensures no gaps, and exports daily digests.
5. **DSR integration** — Include relevant audit entries in DSR export pipeline.

## Follow-up Actions
- Integrate with security monitoring to alert on hash chain breaks.
- Evaluate long-term archival to object storage for events beyond retention window.
- Update documentation/runbooks describing audit event categories and access patterns.

## References
- `docs/spec/whitepaper.md`
- `docs/spec/consent-apply-v0.1.md`
- ADR 0002 – Signed Consent Tokens & Key Rotation
