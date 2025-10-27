# Consent‑First Apply Gateway – v0.1 Spec & C# Solution Skeleton

## 0) Overview

A B2B service that lets job boards/ATS accept **explicit, revocable candidate consent** for third‑party agents to apply on their behalf, with signed application payloads, audit logs, and DSR (data subject request) support.

---

## 1) Solution layout (C#/.NET 9)

```
consent-apply-gateway/
├─ src/
│  ├─ Gateway.Api/                 # ASP.NET Core Minimal APIs + OpenAPI
│  ├─ Gateway.Domain/             # Entities, Value Objects, Policies
│  ├─ Gateway.Application/        # Use-cases, Handlers, Services interfaces
│  ├─ Gateway.Infrastructure/     # EF Core, Postgres, Key Vault, Email
│  ├─ Gateway.CertAuthority/      # JWK/JWT issuing, key rotation jobs
│  ├─ Gateway.Sdk.DotNet/         # Partner SDK (client + models + signing)
│  └─ Gateway.Test/               # Unit/integration tests
├─ deploy/
│  ├─ docker/                      # Dockerfiles
│  ├─ helm/                        # Helm chart
│  └─ k8s/                         # K8s manifests (dev)
├─ docs/
│  ├─ api/openapi.yaml
│  └─ spec/consent-apply-v0.1.md
└─ tools/
   └─ scripts/
```

---

## 2) Data model (minimal)

Tables (Postgres):

* `tenants(id, name, type=board|ats|agent, jwks_uri, status)`
* `candidates(id, email_hash, pii_enc_blob, created_at)`
* `consents(id, candidate_id, agent_tenant_id, board_tenant_id, scopes[], status, issued_at, expires_at, revoked_at)`
* `keys(id, tenant_id, kid, alg, public_jwk, created_at, rotated_at)`
* `applications(id, consent_id, agent_tenant_id, board_tenant_id, status, submitted_at, receipt, payload_hash)`
* `audits(id, actor, action, tenant_id, subject_id, metadata_jsonb, at)`

---

## 3) Security model

* **Mutual auth**: platform issues **Client Credentials** (OAuth2) per tenant for API access.
* **Signing**: Agents sign **ApplyPayload** with their **JWS** (ES256/EdDSA). Boards/ATS verify against agent JWKs published via platform (or tenant‑hosted JWKS with allowlist).
* **Receipt**: Boards/ATS return a signed **ApplyReceipt** (JWS) so the platform can validate end‑to‑end provenance.
* **PII**: Sensitive fields optionally **envelope‑encrypted** with board’s RSA public key inside the payload.

---

## 4) Consent flow (v0.1)

1. **Create consent** (board/ATS redirects candidate to Gateway):

   * `GET /oauth/authorize?client_id={board}&redirect_uri=...&scope=apply:submit offline_access&agent_id=...`
   * Candidate authenticates and sees consent screen (scopes, validity, revocation info).
2. **Token issue**: Gateway issues **Consent Token (JWT)** to the agent (via code exchange), scoping which board(s) can receive applies.
3. **Revocation**: Candidate can revoke anytime → token invalidated.

Consent Token (JWT claims):

```json
{
  "sub": "cand_123",
  "aud": "agent_tenant_id",
  "iss": "https://gateway.eu",
  "scope": ["apply:submit"],
  "boards": ["board_stepstone"],
  "exp": 1735689600,
  "jti": "ctok_9J..."
}
```

---

## 5) Apply payload (v0.1) – canonical JSON

**Detached JWS** over the canonicalized JSON (JCS or JWS JSON Serialization). Recommended alg: **ES256** or **EdDSA**.

```json
{
  "spec": "consent-apply/v0.1",
  "consent_token": "<JWT>",
  "candidate": {
    "id": "cand_123",          
    "contact": {"email": "alice@example.com", "phone": "+45..."},
    "pii": {"first_name": "Alice", "last_name": "Larsen"},
    "cv": {"url": "https://files.gateway.eu/cv/cand_123_v5.pdf", "sha256": "..."}
  },
  "job": {
    "external_id": "stepstone:98765",
    "title": "Senior Backend Engineer",
    "company": "ACME GmbH",
    "apply_endpoint": "quick-apply"
  },
  "materials": {
    "cover_letter": {"text": "..."},
    "answers": [{"question_id": "q_legal_work", "answer": "Yes"}]
  },
  "meta": {"locale": "de-DE", "user_agent": "agent/1.2.3", "ts": "2025-10-27T10:15:00Z"}
}
```

Agent computes `b64(signature) = Sign(private_key, SHA256(canonical_json))` and sends:

```
POST /v1/applications
Headers:
  Authorization: Bearer <agent_api_token>
  X-JWS-Signature: eyJhbGciOiJFUzI1NiIsImtpZCI6Ik...  (Detached JWS)
Body: <payload above>
```

**Server verification**

* Validate `consent_token` (issuer, audience, scope, expiry, revocation, board allowlist).
* Verify JWS against agent’s JWK (by `kid`).
* (Optional) Decrypt envelope‑encrypted PII for the target board.
* Persist, forward to board/ATS adapter, await receipt.

---

## 6) Apply receipt (from Board/ATS)

Boards/ATS respond with a signed receipt (JWS) to prove acceptance.

```json
{
  "spec": "consent-apply/v0.1",
  "application_id": "app_7f9...",
  "board_id": "board_stepstone",
  "job_external_id": "stepstone:98765",
  "candidate_id": "cand_123",
  "status": "accepted",
  "received_at": "2025-10-27T10:15:02Z",
  "board_ref": "SS-ACK-223344"
}
```

Header: `X-JWS-Receipt: <JWS>` with board’s `kid`.

---

## 7) Minimal HTTP surface (Gateway.Api)

```csharp
app.MapPost("/v1/consents", CreateConsent);      // Admin/Board → provision consent intent
app.MapGet("/oauth/authorize", OAuthAuthorize);  // Candidate UI
app.MapPost("/oauth/token", OAuthToken);         // Code → Consent Token
app.MapPost("/v1/applications", SubmitApplication).RequireAuthorization("agent");
app.MapGet("/v1/applications/{id}", GetAppById).RequireAuthorization();
app.MapPost("/v1/consents/{id}/revoke", RevokeConsent).RequireAuthorization("candidate");
app.MapGet("/.well-known/jwks.json", GetPlatformJwks);
```

---

## 8) DTOs (Gateway.Domain)

```csharp
public record ConsentToken(string Jwt);

public record ApplyPayload(
    string Spec,
    string ConsentToken,
    Candidate Candidate,
    JobRef Job,
    Materials Materials,
    Meta Meta
);

public record Candidate(
    string Id,
    Contact Contact,
    Pii Pii,
    Document Cv
);

public record Contact(string Email, string? Phone);
public record Pii(string FirstName, string LastName);
public record Document(string Url, string Sha256);

public record JobRef(string ExternalId, string Title, string Company, string ApplyEndpoint);

public record Materials(CoverLetter CoverLetter, IReadOnlyList<Answer> Answers);
public record CoverLetter(string Text);
public record Answer(string QuestionId, string AnswerText);

public record Meta(string Locale, string UserAgent, DateTime Ts);

public record ApplyReceipt(
    string Spec,
    string ApplicationId,
    string BoardId,
    string JobExternalId,
    string CandidateId,
    string Status,
    DateTime ReceivedAt,
    string? BoardRef
);
```

---

## 9) Verification & signing helpers (Gateway.Application)

```csharp
public interface IJwsSigner
{
    string SignDetached(ReadOnlySpan<byte> canonicalJson, string kid);
}

public interface IJwsVerifier
{
    bool VerifyDetached(ReadOnlySpan<byte> canonicalJson, string jws, string expectedKid);
}

public interface IConsentService
{
    Task<ConsentValidation> ValidateAsync(string consentJwt, string boardId, CancellationToken ct);
}

public sealed record ConsentValidation(bool Valid, string CandidateId, DateTime ExpiresAt, string[] Scopes);
```

---

## 10) Minimal handler (Submit)

```csharp
app.MapPost("/v1/applications", async (
    HttpRequest req,
    [FromHeader(Name = "X-JWS-Signature")] string jws,
    [FromServices] IJwsVerifier verifier,
    [FromServices] IConsentService consents,
    [FromServices] ICanonicalizer canonicalizer,
    [FromServices] IApplicationService apps,
    CancellationToken ct) =>
{
    var payload = await req.ReadFromJsonAsync<ApplyPayload>(cancellationToken: ct);
    if (payload is null) return Results.BadRequest();

    var canonical = canonicalizer.Canonicalize(payload); // JCS
    var kid = JwsUtils.ExtractKid(jws);
    if (!verifier.VerifyDetached(canonical, jws, kid)) return Results.Unauthorized();

    var cv = await consents.ValidateAsync(payload.ConsentToken, payload.Job.ApplyEndpoint, ct);
    if (!cv.Valid) return Results.Forbid();

    var appId = await apps.ForwardAsync(payload, ct); // to board adapter; records audit
    return Results.Accepted($"/v1/applications/{appId}");
});
```

---

## 11) OpenAPI excerpt (docs/api/openapi.yaml)

```yaml
openapi: 3.0.3
info:
  title: Consent-Apply Gateway API
  version: v0.1
paths:
  /v1/applications:
    post:
      summary: Submit a signed application payload
      security: [{ bearerAuth: [] }]
      parameters:
        - in: header
          name: X-JWS-Signature
          required: true
          schema: { type: string }
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/ApplyPayload' }
      responses:
        '202': { description: Accepted }
components:
  securitySchemes:
    bearerAuth:
      type: http
      scheme: bearer
  schemas:
    ApplyPayload:
      type: object
      required: [spec, consent_token, candidate, job, materials, meta]
```

---

## 12) SDK sketch (Gateway.Sdk.DotNet)

```csharp
public sealed class ApplyClient
{
    private readonly HttpClient _http;
    private readonly IJwsSigner _signer;

    public ApplyClient(HttpClient http, IJwsSigner signer) { _http = http; _signer = signer; }

    public async Task<HttpResponseMessage> SubmitAsync(ApplyPayload payload, string kid, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions.Canonical);
        var jws = _signer.SignDetached(Encoding.UTF8.GetBytes(json), kid);
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/applications")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-JWS-Signature", jws);
        return await _http.SendAsync(req, ct);
    }
}
```

---

## 13) Revocation & DSR endpoints

* `GET /v1/me/consents` (candidate portal)
* `POST /v1/consents/{id}/revoke`
* `POST /v1/dsr/export`
* `POST /v1/dsr/delete`

---

## 14) Operational policies

* **Key rotation**: quarterly, overlapping validity; publish JWKS at `/.well-known/jwks.json` per tenant.
* **Rate limits**: default 10 req/s per tenant; burst 100.
* **Retention**: raw payloads 30 days; receipts + minimal metadata 12 months.

---

## 15) Acceptance criteria (MVP)

* End‑to‑end signed apply succeeds against a mock board adapter.
* Consent revocation invalidates new submissions immediately.
* Audit contains verifier outcome, consent jti, payload hash.
* DSR export produces all consents & applications for a candidate within SLA.
