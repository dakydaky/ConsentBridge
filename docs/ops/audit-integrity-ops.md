# Audit Integrity Operations Guide

This guide covers automatic audit chain verification and daily digest export.

## Configuration

App settings (appsettings.json / environment):

```
AuditVerification:
  SweepHours: 24          # How often the verifier runs
  WindowDays: 1           # Verification window length per run
  OverlapMinutes: 5       # Overlap to mitigate edge gaps
  DigestOutputPath: /app/audit-digests  # Destination for JSON digests
```

Environment variables:

```
AuditVerification__SweepHours=24
AuditVerification__WindowDays=1
AuditVerification__OverlapMinutes=5
AuditVerification__DigestOutputPath=/app/audit-digests
```

## What it does

- Runs on startup, then every `SweepHours`.
- For each tenant, recomputes the audit hash chain for the last `WindowDays` (with `OverlapMinutes`) and records a verification run.
- Exports a JSON digest per tenant to `DigestOutputPath` with the computed anchor and status.

## On-demand admin endpoints

- `POST /internal/audit/verify?tenant=<slug>&days=<n>`
  - Runs verification from now minus `n` days to now.
- `GET /internal/audit/status?tenant=<slug>&take=<n>`
  - Lists recent verification runs.

## Observability

- Metrics (counters):
  - `audit.verification.success`
  - `audit.verification.failed`

Use your metrics exporter (e.g., OpenTelemetry) to ship these to your backend.

