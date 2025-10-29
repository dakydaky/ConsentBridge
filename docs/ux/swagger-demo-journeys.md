# 🌉 ConsentBridge Swagger Journeys (v0.2)

Welcome! These curated “choose‑your‑perspective” guides help you demo the platform directly from `http://localhost:8080/swagger`. Pick a persona, follow the steps, and point stakeholders at the parts they care about most.

> Need to show off debug tooling (e.g., on-demand key rotation)? See `swagger-demo-journeys-debug.md`.

---

## 🕵️ Agent Operator — “Can I submit safely?”

1. **🔐 Authenticate**
   - `POST /oauth/token`
   - Body (JSON):
     ```json
     {
       "grantType": "client_credentials",
       "clientId": "agent_acme_client",
       "clientSecret": "agent-secret",
       "scope": "apply.submit"
     }
     ```
   - Copy the `access_token` → Swagger “Authorize…”

2. **🆗 Trigger consent**
   - `POST /v1/consent-requests`
   - Body: `{"CandidateEmail":"alice@example.com","AgentTenantId":"agent_acme","BoardTenantId":"mockboard_eu","Scopes":["apply.submit"]}`
   - Grab the `request_id` from the response.

3. **📬 Approve the OTP flow (browser)**
   - Visit `http://localhost:8080/consent/{request_id}`
   - OTP is logged to the gateway console; approve to reveal the `consentToken`.

4. **🖋️ Sign the application payload**
   - Update `docs/ux/demo-application-payload.json` with the real `consentToken`.
   - Run `pwsh ./demo.ps1 -PayloadPath ./docs/ux/demo-application-payload.json`
   - Copy:
     - Canonical JSON → request body.
     - Printed value → `X-JWS-Signature`.

5. **📤 Submit the application**
   - `POST /v1/applications`
   - Headers: `Authorization: Bearer <token>`, `X-JWS-Signature: <value>`
   - Body: canonical JSON from the script.
   - Expect `202 Accepted` with `{id, status: Accepted}`.

6. **🧾 Review provenance**
   - `GET /v1/applications/{id}` → verify `submissionSignature`, `submissionKeyId`, `receiptSignature`, `receiptHash`.
   - Bonus: open `http://localhost:8080/applications/{id}` for a gallery-style view.

7. **🔄 Renew before expiry (new)**
   - If your consent token is nearing expiry, call `POST /v1/consents/{id}/renew` with the consent ID.
   - Paste the returned `token` into your application payload and re-run the submission.

8. **⏲️ Token grace (new)**
   - Submissions that arrive just after token expiry are accepted within the configured grace window.
   - Look for an `AUDIT application token_grace_accept` log entry in the gateway output.

---

## 🧑‍💼 Hiring Board — “Show me my receipt proof”

1. **🔐 Authenticate as board**
   - `POST /oauth/token`
   - Body (JSON):
     ```json
     {
       "grantType": "client_credentials",
       "clientId": "mockboard_client",
       "clientSecret": "board-secret",
       "scope": "apply.submit"
     }
     ```
   - Authorize Swagger with the returned token.

2. **🗃️ List applications destined for you**
   - `GET /v1/applications/{id}` (use the ID from the agent flow).
   - Check:
     - `boardTenantId == "mockboard_eu"`
     - `status == Accepted`
     - `receiptSignature` and `receiptHash` present.

3. **📄 Inspect the receipt payload**
   - Use the portal page `http://localhost:8080/applications/{id}` to show the signed receipt JSON and matching signature.

4. **🔑 Confirm key provenance**
   - `GET /.well-known/jwks.json` (all tenants)
   - `GET /tenants/mockboard_eu/jwks.json` (board-specific)
   - Demonstrate that MockBoard’s public key (`kid: mockboard-key`) is published alongside the agent key.

5. **🧭 Rely on provenance**
   - Show how `receiptHash` ties the stored payload to the signed response (SHA‑256).
   - Mention the roadmap item for surfacing this in recruiter UX dashboards.

---

## 👩‍⚖️ Privacy Lead — “Does this respect GDPR?”

Use Swagger to back up compliance talking points:

1. **🔐 Lawful basis (consent)**
   - Demonstrate the consent request + OTP approval (steps in Agent journey).
   - Emphasize retention policy references in `docs/security/cryptography-and-compliance.md`.

2. **🔒 Integrity & audit**
   - `POST /v1/applications`: highlight enforcement of `X-JWS-Signature`.
   - `GET /v1/applications/{id}`: show `submissionSignature`, `submissionKeyId`, `submissionAlgorithm`, `receiptSignature`, `receiptHash`.

3. **🔑 Public key transparency**
   - `GET /.well-known/jwks.json`: explain how tenants can verify keys before trusting receipts.

4. **🗑️ Data subject rights**
   - `POST /v1/dsr/export` – walk through the JSON package returned for consents, applications, and consent requests.
   - `POST /v1/dsr/delete` – show the `confirm` flag, deletion counters, and the tenant scoping rules.
   - Automated retention: receipts older than 12 months return with `receipt` stripped; consent requests older than 90 days are removed automatically.
5. **🛡️ Next steps**
   - Mention planned key rotation tooling and JWKS hosting in production to bolster compliance posture.

---

## 🧪 Engineer — “Breaking tests & failure drills”

1. **❌ Signature failure drill**
   - Change the JWS header `kid` before posting to `/v1/applications` → expect `400 invalid_signature`.

2. **🔁 Replay prevention (demo)**
   - Re-submit the same application ID; we store idempotent application records, but real duplicate protection is a roadmap item.

3. **⚠️ Receipt tampering**
   - Manually edit the receipt JSON from MockBoard and post it back → observe `502` as verification fails.

4. **🔄 JWKS rotation rehearsal**
   - Swap the JWKS file for MockBoard, restart gateway, re-run the submission to confirm verification switches to the new key.

---

## ✅ Quick Reference

| Persona | Key endpoints | Stories to tell |
| ------- | ------------- | --------------- |
| 🕵️ Agent | `/oauth/token`, `/v1/consent-requests`, `/v1/applications`, `/v1/applications/{id}` | End-to-end submission with ES256 signature |
| 🧑‍💼 Board | `/v1/applications/{id}`, `/.well-known/jwks.json` | Receipts & provenance proof |
| 👩‍⚖️ Privacy | `/v1/consent-requests`, `/v1/applications`, `/.well-known/jwks.json` | GDPR-aligned controls & roadmap gaps |
| 🧪 Engineer | `/v1/applications`, `/.well-known/jwks.json` | Failure drills & key rotation rehearsal |

Happy demoing! 🎉

