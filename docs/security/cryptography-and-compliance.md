# Cryptography & Regulatory Considerations

This note explains how the current demo environment uses cryptography, what guarantees it provides, and how the shape of the system aligns with privacy regulations such as the GDPR. It also calls out limitations and the work that remains before we could make hard compliance statements.

## 1. Signature Model

### 1.1 Agent submissions
- **Algorithm:** Agents sign the canonical `ApplyPayload` JSON with **ES256** (ECDSA over P‑256 with SHA‑256).
- **Private key custody:** In the demo scaffold the agent key is provided as a local JWK (`certs/agent_acme_private.jwk.json`) consumed by the `demo.ps1` helper.
- **Verification:** The gateway validates the detached JWS with the **tenant JWKS** that is loaded at startup (`DemoTenants.Agent.JwksPath`). The signature, key id (`kid`), and algorithm are persisted with the application record for provenance.

### 1.2 Board receipts
- **Algorithm:** MockBoard signs receipt payloads with ES256 using `certs/mockboard_private.jwk.json`.
- **Verification:** The gateway verifies the receipt against the MockBoard JWKS and stores the verified payload, signature, and SHA‑256 hash.

### 1.3 JWKS loading
- **Source:** JWKS documents live in `src/Gateway.Api/jwks/*.jwks.json`. The `ConfigurationTenantKeyStore` reads each file referenced by `DemoTenants.*.JwksPath`.
- **Validation:** JWKS entries are filtered by `kid` and algorithm, then used for signature verification. There is no dynamic fetch/caching yet; rotating keys requires updating the files and restarting the services.

### 1.4 Residual risks
- Local private keys are bundled for demo convenience; in production each tenant must custody and rotate its keys.
- JWKS files are not authenticated beyond filesystem trust; serving them via HTTPS with integrity guarantees remains future work.
- Key rotation and revocation notifications are manual today.

## 2. Data Protection & Cryptographic Goals

| Goal | Current behaviour | Notes |
| ---- | ----------------- | ----- |
| Authenticity | ES256 signatures for agent submissions and board receipts | Requires tenants to protect private keys |
| Integrity | SHA‑256 hashes stored for payload and receipt | No tamper-evident ledger yet |
| Confidentiality | TLS termination assumed outside the demo | At-rest encryption delegated to Postgres deployment |
| Non-repudiation | Submission/receipt signatures plus key ids retained | Formal audit trails still pending |

The system intentionally stores only hashes and canonical payloads needed for provenance. Raw application payloads follow the retention guidance in `docs/spec/whitepaper.md`.

## 3. GDPR Alignment (High Level)

The platform is being shaped to satisfy core GDPR obligations:

- **Lawful basis & consent:** Consent requests capture explicit approval before processing applications.
- **Data minimisation:** Gateway retains only canonical payloads, hashes, and signatures required for provenance. Specs call for automatic deletion of raw payloads after short retention windows.
- **Integrity & confidentiality:** Signatures ensure that only authorised tenants can submit or acknowledge applications; persisted hashes allow detection of modification.
- **Accountability:** Storing the submission `kid`, algorithm, and receipt proofs enables audit trails that regulators expect. Additional logging and immutable audit stores are still TODO.
- **Data subject rights:** Export/delete (DSR) endpoints allow tenants to retrieve or purge candidate data on demand; retention automation remains a roadmap item.

> **Important:** These mechanisms align with GDPR principles, but the current demo is not a certified compliance solution. Production readiness requires operational controls (key rotation, monitoring, documented lawful bases, DPIAs) and completion of outstanding features (DSR automation, secure JWKS distribution, audit trails, infrastructure hardening).

## 4. Recommended Next Steps

1. **JWKS service:** Serve tenant keys via authenticated HTTPS and add caching/refresh logic.
2. **DSR reporting:** Add SLA tracking, notification hooks, and retention automation on top of the new export/delete workflow.
3. **Key rotation & revocation:** Provide tooling to rotate keys and reject obsolete signatures.
4. **Audit trails:** Append-only storage (e.g., event logs) linking signature metadata to human-readable audits.
5. **Documentation:** Update SDP/DPAs once the above controls exist; engage privacy counsel for formal compliance posture.

These steps will move the system from a cryptography-enabled demo toward a deployment that can support rigorous security and privacy commitments.
