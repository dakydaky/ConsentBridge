# ConsentBridge MVP Roadmap
_Last refreshed: 2025-10-28_

**Legend:** ✅ delivered · 🚧 in progress · ⏳ queued · 🧭 requires ADR decision

## 1. Consent UX & Auth
- ✅ **ADR 0001: Consent UX & Auth foundation** — recorded in `docs/adr/0001-consent-ux-auth.md` with supporting UX design.
- ✅ **Tenant & credential persistence** — schemas and demo seeding in place for agent/board tenants.
- ✅ **Client credentials OAuth flow** — `/oauth/token` issues JWTs with hashed secrets and scope enforcement.
- ✅ **Protected application APIs** — `/v1/applications` now enforce bearer tokens and scope policies.
- ✅ **Consent web flow MVP** — OTP verification, Razor pages, and consent issuance path shipped.
- ✅ **Signed consent tokens** — per-tenant ES256 JWTs issued with hashed ledger entries and automatic key rotation.
- ✅ **Consent session lifecycle policy** — renewal service + endpoint, acceptance within expiry grace window, and configurable lead/grace periods.
  - ✅ Follow-ups delivered: audit events, operator docs, and lifecycle metrics.
- ⏳ **Agent & board scope management UX** — surface scopes and authorization messaging inside the consent flow.
- ⏳ **OTP throttling & lockout rules** — rate-limit verification attempts and capture audit events.

## 2. Data Protection & Compliance
- ✅ **Retention sweeps** — automated removal of aged consent requests and receipt payloads with configurable windows.
- ✅ **DSR endpoints** — export/delete APIs online; SLA tracking remains to be wired up.
- ⏳ **Retention SLA instrumentation** — metrics and alerts to prove sweeps run within policy windows.
- 🚧 **Immutable audit trail tables** — schema in place; lifecycle + token‑grace events emitted; verification service + admin endpoints added. Next: background verifier, daily digest export, broaden emission (revocation, submission, receipt).
- ⏳ **PII field-level encryption strategy** — implement `candidate.pii_enc` handling aligned to spec guidance.
- ⏳ **DSR export packaging** — deliver signed archive responses and documented operator flow.

## 3. Platform Hardening & Ops
- ⏳ **Integration & regression tests** — add coverage for consent issuance, application flow, adapter error paths, and signature failure handling.
- ⏳ **Load-test harness** — scripted consent/application submissions with configurable tenant mix.
- ⏳ **Structured logging & correlation IDs** — Serilog enrichers, trace IDs, and request-scoped metadata.
- ⏳ **Health & readiness probes** — ASP.NET health checks plus container compose wiring.
- ⏳ **Metrics / OpenTelemetry bridge** — emit gateway and adapter telemetry to OTel collectors.
- ⏳ **Centralised secret management** — externalise connection strings, JWKS endpoints, and tenant keys via env/secret stores.
- ⏳ **Configuration runbooks** — operator documentation, `.env` templates, and troubleshooting decision tree.

## 4. Data Layer & Migrations
- ✅ **EF migrations scaffolded** — baseline migration committed alongside tooling.
- ⏳ **Formal migration pipeline** — ensure `dotnet ef database update` runs on container startup.
- ⏳ **Seed scripts for demo tenants** — scripted identities, keys, and sample payloads for demo environments.
- ⏳ **Migration rollback playbook** — define down-migrations, backups, and recovery steps.

## 5. Ecosystem & Delivery
- ⏳ **Gateway .NET SDK** — ship HTTP client, signing helpers, usage samples, and NuGet packaging.
- ⏳ **Language-agnostic quickstarts** — publish Node/Python examples covering consent issuance and application submission.
- ⏳ **Deployment assets** — replace placeholder manifests with Helm/K8s charts and CI pipeline definitions.
- ⏳ **Release automation** — versioned container images, changelog generation, and tag promotion workflow.
- ⏳ **Documentation revamp** — expand README with architecture diagrams, API walkthroughs, troubleshooting, and Swagger/OpenAPI alignment.

## 6. MockBoard Adapter UX
- ✅ **Dashboard of recent applies** — Razor-based landing page shipped.
- ✅ **Payload modal viewer** — gallery-style rendering of parsed payload details.
- ✅ **Signed receipt viewer** — displays receipt payload and signature provenance.
- ⏳ **Filtering & search** — filter feed by status, consent, or job reference.
- ⏳ **Live updates** — push new applications into the dashboard without manual refresh.

## 7. Security & Risk
- ⏳ **Threat model refresh** — update STRIDE analysis for consent issuance and board adapters.
- ⏳ **Key rotation playbook** — automation and runbook for updating tenant JWKS material.
- ⏳ **Pen-test readiness checklist** — logging, alerting, and break-glass access controls.
