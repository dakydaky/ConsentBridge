# ConsentBridge
**EU Consent‑First Apply Gateway** for job boards, ATS vendors, and trusted third‑party agents.

> **Mission:** Make job applications *authorized, auditable, and interoperable* — turning gray‑area automation into a standards‑based, GDPR‑grade flow.

<p align="center">
  <img alt="ConsentBridge" src="https://dummyimage.com/1200x320/111827/ffffff&text=ConsentBridge" />
</p>

<div align="center">

[![Build](https://img.shields.io/badge/build-dotnet_9-brightgreen)](#)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](#license)
[![Status](https://img.shields.io/badge/status-MVP-green)](#roadmap)
[![Made for EU](https://img.shields.io/badge/made_for-EU-0052b4)](#gdpr--privacy)

</div>

---

## ✨ What is ConsentBridge?
ConsentBridge is a **gateway** that lets candidates explicitly **authorize** third‑party agents to apply on their behalf. Agents submit **signed application payloads**; boards/ATS return **signed receipts**. The platform logs everything for **auditability** and supports **DSR** (data subject requests) out of the box.

**Why it matters**
- ✅ **Compliant**: explicit consent, revocation, and Article‑22 transparency.
- 🔒 **Trustable**: cryptographically signed payloads & receipts.
- ⚡ **Low‑friction**: simple HTTP APIs, SDKs, and a consent UI.

---

## 🧱 Solution layout (C#/.NET 9)
```text
consentbridge/
├─ src/
│  ├─ Gateway.Api/                 # ASP.NET Core Minimal APIs + OpenAPI
│  ├─ Gateway.Domain/             # Entities, Value Objects, Policies
│  ├─ Gateway.Application/        # Use-cases, Handlers, Service interfaces
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

## 🗺️ Architecture (at a glance)
```
Agent → [Signed ApplyPayload] → ConsentBridge → Board/ATS Adapter → Board/ATS
                 ↑                   ↓                           ↑
          Consent Token (JWT)   Audit + DSR                Signed Receipt (JWS)
```
- **OAuth‑style consent** issues a **Consent Token (JWT)** to the agent.
- Agent sends **detached‑JWS** signed ApplyPayload; Board/ATS returns a **signed receipt**.
- **Audit log** captures who/what/when; **DSR** provides export/delete.

---

## 🚀 Quickstart
### 1) Prereqs
- Docker + Docker Compose
- (Optional) .NET 9 SDK for local builds

### 2) Run locally
```bash
docker compose up --build
# Gateway API → http://localhost:8080/swagger
# Mock Board   → http://localhost:8081/health
```

### 3) Create a consent
```bash
curl -s -X POST http://localhost:8080/v1/consents \
  -H "Content-Type: application/json" \
  -d '{
    "CandidateEmail":"alice@example.com",
    "AgentTenantId":"agent_acme",
    "BoardTenantId":"mockboard_eu"
  }'
```
→ Copy `consent_token` from the response.

### 4) Submit a (signed) application
```bash
ACCESS_TOKEN=REPLACE_WITH_TOKEN
TOKEN=ctok:REPLACE
curl -s -X POST http://localhost:8080/v1/applications \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H 'Content-Type: application/json' \
  -H 'X-JWS-Signature: demo.signature' \
  -d '{
    "ConsentToken": "'"$TOKEN"'",
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
      "CoverLetter": {"Text": "Hello!"},
      "Answers": [{"QuestionId": "q_legal_work", "AnswerText": "Yes"}]
    },
    "Meta": {"Locale": "de-DE", "UserAgent": "agent/0.1", "Ts": "2025-10-27T10:15:00Z"}
  }'
```

### 5) Revoke consent
```bash
curl -X POST http://localhost:8080/v1/consents/{consent_id}/revoke -i
```

---

## 📦 Features (MVP)
- **Consent UI & tokens** (OAuth‑style) with **revocation**
- **Signed ApplyPayloads** (ES256/EdDSA) & **signed receipts**
- **Evidence**: audit trail, payload hashes, timestamps
- **DSR**: export & delete endpoints
- **SDKs**: .NET (more to come)

---

## 🔌 API Surface (minimal)
- `POST /v1/consents` → create consent intent (returns `consent_token`)
- `POST /v1/applications` → submit **detached‑JWS** signed ApplyPayload
- `GET /v1/applications/{id}` → retrieve application status
- `POST /v1/consents/{id}/revoke` → revoke consent
- `POST /oauth/token`  → client credentials grant (hashed secrets + JWT access tokens)
- `GET /.well-known/jwks.json` → platform public keys (planned)

See **`docs/api/openapi.yaml`** for full schemas.

---

## 🔐 GDPR & Privacy
- **Explicit consent** with scopes and expiry
- **Revocation** takes effect immediately for new applies
- **Data minimization** & short retention for raw payloads
- **DSR** endpoints (export/delete)
- **Tenant isolation** & key management (platform + per‑tenant JWKS)

---

## 🛡️ Security Model
- **Mutual auth** via client credentials (per tenant)
- **Detached JWS** signatures for ApplyPayloads
- **Receipts** signed by Boards/ATS
- **Key rotation** & JWKS discovery

> Demo mode may stub verification while you wire your keys. Don’t run in prod without real verification.

---

## 🧪 Local Development
```bash
# Hot reload API
dotnet watch run --project src/Gateway.Api

# Create migration
dotnet ef migrations add Initial \
  --project src/Gateway.Infrastructure \
  --startup-project src/Gateway.Api

# Apply migration
dotnet ef database update \
  --project src/Gateway.Infrastructure \
  --startup-project src/Gateway.Api
```

---

## 🗺️ Roadmap
- [ ] Real JWS verification (ES256/EdDSA) + JWKS endpoint
- [ ] Client credentials auth per tenant
- [ ] Signed receipts verifier & provenance card for recruiters
- [ ] Candidate portal (consent dashboard, DSR)
- [ ] Helm chart + production hardening (OTel, rate limits)

---

## 🤝 Contributing
PRs welcome! Please:
1. Open an issue with context and acceptance criteria.
2. Keep PRs small and focused.
3. Include tests where feasible.

---

## 📄 License
[MIT](./LICENSE) — see the LICENSE file for details.

---

## 🙌 Credits
Built with ❤️ for EU job seekers, boards, and ATS vendors who want **trust without friction**.



