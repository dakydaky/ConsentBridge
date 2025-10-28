# Consent‑Apply Protocol v0.1

**Status:** Draft • **Audience:** Job Boards, ATS vendors, Trusted Agents • **Scope:** EU consented quick-apply

## 1. Goals
- Allow candidates to **explicitly authorize** trusted agents to submit job applications on their behalf.
- Provide **cryptographic provenance** of both submission and receipt.
- Ensure **GDPR compliance**: consent, revocation, DSR, and transparency.

## 2. Roles
- **Candidate** — the data subject granting consent.
- **Agent** — third-party acting on behalf of the candidate.
- **Gateway (ConsentBridge)** — trust layer issuing/validating consent tokens, brokering requests.
- **Board/ATS** — destination accepting applications and issuing signed receipts.

## 3. Consent
- OAuth-like flow issues a **Consent Token (JWT)** to the Agent, scoped to boards and actions.
- Token includes `sub` (candidate id), `iss` (gateway), `aud` (agent tenant id), `boards` allowlist, `scope` (e.g., `apply:submit`), `exp`, `jti`.
- **Revocation** invalidates future submissions immediately.

### 3.1 Consent Token (example)
```json
{
  "sub": "cand_123",
  "aud": "agent_acme",
  "iss": "https://gateway.example.eu",
  "scope": ["apply:submit"],
  "boards": ["board_stepstone"],
  "exp": 1735689600,
  "jti": "consent_jti_9Jq..."
}
````

## 4. ApplyPayload

Canonical JSON document containing candidate info, job reference, materials, and metadata.

### 4.1 Canonicalization

* Use **JCS** (JSON Canonicalization Scheme) or RFC 8785 to produce a byte sequence for signing.

### 4.2 Signing

* Agents MUST attach a **detached JWS** over the canonical JSON using **ES256** or **EdDSA**.
* Header contains `alg`, `kid`; signature travels in `X-JWS-Signature` header.

### 4.3 Envelope encryption (optional)

* Sensitive PII may be encrypted with the Board/ATS public key and placed under `candidate.pii_enc` alongside a `pii_enc_alg`.

### 4.4 Example ApplyPayload

```json
{
  "spec": "consent-apply/v0.1",
  "consent_token": "<Consent JWT (demo mode also accepts ctok:UUID during transition)>",
  "candidate": {
    "id": "cand_123",
    "contact": {"email": "alice@example.com", "phone": "+45 1234"},
    "pii": {"first_name": "Alice", "last_name": "Larsen"},
    "cv": {"url": "https://files/cv.pdf", "sha256": "deadbeef"}
  },
  "job": {"external_id": "mock:98765", "title": "Backend Engineer", "company": "ACME GmbH", "apply_endpoint": "quick-apply"},
  "materials": {"cover_letter": {"text": "Hello!"}, "answers": [{"question_id": "q_legal_work", "answer": "Yes"}]},
  "meta": {"locale": "de-DE", "user_agent": "agent/0.1", "ts": "2025-10-27T10:15:00Z"}
}
```

## 5. Receipts

* Board/ATS responds with a **signed receipt** (JWS) including: `spec`, `application_id`, `board_id`, `job_external_id`, `candidate_id`, `status`, `received_at`, `board_ref`.
* Gateway verifies signature against the Board/ATS JWKS, stores receipt, and exposes status.

### 5.1 Receipt example

```json
{
  "spec": "consent-apply/v0.1",
  "application_id": "app_7f9...",
  "board_id": "mockboard_eu",
  "job_external_id": "mock:98765",
  "candidate_id": "cand_123",
  "status": "accepted",
  "received_at": "2025-10-27T10:15:02Z",
  "board_ref": "MB-223344"
}
```

## 6. Transport

* HTTP/1.1 or HTTP/2 over TLS.
* Header `X-JWS-Signature` transmits the detached JWS.
* Authorization via **client credentials** (per-tenant) or bearer JWT in demo mode.

## 7. Error Model

| HTTP | Code               | Meaning                        |
| ---: | ------------------ | ------------------------------ |
|  401 | invalid_signature  | JWS invalid or missing         |
|  401 | invalid_consent    | Consent token invalid          |
|  403 | consent_not_active | Revoked or expired             |
|  403 | board_not_allowed  | Token not scoped to this board |
|  422 | payload_invalid    | Schema/validation error        |
|  502 | upstream_failed    | Board/ATS adapter error        |

## 8. Security

* **Key Management:** per-tenant JWKS with quarterly rotation (overlap period).
* **Replay protection:** Gateway stores payload hash + `jti` to reject duplicates.
* **Rate limits:** sensible per-tenant limits (e.g., 10 rps, burst 100).

## 9. Privacy & GDPR

* **Explicit consent** with scope and expiry.
* **Revocation** immediate for new applies.
* **Data Minimization:** raw payloads retained short-term (≤30 days); receipts/metadata ≤12 months.
* **DSR:** export + delete covering consents, applications, receipts.

## 10. Compatibility

* **Demo mode:** Accepts `ctok:UUID` tokens and skips real JWS verification for local testing during the migration to ADR 0002.
* **Prod mode:** Requires valid consent JWT, detached JWS, optional PII envelope decryption.

## 11. Versioning

* Semantic versioning of the spec string `consent-apply/v{MAJOR.MINOR}`.
* Breaking changes bump **MAJOR**.

## 12. References

* RFC 7515 (JWS), RFC 7517 (JWK), RFC 8785 (JCS), OAuth 2.0 (RFC 6749)
