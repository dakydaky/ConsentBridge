# ADR 0007 – Consent Lifecycle Policy (TTL, Renewal, Grace)

| Field  | Value |
|--------|-------|
| Status | Accepted |
| Date   | 2025-10-29 |
| Owners | Platform Team |
| Tags   | consent, tokens, lifecycle, compliance |

## Context
We introduced per-tenant signed consent tokens (ADR 0002) and a renewal path. The product now needs a clear policy for token TTL, renewal windows, and behaviour around expiry to balance reliability and compliance.

## Decision Drivers
- Regulatory clarity: a consent’s contractual validity (the consent record) must not be implicitly extended without explicit user action.
- Operational resilience: allow short tolerance for token expiry drift (network delays, client clock skew) without reopening the consent agreement.
- Simplicity: avoid ambiguous states between consent and token validity.

## Decision
- Consent record expiry (`Consent.ExpiresAt`) remains a hard boundary with no grace. When a consent expires, applications are rejected and token renewal is not permitted. A new consent (explicit approval) is required.
- Token expiry has a short configurable grace window to smooth client/network delays:
  - `ConsentLifecycleOptions.ExpiryGraceDays` allows accepting a just‑expired token if still within grace.
  - Renewal is allowed when within `RenewalLeadDays` before token expiry and within `ExpiryGraceDays` after token expiry.
- Applications must pass both: consent is Active and unexpired, and token is valid (including grace).

## Consequences
### Positive
- Aligns consent validity with explicit user agreement; no silent extension of consent.
- Reduces integration friction from minor token expiry drift.
- Keeps policy simple and auditable.

### Trade‑offs
- Some late submissions with expired consent will require renewal flow, increasing friction but maintaining compliance posture.

## Implementation Summary
- `ConsentLifecycleService` issues renewals within lead/grace windows only while the consent itself is unexpired.
- `/v1/applications` enforces consent expiry strictly; token expiry allows grace per options, with logging when grace is used.
- Options are configured via `ConsentLifecycle` section.

### Validation details
- Token signature, issuer, and audience are validated; lifetime is enforced at the application layer to support grace window semantics.

## Follow‑up Actions
- Emit audit events for renewals and grace‑path acceptances (see ADR 0003 proposal).
- Document operator guidance for lifecycle settings and renewal flows.
- Add metrics for grace usage, renewals, and expired-consent rejections.

## References
- ADR 0001 – Consent UX & Tenant Authentication
- ADR 0002 – Signed Consent Tokens & Key Rotation
- ADR 0003 – Immutable Audit Trail Persistence (Proposed)
