# Investor Demo Runbook (ConsentBridge E2E)

Goal: Show end‑to‑end value through real screens, not Swagger. Cover the three audiences: Candidate, Agent, and Board Operator — plus a quick integrity/audit proof. No code, no curl on screen.

## Personas Covered
- Candidate: reviews scopes, verifies email, approves consent
- Agent: requests consent, submits an application, tracks status
- Board Operator: receives signed apply, verifies consent, issues receipt
- Compliance: verifies audit chain, runs a simple DSR export

## Demo Data
- Candidate: Alice Larsen (alice@example.com)
- Agent: ACME Agency (tenant `agent_acme`)
- Board: MockBoard EU (tenant `mockboard_eu`)
- Job: Backend Engineer (`mock:98765`)

## Scenes (8–12 minutes total)
1) Context (30s)
   - One slide: Why ConsentBridge (authorized, auditable, interoperable). Transition to live.

2) Candidate Consent (2 min)
   - Show: Consent web page `GET /consent/{requestId}` (Razor page in Gateway.Api).
   - Action: Enter OTP from logs (pre-copied to notes), review scopes, Approve.
   - Outcome: Success screen shows issued consent token (JWT) and expiry.

3) Agent Perspective (2–3 min)
   - Show: Mock Agent Console at http://localhost:8082 (see docker-compose). If not available, use static screens in docs/ux/mock-agent-console.md.
   - Action: "Request Consent" (pre-created), then "Submit Application" with candidate + job.
   - Outcome: Status changes to Submitted → Accepted (receipt available), consent ID/expiry visible.

4) Board Operator Perspective (2–3 min)
   - Show: MockBoard Adapter dashboard (`src/MockBoard.Adapter` → Pages/Index).
   - Action: Open the latest application; view parsed payload, consent token details, and the signed receipt.
   - Outcome: Demonstrate signature verified, consent valid window, and job reference mapping.

5) Audit & Integrity (1–2 min)
   - Show: Audit verification status page/endpoint (per ADR 0003). If UI not present, reference `docs/ops/audit-integrity-ops.md` and display a prepared screenshot of a successful verification run.
   - Outcome: Confirm hash chain intact and last anchor timestamp.

6) Lifecycle & DSR (1–2 min)
   - Revocation: Trigger a revocation from Agent Console; refresh board view to see future submissions denied.
   - DSR: Show that a candidate export (zip manifest) is available via operator flow documented in `docs/ops/consent-lifecycle-ops.md` and `docs/spec/whitepaper.md` sections.

## What We’re Proving
- Explicit user authorization and reversible consent
- Cryptographic trust between agent ↔ board (JWS/JWT, JWKS rotation)
- Immutable audit suitable for compliance sign‑off
- Practical operator UX for boards; low‑friction agent workflow

## Setup Notes (off‑screen)
- Use `demo.ps1` or compose to seed tenants and sample job references.
- Pre‑create a consent request so `/{requestId}` is ready in the browser.
- Keep a notes file with OTP and links to scenes to avoid typing.

## Fallback Plan
- If Agent Console is not implemented, narrate the agent flow with the static mock screens and move directly between Consent Web → MockBoard dashboard.
