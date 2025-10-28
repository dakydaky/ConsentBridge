# Consent Web Flow – Outline

Goal: Replace the direct `POST /v1/consents` call with a minimal browser experience that lets candidates review scopes, verify their email, and issue a consent token. This aligns with ADR 0001 and our roadmap.

## High-level Flow
1. **Agent initiates consent** (future API call) providing candidate email + board/agent IDs.
2. **Candidate receives link** (`/consent/{consentRequestId}`) and lands on the consent app.
3. **Email verification** (magic link or OTP) confirms control of the email address the agent provided.
4. **Scope review screen** shows agent branding, requested scopes, retention blurb.
5. **Approve / deny** sends result back to Gateway API:
   - Approve → create `Consent`, issue signed consent token via `IConsentTokenFactory`, display success screen with optional download.
   - Deny → log refusal and show informative message to candidate.

## Proposed Implementation (MVP)
### Tech Stack
- Razor Pages (lightweight, server-rendered; no SPA build step).
- Minimal Tailwind/Vanilla CSS for quick styling.
- Re-use existing ASP.NET host (`Gateway.Api`) by adding a `/Consent` area.

### API Changes
1. **Consent Requests Table**
   - Columns: `Id`, `AgentTenantId`, `BoardTenantId`, `CandidateEmail`, `Scopes`, `Status`, `CreatedAt`, `ExpiresAt`, `VerificationCode`, `VerifiedAt`.
2. **Endpoints**
   - `POST /v1/consent-requests` (tenant-auth protected) → create pending request and email link (initially log to console).
   - `GET /consent/{id}` → Razor page entry point.
   - `POST /consent/{id}/verify-email` → input OTP; mark `VerifiedAt`.
   - `POST /consent/{id}/decision` → approve or deny; create `Consent` + token if approved.
3. **Token Delivery**
   - On approval, call `IConsentTokenFactory` to create token, store `ConsentToken`, show on success page, and emit webhook/log for agent (future).

### UX Pages
1. Landing (`/consent/{id}`):
   - Show agent/board info, consent scopes, prompt for verification code.
2. Verification:
   - Input code; if correct, proceed to approval.
3. Approval:
   - Summary of scopes + terms; Approve / Deny buttons.
4. Success/Denied:
   - Display token (copy/share) or denial message.

### Email/OTP Generation
- MVP: log verification code to console/server logs.
- Future: integrate email service (SendGrid/Postmark).
- Store hashed codes with short TTL.

## Considerations
- Maintain audit trail: every decision stored via `audit_events` with hash chaining per ADR 0003.
- Prevent reuse: once decision recorded, disable link.
- Security: signed consent token should embed `TokenId`, `tenant info`, `expires`.
- Internationalization: keep strings centralized for later localization.

## Next Steps
1. Extend infrastructure with `ConsentRequest` entity + migration.
2. Implement tenant-protected `POST /v1/consent-requests` endpoint (API only for now).
3. Scaffold Razor Pages + verification workflow (with console-based OTP).
4. Update Swagger docs and README to reflect new flow.
5. Remove direct `POST /v1/consents` from demo once web flow is stable (or retain for test CLI use with warning).

## Status
- [x] Consent requests + OTP flow wired into Razor Pages
- [ ] Email delivery (currently logs OTP)
- [ ] Consent dashboard / retrieval endpoints

