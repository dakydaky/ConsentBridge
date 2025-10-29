# Consent Lifecycle Operations Guide

This guide explains how to operate consent token TTL, renewal, and post‑expiry handling.

## Configuration

App settings (appsettings.json / env):

```
ConsentLifecycle:
  RenewalLeadDays: 14    # Window before token expiry when renew is allowed
  ExpiryGraceDays: 7     # Window after token expiry when token is still accepted
```

Environment variables:

```
ConsentLifecycle__RenewalLeadDays=14
ConsentLifecycle__ExpiryGraceDays=7
```

Defaults favor reliability for demos while remaining compliant.

## Policy

- Consent expiry (Consent.ExpiresAt) is strict. No grace. Renewals are not permitted once consent expires.
- Token expiry allows a short, configurable grace window to absorb client/network drift.
- Applications require both: active, unexpired consent AND a token that is not expired or within grace.

## Renewal Flow

- Endpoint: `POST /v1/consents/{id}/renew` (agent scope `apply.submit`)
- Allowed when within RenewalLeadDays before token expiry or within ExpiryGraceDays after token expiry.
- Response includes the new token, `kid`, `alg`, `issued_at`, and `expires_at`.

## Observability

- Logs structured audit events (prefix `AUDIT`):
  - `consent renewal_succeeded` | `consent renewal_denied`
  - `application token_grace_accept` | `application token_grace_reject`
- Metrics (System.Diagnostics.Metrics):
  - `consent.renewals.success` | `consent.renewals.denied`
  - `applications.token_grace.accepted` | `applications.token_grace.rejected`

Export via OpenTelemetry or your metrics backend using a .NET metrics exporter.

## Tips

- To demonstrate grace acceptance, reduce `ExpiryGraceDays` and attempt a submission just after token expiry.
- For strict compliance demos, set `ExpiryGraceDays=0`.
- For high-churn testing, increase `RenewalLeadDays` to encourage renewals before expiry.

