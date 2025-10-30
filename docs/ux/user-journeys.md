# User Journeys (Non‑Developer)

This document summarizes the end‑to‑end journeys from each non‑developer perspective. No Swagger, no CLI.

## Candidate (Alice)
- Receive a consent link by email and open it.
- Verify email ownership using a one‑time code.
- Review what the agent will do (scopes) and approve or deny.
- On approval, see a confirmation and (optionally) a receipt of the consent with expiry info.
- Later: receive a notification if consent is renewed or revoked.

## Agent (ACME)
- Start a consent request for a candidate and a specific board.
- Track pending requests and whether candidates approved or denied.
- Submit a job application using the valid consent token.
- View application status and the signed receipt from the board.
- Revoke consent at the candidate’s request or when work is complete.

## Board Operator (MockBoard)
- See a live feed of incoming applications.
- Open an application to view: candidate summary, job reference, and the agent’s signed payload.
- Verify the consent token’s validity window and tenant binding.
- Issue and store a signed receipt; make it visible to the agent.
- Search/filter past applications and receipts as needed.

## Compliance / Auditor
- Verify audit chain integrity for a date/tenant window.
- Inspect consent events (issued, renewed, revoked) and application events (submitted, accepted, failed).
- Export a candidate’s data in a packaged archive for DSR.
- Confirm key rotation events are captured and receipts remain verifiable over time.

