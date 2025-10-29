# 🔧 Swagger Debug Journeys – Key Rotation & JWKS Demos

This companion guide keeps everything inside Swagger/OTP flow while showcasing the new debug endpoint for forcing tenant key rotation. Perfect for “show-me-now” security demos.

---

## 🎯 Goal
Demonstrate how consent JWTs are signed, exposed via JWKS, and rotated on demand—without editing config files or bouncing Docker containers.

## 🛠 Prereqs
- Gateway running (`dotnet run --project src/Gateway.Api` or the docker compose stack)
- Swagger UI at `http://localhost:8080/swagger`
- Environment set to **Development** (the debug endpoint only lights up there)

---

## 🌀 Walkthrough — “Rotate keys live”

1. **Authenticate the agent**
   - `POST /oauth/token`
   - Body:
     ```json
     {
       "grantType": "client_credentials",
       "clientId": "agent_acme_client",
       "clientSecret": "agent-secret",
       "scope": "apply.submit"
     }
     ```
   - Copy the `access_token`, click “Authorize…”, and paste as `Bearer <token>`.

2. **Create a consent request**
   - `POST /v1/consent-requests`
   - Body:
     ```json
     {
       "CandidateEmail": "alice@example.com",
       "AgentTenantId": "agent_acme",
       "BoardTenantId": "mockboard_eu",
       "Scopes": ["apply.submit"]
     }
     ```
   - Save the returned `request_id`.

3. **Approve via the OTP flow**
   - Open `http://localhost:8080/consent/{request_id}` in a browser.
   - Use the OTP logged by the gateway; approve to reveal the consent JWT (`consentToken`). Copy the token string.

4. **Inspect the current JWKS**
   - Back in Swagger, call `GET /.well-known/jwks.json`.
   - Note the `kid` values—these are the active consent-signing keys. (You can hit `GET /tenants/agent_acme/jwks.json` for a filtered view.)

5. **Force a rotation**
   - Still in Swagger, open the **Debug** section (only visible in Development).
   - `POST /debug/tenants/{slug}/rotate-consent-key` with `slug = agent_acme`.
   - Response returns the newly minted key (`keyId`, `expiresAt`).

6. **Issue another consent**
   - Repeat steps 1‑3 (new request → approve). The success page shows a fresh consent JWT.
   - Decode the header (jwt.io or your favorite decoder); the `kid` now matches the new key from step 5.

7. **Verify JWKS & application submission**
   - Re-run `GET /.well-known/jwks.json`—you’ll see both keys (retired + active) until the old one ages out.
   - Optional: `POST /v1/applications` with the new JWT to prove the backend accepts the rotated key.

8. **(Lifecycle) Renewal & grace**
   - Issue a consent then call `POST /v1/consents/{id}/renew` to show renewal without OTP.
   - For grace acceptance, reduce `ConsentLifecycle:ExpiryGraceDays` and submit just after token expiry; observe audit `application token_grace_accept` and persisted `AuditEvents` rows.

---

## 🧭 Summary Map

| Step | Endpoint | Why it matters |
|------|----------|----------------|
| 1 | `POST /oauth/token` | Authenticates Swagger to act as the agent |
| 2 | `POST /v1/consent-requests` | Spins up a consent flow tied to the agent |
| 3 | Browser `/consent/{id}` | Issues the actual consent JWT |
| 4 | `GET /.well-known/jwks.json` | Exposes current signing keys (`kid`) |
| 5 | `POST /debug/tenants/{slug}/rotate-consent-key` | Forces new tenant key generation (dev only) |
| 6 | Browser `/consent/{id}` (again) | Shows a JWT signed by the rotated key |
| 7 | `GET /.well-known/jwks.json`, `POST /v1/applications` | Confirms JWKS + submission validation post-rotation |

---

## 💡 Tips
- Want to demonstrate headless consent issuance? Use `dotnet user-jwts decode` instead of jwt.io.
- If you need fresh demo data, `dotnet ef database update` seeds the default tenants; `POST /debug/tenants/{slug}/rotate-consent-key` can be called repeatedly.
- Rotate other tenants (e.g., `mockboard_eu`) the same way to show multi-tenant isolation.

Happy debugging! 🎉
