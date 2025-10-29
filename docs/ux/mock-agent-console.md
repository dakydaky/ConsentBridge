# Mock Agent Console (Design)

Purpose: Provide a simple, investor‑friendly UI to represent an agent’s experience without using Swagger or CLI.

## Core Screens
- Dashboard: list of candidates and consent status (Pending, Approved, Revoked, Expired)
- Request Consent: form to start a new consent request (candidate email, board, scopes)
- Submit Application: form to send an application using a valid consent
- Application Detail: status, signed receipt, and validation summary
- Consent Detail: token metadata (tenant, expiry), revoke/renew actions

## Wireframe (ASCII)

Dashboard
-------------------------------------------------
 Candidate           Consent         Last Action
 ------------------------------------------------
 Alice Larsen        Approved ✓      Submitted → Accepted (Receipt)
 Bob Jensen          Pending …       Awaiting Approval
 ------------------------------------------------
 [Request Consent] [Submit Application] [Refresh]

Request Consent
-------------------------------------------------
 Candidate Email: [ alice@example.com          ]
 Board:           [ MockBoard EU  v ]  Scopes: [apply.submit]
 [ Start Request ]   [ Cancel ]

Submit Application
-------------------------------------------------
 Candidate: [ Alice Larsen  v ]  Job Ref: [ mock:98765 ]
 Materials: [ Cover Letter | CV URL ]
 [ Submit ]   [ Cancel ]

Application Detail
-------------------------------------------------
 Status: Accepted
 Receipt: [ View JSON ] [ Verify Signature ✓ ]
 Consent: Token exp 2025‑12‑31  kid=acme‑k2

## Non‑Functional
- No persistent auth required for demo (tenant fixed to ACME)
- Safe dev‑only endpoints; no real emails sent
- Latency kept low; optimistic UI updates

## Technical Sketch (MVP)
- Project: `src/MockAgent.Console` (Razor Pages, minimal dependencies)
- Pages: `/` (Dashboard), `/consents/new`, `/applications/new`, `/applications/{id}`
- Integrations:
  - `POST /v1/consent-requests` (create)
  - `GET /v1/consents/{id}` (status)
  - `POST /v1/applications` (submit)
  - `GET /v1/applications/{id}` (status/receipt)
  - `POST /v1/consents/{id}/revoke` (revoke)

## Demo Notes
- Pair with the existing Consent Web flow for the candidate step
- Keep sample candidates/jobs pre‑seeded to eliminate typing
- Add a hidden "debug info" drawer to show token/headers if needed off‑screen

