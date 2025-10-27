# ConsentBridge: The EU Consent‑First Apply Gateway

## Abstract

ConsentBridge is an EU‑first trust layer that transforms automated job applications from a gray‑area practice into an authorized, auditable, and interoperable standard. This whitepaper consolidates the strategic rationale, compliance framing, architecture, multi‑agent context, protocol specification, security model, market approach, and implementation plan into a single reference document.

---

## 1. Executive Summary

Recruitment is increasingly intermediated by automation tools that draft and submit applications at scale. Employers and boards face spam, provenance doubt, and policy violations; candidates face opaque automation and privacy concerns. ConsentBridge provides a standards‑based gateway for **explicit, revocable consent** and **cryptographic provenance** of job applications. It enables job boards and ATS vendors to accept third‑party, agent‑submitted applications safely while giving candidates control, visibility, and data subject rights.

**Core claims**

* **Compliance by design:** GDPR, consent, revocation, DSR, auditability, and Article‑22 transparency.
* **Reliability and trust:** Detached‑JWS signed ApplyPayloads with verifiable receipts and replay protection.
* **Interoperability:** Minimal, language‑agnostic protocol with SDKs. Works with board/ATS adapters and candidate agents.
* **EU‑first wedge:** Focused on Europe’s regulatory environment and partner ecosystem for defensibility.

---

## 2. Problem Landscape

* **Unauthorized automation:** Existing auto‑apply tools often scrape or simulate users, violating ToS and eroding trust.
* **Recruiter signal quality:** Spray‑and‑pray submissions reduce conversion and waste screening time.
* **Privacy and governance debt:** Limited user agency over how/where data is used; missing export/delete.
* **Integration friction:** Each board/ATS reinvents apply endpoints, creating brittle custom work.

**What stakeholders need**

* **Candidates:** control, transparency, revocation, and outcomes.
* **Boards/ATS:** authorized traffic, lower fraud, higher completion, auditable provenance.
* **Agents:** a legitimate pipe to deliver high‑quality applications.

---

## 3. Vision & Scope

**Vision:** Make authorized, evidence‑backed applications the default in Europe.

**Scope of v1**

* Consent issuance, validation, and revocation.
* Signed ApplyPayload submission and signed receipt verification.
* Audit logging and DSR endpoints.
* SDKs (.NET first) and a mock board adapter for easy adoption.

**Out of scope (v1):** Offer negotiation, interview scheduling, or bypassing anti‑bot defenses on non‑partner sites.

---

## 4. Legal & Compliance (EU‑First)

**Design principles**

* **Lawful basis:** Consent, recorded with scopes, expiry, and revocation metadata.
* **Transparency:** Candidate portal shows who applied, what, when, to where; explains automated steps.
* **Data minimization:** Store only what’s required; short retention for raw payloads.
* **DSR Support:** Export and delete across consents, applications, receipts.
* **Article‑22 considerations:** Human‑in‑the‑loop at the candidate agent; explainability of materials and decisions.

**Data retention (v1)**

* Raw ApplyPayloads ≤ 30 days; receipts/metadata ≤ 12 months; configurable per tenant.

**Cross‑border controls**

* EU data residency by default; SCCs for sub‑processors as needed.

---

## 5. Market Positioning

**Primary ICP:** Mid‑size EU job boards; **Secondary:** EU‑operating ATS; **Credibility wedges:** universities/bootcamps; **Long‑cycle:** public employment portals.

**Differentiation**

* Consent‑first design vs. scraping.
* Cryptographic provenance vs. heuristic detection.
* Interoperability standard vs. one‑off integrations.

**Monetization**

* Platform SaaS + per‑apply events; optional rev‑share with boards/ATS; university flat fees.

**12–18 month revenue scenarios**

* Low: ~€0.2M ARR; Base: ~€0.9M ARR; High: ~€4.5M ARR (see GTM section for assumptions).

---

## 6. Architecture Overview

```
Agent → [Signed ApplyPayload] → ConsentBridge → Board/ATS Adapter → Board/ATS
                 ↑                   ↓                           ↑
          Consent Token (JWT)   Audit + DSR                Signed Receipt (JWS)
```

**Components**

* **Gateway.Api**: Minimal APIs, OpenAPI, consent/workflows.
* **Gateway.Domain**: Entities, DTOs, policies.
* **Gateway.Application**: Use‑cases, orchestration, verification.
* **Gateway.Infrastructure**: EF Core/Postgres, storage, queue.
* **Gateway.CertAuthority**: JWK/JWT issuance, rotation.
* **Gateway.Sdk.DotNet**: Agent/board SDK.
* **Adapters**: Board/ATS integrations (MockBoard provided).

**Data model (core)**

* `candidates`, `consents`, `applications`, `keys`, `audits`.

---

## 7. Protocol: Consent‑Apply v0.1

**Consent**

* OAuth‑like flow yields **Consent Token (JWT)** with `sub`, `aud`, `iss`, `scope`, `boards[]`, `exp`, `jti`.
* Revocation invalidates future applies immediately.

**ApplyPayload**

* Canonical JSON (JCS/RFC 8785).
* **Detached JWS** (ES256/EdDSA) in header `X‑JWS‑Signature`.
* Optional **envelope encryption** of PII using board public key.

**Receipt**

* Board/ATS returns signed JWS with `application_id`, `status`, and reference ID.

**Replay protection**

* Store `(payload_hash, jti)` to reject duplicates.

**Transport**

* HTTP/1.1 or HTTP/2 over TLS; client credentials per tenant.

(Full OpenAPI & protocol text provided separately in `docs/`).

---

## 8. Security Model & Threats

**Goals**: Integrity, authenticity, non‑repudiation, least privilege, privacy.

**Controls**

* **Authentication/Authorization**: Client credentials per tenant; scoping by consent token.
* **Integrity**: Detached JWS for ApplyPayload and for receipts.
* **Key Management**: JWKS exposure with quarterly rotation and overlap.
* **Rate Limiting**: Per‑tenant RPS/burst caps; anomaly alerts.
* **Auditability**: Immutable logs with consent `jti`, payload hash, signatures, and timestamps.
* **Privacy**: Envelope encryption of PII; minimal retention.

**Threat scenarios & mitigations**

* **Stolen agent key** → Immediate key revoke, JWKS rotation, anomaly detection.
* **Replay** → Payload hash + `jti` replay check.
* **Consent theft** → Audience matching, board allowlist, short expiry, revocation UI.
* **Adapter compromise** → Signed receipts, tenant isolation, allowlist egress.

---

## 9. Implementation Blueprint (C#/.NET)

**Project layout (net9 target)**

```
consentbridge/
├─ src/
│  ├─ Gateway.Api/
│  ├─ Gateway.Domain/
│  ├─ Gateway.Application/
│  ├─ Gateway.Infrastructure/
│  ├─ Gateway.CertAuthority/
│  ├─ Gateway.Sdk.DotNet/
│  └─ Gateway.Test/
├─ deploy/ (docker, helm, k8s)
├─ docs/ (openapi, spec)
└─ tools/
```

**Core interfaces**

* `IJwsSigner`, `IJwsVerifier`, `IConsentService`, `IReceiptVerifier`.

**Operational defaults**

* Auto‑migrations for dev; OTel tracing; Serilog to console; Postgres + pgvector‑ready.

---

## 10. Multi‑Agent Context (Optional Extensions)

Although ConsentBridge is infra, it slots into multi‑agent job‑search systems:

* **Profile Agent** → candidate profile normalization.
* **Discovery Agent** → official APIs (EURES/partners) for listings.
* **Matching Agent** → hybrid semantic + rules scoring with rationales.
* **Material Tailor Agent** → cover letters, Q&A (explainable, no fabrication).
* **Compliance Agent** → site policies; defer to draft‑only when uncertain.
* **Visual Navigator** → perception‑action for sandboxed flows; not required for partner applies.

ConsentBridge formalizes the **apply** step so agents can operate legitimately.

---

## 11. Go‑To‑Market (EU) & Pricing

**ICP order**

1. Mid‑size EU job boards (founding partners program, 60‑day pilot).
2. EU‑operating ATS (design partner; recruiter provenance card).
3. Universities/bootcamps (white‑label portal; outcome dashboards).

**Pricing**

* Boards: €2k–€5k/mo + €0.02–€0.08 per accepted apply.
* ATS: €3k–€8k/mo + €0.01–€0.05 per routed apply.
* Universities: €10k–€30k/yr; bootcamps €2k–€10k/mo.

**Base revenue scenario**

* 8 boards + 2 ATS + 10 universities + 6 bootcamps ≈ **€0.9M ARR**.

**Positioning messages**

* “Authorized traffic, fewer fake applies, measurable conversion lift, and provable compliance.”

---

## 12. Roadmap (T‑12 Weeks MVP)

**W1–2:** Platform skeleton, Postgres, auth scaffolding, consent creation (demo), OpenAPI & logging.
**W3–4:** Consent JWT, revocation, DSR stubs; MockBoard adapter + end‑to‑end accept.
**W5–6:** JWS verify (ES256/EdDSA) + JWKS; signed receipts; audit enrichments.
**W7–8:** Client‑credentials per tenant; rate limits; candidate portal (list, revoke).
**W9–10:** First partner integration (board/ATS); SLA monitors; retention jobs.
**W11:** Security review, load & chaos testing; privacy policy & DPIA draft.
**W12:** Private beta (DE/FR/DK); case studies and spec hardening.

---

## 13. KPIs & Telemetry

* **Adoption:** # tenants (boards, ATS), # certified agents.
* **Quality:** acceptance rate, duplicate rate, provenance verification success.
* **Latency:** p95 submit‑to‑receipt; adapter error rate.
* **Privacy:** DSR SLA (<7 days), consent revocation propagation time.
* **Economics:** events per board, € per apply, infra cost/event, gross margin.

---

## 14. Operations & SRE

* **Observability:** OpenTelemetry traces, metrics, structured logs; dashboards for submit/receipt flows.
* **Resilience:** idempotent handlers, at‑least‑once delivery to adapters, retry with jitter.
* **Back‑pressure:** per‑tenant queues and circuit breakers on failing adapters.
* **Key rotation:** quarterly with overlap; alert on stale `kid` usage.

---

## 15. Privacy Engineering & DSR

* **Export:** ZIP of consents, applications, receipts, and audit summaries.
* **Delete:** Remove payloads and PII; retain minimal hashed references when legally permitted.
* **User experience:** Plain‑language consent screens; explain automation scope and rights.

---

## 16. Risks & Mitigations

* **Standards inertia:** Provide drop‑in SDKs and mock adapters; publish verifier libraries; “Founding Partner” incentives.
* **Key compromise:** Immediate revoke, rotate, and notify; anomaly detection on signing patterns.
* **Low agent adoption:** Launch a **Certified Agent** directory; co‑market with boards/ATS; open‑source SDKs.
* **Regulatory change:** EU‑first legal counsel; modular policy engine per tenant.

---

## 17. Governance & Certification

* **Certified Agent Program:** onboarding checks, rate‑limit tiers, and testing suites.
* **Change control:** Spec semver; public changelog; 90‑day deprecation windows.
* **Compliance:** DPIA, DPO contact, security.txt, and vulnerability disclosure program.

---

## 18. Reference Implementation

* **Ready‑to‑run scaffold** with Docker Compose (Gateway, Postgres, MockBoard).
* Swagger UI for exploration; Makefile tasks for migrations and logs.
* Upgrade path to the full .NET 9 solution layout with CertAuthority and SDKs.

---

## 19. Appendices

### A. OpenAPI Overview

Endpoints: `/health`, `/v1/consents` (create), `/v1/consents/{id}/revoke`, `/v1/applications` (submit), `/v1/applications/{id}` (get), `/.well‑known/jwks.json` (planned). See `docs/api/openapi.yaml`.

### B. Consent‑Apply v0.1 Protocol

Canonical payloads, detached JWS, receipts, error model, replay protection. See `docs/spec/consent‑apply‑v0.1.md`.

### C. Example Payloads

* **ApplyPayload** with CV URL hash, localized metadata.
* **Receipt** with `board_ref` and timestamps.

### D. Security Hardening Checklist

* [ ] Enforce client credentials with per‑tenant scopes.
* [ ] Verify JWS (alg, kid, exp); reject `none`/weak alg.
* [ ] JWKS cache with TTL and pinning.
* [ ] Encrypted PII fields; KMS‑backed key management.
* [ ] Rate limits + circuit breakers.
* [ ] Audit immutability and tamper‑evident logs.

### E. DPIA Outline (Template)

* Processing purposes, data categories, retention, lawful basis, risk assessment, mitigations, DPO contact.

### F. Glossary

* **Agent**: third‑party client acting for the candidate.
* **ApplyPayload**: canonical JSON document describing an application.
* **Detached JWS**: signature separate from payload body.
* **JWKS**: JSON Web Key Set, a published list of public keys.
* **Receipt**: board/ATS‑signed confirmation of submission.

---

## 20. Conclusion

ConsentBridge delivers the missing standard for legitimate, privacy‑respecting automation in hiring. By coupling explicit consent with cryptographic provenance and practical developer tooling, it aligns incentives across candidates, agents, boards, and ATS vendors. The project’s EU‑first stance provides a powerful initial wedge, with a clear path from MVP to ecosystem standard.
