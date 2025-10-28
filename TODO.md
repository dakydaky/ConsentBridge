Near-Term Productization

Consent UX & Auth
- ✅ ADR 0001 recorded; see docs/adr/0001-consent-ux-auth.md and foundational design in docs/design/consent-ux-auth-foundation.md.
- ✅ Implement tenant & credential persistence (schema + demo seeding in place).
- ✅ `/oauth/token` client credentials endpoint with hashed secret validation + JWT issuance.
- ✅ Enforce bearer tokens on `/v1/applications` (JWT auth + scope policy).
- Deliver consent web flow (email verification, consent approval UI, token issuance via Gateway.CertAuthority).
- Replace `ctok:` demo token with signed JWT + detached-JWS validation per tenant.
Real Signature Handling – Replace AcceptAllVerifier with ES256/EdDSA detached-JWS validation, plug in tenant JWKS discovery, and persist JWS metadata for audits (docs/spec/whitepaper.md and spec-multi.md call out cryptographic guarantees).
Receipts & Provenance – MockBoard currently returns unsigned JSON. Implement the signed-receipt contract, verify receipts server-side, and generate the recruiter-facing provenance card described in the spec pack.
Data & Compliance

Migrations & Seed Data – Formalize EF migrations (already scaffolded) and add seed scripts for demo tenants; include automated dotnet ef database update in container startup once pending-model-changes are eliminated.
DSR Endpoints – Add export/delete APIs, retention windows, and corresponding database workflows as outlined in specs.
Audit Trails – Extend persistence layer with immutable audit tables and payload hashing strategies expected in the whitepaper.
Platform Hardening

Integration & Unit Tests – Introduce test coverage for consent issuance, application flow, mocking external adapters, and signature failure cases (src/Gateway.Test is empty).
Observability & Logging – Wire structured logging (Serilog sinks, correlation IDs), health probes, and metrics/OTel integration mentioned in roadmap.
Configuration & Secrets – Externalize connection strings, JWKS endpoints, and tenant keys via dotenv/Kubernetes secrets; document ops runbooks.
Ecosystem & Delivery

SDK & Client Tooling – Flesh out Gateway.Sdk.DotNet with actual HTTP client, signing helpers, and samples; publish to help partners integrate.
Deployment Assets – Replace placeholder .gitkeep charts/manifests with real Helm/K8s manifests, CI pipeline definitions, and update README setup steps accordingly.
Documentation & Storytelling – Expand README with architecture diagrams, API walkthroughs, and troubleshooting; align Swagger metadata with the published OpenAPI in docs/api/openapi.yaml.

