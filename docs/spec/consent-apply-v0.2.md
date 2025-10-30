# ConsentBridge

**consent-apply-v0.1** (HR Open/JDX mapped) + **Partner One‑Pager**

> Status: Draft for review · Audience: Product, Legal/Privacy, ATS & Job Board Integrators

---

## 1) Specification: consent-apply-v0.1

### 1.1 Purpose & Scope

A neutral, consent-first gateway for third-party **agents** to apply on behalf of **candidates** to **boards/ATS** via signed, auditable payloads. This spec defines:

* Consent issuance & revocation (OAuth-style).
* The **ApplyPayload** format, signing (detached JWS), and verification.
* The **ApplyReceipt** format (JWS) returned by boards/ATS.
* JWKS discovery, key rotation, and audit primitives.
* Minimal webhooks and error taxonomy.

### 1.2 Key Terms

* **Candidate** – data subject; holder of consent and personal data.
* **Agent** – third-party acting on behalf of Candidate under explicit consent.
* **Board/ATS (Relying Party)** – accepts ApplyPayload, returns signed ApplyReceipt.
* **Consent Token (ctok)** – JWT issued to Agent after Candidate approval; scoped & expiring.
* **Detached JWS** – JWS where payload is not embedded; signature is sent via header.

### 1.3 Transport & Security

* **Transport:** HTTPS; JSON over HTTP.
* **Auth (platform):** Client Credentials for tenant→gateway; Bearer access tokens.
* **Delegation (candidate→agent):** Consent flow issues **ctok** (JWT) with scopes.
* **Signing:** ES256 (P‑256) or EdDSA (ed25519). JWS Compact Serialization (detached).
* **Keys:** Per‑tenant key pairs; JWKS at `/.well-known/jwks.json` and `/tenants/{slug}/jwks.json`.

### 1.4 HR Open/JDX Mapping (selected)

* **Candidate** → HR‑JSON `Candidate` / JDX `Person` (name, contact, identifiers).
* **Job** → HR‑JSON `Requisition`/`JobPosting` reference; JDX `JobPosting` identifiers.
* **Materials** → HR‑JSON `Attachment`, `Answer` arrays; JDX `Application` supplemental docs.
* **Consent** → Represented as OAuth2-style grant + UMA-like resource owner delegation.

> Appendix A includes a detailed field‑by‑field crosswalk.

### 1.5 Consent Token (ctok) – JWT

**Header**

```json
{
  "alg": "ES256",
  "kid": "agent_acme_kid_2025_10",
  "typ": "JWT"
}
```

**Claims (example)**

```json
{
  "iss": "https://consentbridge.example/",
  "sub": "agent:acme",                
  "aud": ["apply:mockboard_eu"],      
  "scope": "apply.submit apply.status",
  "cid": "cand_123",                  
  "email": "alice@example.com",       
  "consent_id": "cns_9f3...",
  "iat": 1761810000,
  "exp": 1761817200,
  "jti": "ctok_2c1..."
}
```

### 1.6 ApplyPayload (candidate→board via gateway)

**Content-type:** `application/json` (canonicalized for signing)

```json
{
  "ConsentToken": "<JWT ctok>",
  "Candidate": {
    "Id": "cand_123",
    "Contact": {"Email": "alice@example.com", "Phone": "+45 1234"},
    "Pii": {"FirstName": "Alice", "LastName": "Larsen"},
    "Cv": {"Url": "https://example/cv.pdf", "Sha256": "deadbeef"}
  },
  "Job": {
    "ExternalId": "mock:98765",
    "Title": "Backend Engineer",
    "Company": "ACME GmbH",
    "ApplyEndpoint": "quick-apply"
  },
  "Materials": {
    "CoverLetter": {"Text": "Hello MockBoard!"},
    "Answers": [{"QuestionId": "q_legal_work", "AnswerText": "Yes"}]
  },
  "Meta": {"Locale": "de-DE", "UserAgent": "agent/0.1", "Ts": "2025-10-27T10:15:00Z"}
}
```

**Signing (Detached JWS)**

* Canonicalize JSON (RFC 8785) → `payload.c14n` bytes.
* Compute JWS with header `{ alg, kid, typ:"JOSE" }`, base64url signature over protected header + `payload.c14n`.
* Send signature in header: `X-JWS-Signature: <protected>.. <signature>` (note double dot for detached payload).
* Gateway/Board fetch verifier keys from Agent’s tenant JWKS (via ConsentBridge directory).

### 1.7 ApplyReceipt (board/ATS → agent)

**Purpose:** Immutable evidence of submission.

**JWS Payload (example)**

```json
{
  "iss": "mockboard_eu",
  "aud": "agent:acme",
  "rid": "rcpt_01HZN...",              
  "app_id": "app_01HZM...",            
  "job_ref": "mock:98765",
  "received_at": "2025-10-27T10:15:02Z",
  "payload_hash": {
    "alg": "sha256",
    "value": "A8tV...=="
  },
  "consent_id": "cns_9f3...",
  "verifier": "https://mockboard.example/receipts/rcpt_01HZN..."
}
```

**Return header:** `Content-Type: application/jose; profile=receipt.v1`

**Verification:**

* Validate JWS using Board/ATS JWKS.
* Ensure `payload_hash` matches canonicalized ApplyPayload.
* Check `aud` matches submitting Agent tenant.

### 1.8 Endpoints (minimal)

* `POST /v1/consent-requests` → start consent (OTP + web approval)
* `POST /v1/applications` → submit ApplyPayload (requires `X-JWS-Signature`)
* `GET /v1/consents/{id}` → consent details
* `GET /v1/applications/{id}` → status + latest receipt
* `POST /v1/consents/{id}/revoke` → immediate revocation
* `GET /.well-known/jwks.json` and `/tenants/{slug}/jwks.json`

### 1.9 Webhooks (recommended)

* `consent.revoked` – payload: `{ consent_id, agent_tenant, candidate_email, revoked_at }`
* `application.receipt.created` – payload: `{ app_id, receipt_id, verifier_url }`
* `dsr.export.ready` – payload: `{ request_id, download_url, expires_at }`

### 1.10 Error Taxonomy (excerpt)

| Code                 | HTTP | Meaning                          | Client Action             |
| -------------------- | ---: | -------------------------------- | ------------------------- |
| `consent_expired`    |  401 | ctok exp or `consent_id` revoked | Re‑initiate consent       |
| `signature_invalid`  |  400 | JWS invalid/kid unknown          | Rotate keys / fix signing |
| `scope_insufficient` |  403 | ctok lacks `apply.submit`        | Request broader scope     |
| `job_unavailable`    |  404 | Posting closed/not found         | Stop retries              |
| `rate_limited`       |  429 | Throttled                        | Backoff + jitter          |

### 1.11 Privacy & Retention

* **Minimization:** send only material required by posting.
* **Retention (default):** raw receipts 12m; consent requests 90d; hashed audit forever.
* **DSR:** export/delete endpoints scoped by tenant; revocation takes effect for new applies immediately.

### 1.12 Security Addenda

* **Agent KYC:** onboarding checklist (legal entity, DPIA, abuse process).
* **Replay protection:** include `Meta.Ts`; reject if skew > 10m; idempotency key optional.
* **Audience binding:** `aud` ties ctok to board/ATS tenant.

---

## Appendix A – HR Open / JDX Crosswalk (excerpt)

| ConsentBridge Field              | HR Open / JDX                      | Notes                            |
| -------------------------------- | ---------------------------------- | -------------------------------- |
| Candidate.Pii.FirstName/LastName | `PersonName`                       | Map to `GivenName`/`FamilyName`  |
| Candidate.Contact.Email          | `ContactInfo.Email`                | Primary contact                  |
| Candidate.Cv                     | `Attachment`                       | Include `ContentHash`            |
| Materials.Answers[]              | `Assessment/QuestionnaireResponse` | `QuestionId`→`Identifier`        |
| Job.ExternalId                   | `JobPosting.Identifier`            | Preserve source system namespace |
| Job.Title                        | `JobPosting.Title`                 |                                  |
| Job.Company                      | `HiringOrganization.Name`          |                                  |
| ApplyReceipt.payload_hash        | `Provenance/Hash`                  | New field in spec extension      |

---

## 2) Partner One‑Pager (ATS / Job Board)

### What you get

* **GDPR‑grade delegated consent** for third‑party submissions.
* **Signed applies in → signed receipts out** (tamper‑evident evidencing).
* **Drop‑in adapter**: verify Agent signatures, emit receipts, expose a verification URL.
* **DSR tooling**: export/delete for agent‑scoped candidate data.

### Why it helps

* Cut **liability**: explicit consent trail, instant revocation, auditable provenance.
* Reduce **spam/abuse**: KYC’d agents, cryptographic signatures, rate limits.
* Improve **conversion**: candidates choose trusted agents; fewer duplicate/illicit submissions.

### How it works (3 steps)

1. **Verify applies:** Fetch Agent JWKS; verify detached‑JWS over canonicalized payload.
2. **Process application:** Create/attach to your internal candidate & job records.
3. **Return evidence:** Sign and return an **ApplyReceipt** JWS (+ expose `verifier_url`).

### Minimal integration (2–4 days typical)

* **Inbound:** Add a controller to consume `POST /v1/applications` via ConsentBridge adapter; verify JWS; map fields to your application model.
* **Outbound:** Emit `ApplyReceipt` JWS and host a simple `/receipts/{id}` verify endpoint.
* **Keys:** Publish your board/ATS JWKS; rotate keys on your regular schedule.

### SLA & SRE notes

* Idempotency supported (optional key).
* Retries: exponential backoff; signatures valid for ±10 min clock skew.
* Observability: request ID correlation; structured logs incl. `consent_id`, `app_id`.

### Legal & Privacy

* You remain the processor/controller per your normal role.
* ConsentBridge provides evidence of **explicit consent** and a revocation channel.
* DSR requests flow through your established export/delete mechanisms.

### Contact

* Tech: `tech@consentbridge.dev`
* Privacy: `privacy@consentbridge.dev`

---

*End of draft.*
