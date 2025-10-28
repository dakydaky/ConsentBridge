# Swagger Demo Journeys (v0.1)

This note documents the current end-to-end flows we can showcase via Swagger (`http://localhost:8080/swagger`) using the demo scaffold. Update as new endpoints and auth pieces come online.

---

## Journey A – Agent applies with active consent (current happy path)

**Goal**: Show that the gateway can accept an application on behalf of a candidate and persist the outcome while forwarding to the MockBoard adapter.

1. **Create a consent request**
   - Endpoint: `POST /v1/consent-requests`
   - Headers: `Authorization: Bearer <token>` (obtain via Journey C)
   - Body example:
     ```json
     {
       "CandidateEmail": "alice@example.com",
       "AgentTenantId": "agent_acme",
       "BoardTenantId": "mockboard_eu",
       "Scopes": ["apply.submit"]
     }
     ```
   - Expected response: `202 Accepted` with `request_id` and a link to `/consent/{id}`.
   - Demo note: The OTP code is written to the API logs so you can complete the flow locally.

2. **Complete the web approval**
   - Visit `http://localhost:8080/consent/{request_id}`.
   - Enter the OTP, verify your email, and approve the consent. The success page displays the `consent_token`.

3. **Submit an application**
   - Endpoint: `POST /v1/applications`
   - Headers: `Authorization: Bearer <token>` and `X-JWS-Signature: demo.signature`
   - Body example (swap in real `ConsentToken`):
     ```json
     {
       "ConsentToken": "ctok:REPLACE",
       "Candidate": {
         "Id": "cand_123",
         "Contact": { "Email": "alice@example.com", "Phone": "+45 1234" },
         "Pii": { "FirstName": "Alice", "LastName": "Larsen" },
         "Cv": { "Url": "https://example/cv.pdf", "Sha256": "deadbeef" }
       },
       "Job": {
         "ExternalId": "mock:98765",
         "Title": "Backend Engineer",
         "Company": "ACME GmbH",
         "ApplyEndpoint": "quick-apply"
       },
       "Materials": {
         "CoverLetter": { "Text": "Hello MockBoard!" },
         "Answers": [{ "QuestionId": "q_legal_work", "AnswerText": "Yes" }]
       },
       "Meta": { "Locale": "de-DE", "UserAgent": "agent/0.1", "Ts": "2025-10-27T10:15:00Z" }
     }
     ```
   - Expected response: `202 Accepted` with application `id` and `status: Accepted`.
   - Behind the scenes: record is persisted with receipt payload from MockBoard; signature verification is currently stubbed (AcceptAllVerifier).

4. **Retrieve application status**
   - Endpoint: `GET /v1/applications/{id}`
   - Expected response: `200 OK` with receipt and audit fields showing the forwarded application.

---

## Journey B – Consent lifecycle & revocation (current behavior)

**Goal**: Demonstrate consent issuance, retrieval, and revocation via API, highlighting future auth hooks.

1. **Initiate consent** - same as step A1.
2. **Inspect consent** *(future)*:
   - Planned endpoint: `GET /v1/consents/{id}` (not yet implemented).
   - Will require tenant-auth (bearer token from `/oauth/token`).
3. **Revoke consent**
   - Endpoint: `POST /v1/consents/{id}/revoke`
   - Expected response: `204 No Content`.
   - After revocation, repeat Journey A step 2 with the same token → expect `403 Forbid`.

---

## Journey C - Tenant auth (`/oauth/token`)

- **Obtain access token**
  - Endpoint: `POST /oauth/token`
  - Content-Type: `application/x-www-form-urlencoded` (or `application/json` when calling from Swagger)
  - Body example (URL-encoded):
    ```
    grant_type=client_credentials&
    client_id=agent_acme_client&
    client_secret=agent-secret&
    scope=apply.submit
    ```
    
    ```
  - Expected response: `200 OK`
    ```json
    {
      "access_token": "<JWT>",
      "token_type": "Bearer",
      "expires_in": 1800,
      "scope": "apply.submit"
    }
    ```
  - Returned JWT includes claims for tenant slug (`sub`), tenant id, tenant type, client id, and scopes.
  - Enforcement on `/v1/applications` is still pending; note this to viewers.
  - After receiving the token, click Swagger’s **Authorize** button and paste `Bearer <token>` so subsequent calls include the header automatically.
  - **Swagger tip:** send the body as JSON if you cannot set the form URL-encoded content type:
    ```json
    {
      "grantType": "client_credentials",
      "clientId": "agent_acme_client",
      "clientSecret": "agent-secret",
      "scope": "apply.submit"
    }
    ```

- **Consent web flow**
  - Not yet exposed in Swagger.
  - Will replace direct `POST /v1/consents` with browser-based approval, then the API will issue the consent token through `IConsentTokenFactory`.
Track progress in:
- `docs/adr/0001-consent-ux-auth.md`
- `docs/design/consent-ux-auth-foundation.md`
- `TODO.md` (Consent UX & Auth section)

Update this file after each milestone to keep product demos and investor walkthroughs consistent.




