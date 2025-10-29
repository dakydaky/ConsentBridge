# ConsentBridge MVP Roadmap
_Last refreshed: 2025-10-29_

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
  - 🧭 Rate-limit policy and UX messaging to be decided.

## 2. Data Protection & Compliance
- ✅ **Retention sweeps** — automated removal of aged consent requests and receipt payloads with configurable windows.
- ✅ **DSR endpoints** — export/delete APIs online; SLA tracking remains to be wired up.
- ⏳ **Retention SLA instrumentation** — metrics and alerts to prove sweeps run within policy windows.
- 🚧 **Immutable audit trail tables** — schema in place; lifecycle + token‑grace events emitted; verification service + admin endpoints; background verifier + daily digest export; broadened emission (consent issuance/denial/revocation, application created/accepted/failed, receipt verify/fail, key rotation); DSR export includes audit entries.
  - 🚧 Next: DB role hardening rollout (apply `scripts/db/secure-audit.sql` GRANT/REVOKE in deploy), finalize WORM/off‑cluster digest storage policy (object storage), add integrity failure alerting.
- ⏳ **PII field-level encryption strategy** — implement `candidate.pii_enc` handling aligned to spec guidance.
- ⏳ **DSR export packaging** — deliver signed archive responses and documented operator flow.

## 3. Platform Hardening & Ops
- 🚧 **Integration & regression tests** — unit coverage increased (lifecycle, verifier). Add end‑to‑end tests for application grace path and audit emission.
- ⏳ **Load-test harness** — scripted consent/application submissions with configurable tenant mix.
- 🚧 **Structured logging & correlation IDs** — audit metadata carries correlation IDs (X‑Correlation‑ID/TraceIdentifier). Add Serilog enrichers and request logging.
- ⏳ **Health & readiness probes** — ASP.NET health checks plus container compose wiring.
- ⏳ **Metrics / OpenTelemetry bridge** — emit gateway and adapter telemetry to OTel collectors.
- ⏳ **Centralised secret management** — externalise connection strings, JWKS endpoints, and tenant keys via env/secret stores.
- ⏳ **Configuration runbooks** — operator documentation, `.env` templates, and troubleshooting decision tree.

## 4. Data Layer & Migrations
- ✅ **EF migrations scaffolded** — baseline migration committed alongside tooling.
- 🚧 **Formal migration pipeline** — app performs startup migration; add CI/CD migration step and rollback playbook.
- ⏳ **Seed scripts for demo tenants** — scripted identities, keys, and sample payloads for demo environments.
- ⏳ **Migration rollback playbook** — define down-migrations, backups, and recovery steps.

## 5. Ecosystem & Delivery
- ✅ **CI test reporting** — GitHub Actions publishes TRX results, artifacts, and PR summaries.
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

## 8. Investor Demo (E2E Perspectives)
- ⏳ **Mock Agent Console (Razor)** — lightweight UI to represent the agent perspective without Swagger. MVP screens: Dashboard, Request Consent, Submit Application, Application Detail, Consent Detail. See `docs/ux/mock-agent-console.md`.
  - Acceptance: End-to-end demo runs using Consent Web + Mock Agent Console + MockBoard dashboard with no CLI/Swagger on screen.
- ⏳ **Runbook & user journeys** — add non-developer docs to drive the demo: `docs/ux/investor-demo-runbook.md`, `docs/ux/user-journeys.md`.
  - Acceptance: Investor can follow the storyboard to see Candidate → Agent → Board → Audit flows.
- ⏳ **Demo data seeding** — script or configure sample candidates/jobs consistent with runbook (Alice, ACME, MockBoard EU, job `mock:98765`).
  - Acceptance: Single `make demo-seed` or `./demo.ps1 -Seed` prepares environment.
- ⏳ **Audit verification snapshot** — prepare a success screenshot or minimal UI showing a verified audit window per ADR 0003.
  - Acceptance: Integrity proof is demonstrable without terminal output.
