Near-Term Productization

Consent UX & Auth
- ✅ ADR 0001 recorded; see docs/adr/0001-consent-ux-auth.md and foundational design in docs/design/consent-ux-auth-foundation.md.
- ✅ Implement tenant & credential persistence (schema + demo seeding in place).
- ✅ `/oauth/token` client credentials endpoint with hashed secret validation + JWT issuance.
- ✅ Enforce bearer tokens on `/v1/applications` (JWT auth + scope policy).
- ✅ Deliver consent web flow MVP (OTP verification, Razor pages, consent issuance).
- Replace `ctok:` demo token with signed JWT + detached-JWS validation per tenant.
Real Signature Handling – Replace AcceptAllVerifier with ES256/EdDSA detached-JWS validation, plug in tenant JWKS discovery, and persist JWS metadata for audits (docs/spec/whitepaper.md and spec-multi.md call out cryptographic guarantees).
✅ Receipts & Provenance – Signed receipt contract in place (MockBoard signing, gateway verifies & stores hash/signature) with `/applications/{id}` portal view rendering provenance payload.
Data & Compliance

Migrations & Seed Data – Formalize EF migrations (already scaffolded) and add seed scripts for demo tenants; include automated dotnet ef database update in container startup once pending-model-changes are eliminated.
DSR Endpoints – Add export/delete APIs, retention windows, and corresponding database workflows as outlined in specs.
Audit Trails – Extend persistence layer with immutable audit tables and payload hashing strategies expected in the whitepaper.
Platform Hardening

Integration & Unit Tests – Expand coverage beyond the new receipt signer/verifier tests to include consent issuance, application flow, adapter error paths, and signature failure handling end-to-end.
Observability & Logging – Wire structured logging (Serilog sinks, correlation IDs), health probes, and metrics/OTel integration mentioned in roadmap.
Configuration & Secrets – Externalize connection strings, JWKS endpoints, and tenant keys via dotenv/Kubernetes secrets; document ops runbooks.
Ecosystem & Delivery

SDK & Client Tooling – Flesh out Gateway.Sdk.DotNet with actual HTTP client, signing helpers, and samples; publish to help partners integrate.
Deployment Assets – Replace placeholder .gitkeep charts/manifests with real Helm/K8s manifests, CI pipeline definitions, and update README setup steps accordingly.
Documentation & Storytelling – Expand README with architecture diagrams, API walkthroughs, and troubleshooting; align Swagger metadata with the published OpenAPI in docs/api/openapi.yaml.


\nMockBoard UX\n- [x] Add Razor-based dashboard with recent applies\n- [ ] Surface real payload details (parse JSON)\n- [ ] Signed receipt display & download
