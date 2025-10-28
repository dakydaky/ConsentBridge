# ADR 0004 – Observability & Telemetry Stack

| Field   | Value |
|---------|-------|
| Status  | Proposed |
| Date    | 2025-10-28 |
| Owners  | Platform Reliability |
| Tags    | observability, logging, metrics, tracing |

## Context
ConsentBridge spans multiple services (Gateway.Api, Gateway.CertAuthority, MockBoard adapter, background jobs) with security-sensitive workflows (consent issuance, application submission, key rotation). Current logging is ad-hoc: console output with minimal correlation, no central metrics, and no distributed tracing. As we progress toward production readiness, we must ensure reliable observability:
- Diagnose failures across consent/apply flows quickly.
- Provide metrics for SLA reporting (retention sweeps, DSR requests, token issuance).
- Monitor security-sensitive operations (signature verification failures, key rotations).
- Enable incident response with traceable request lifecycles.

## Problem Statement
We need a cohesive observability stack that:
- Captures structured logs with tenant/request correlation.
- Emits metrics compatible with cloud-native monitoring.
- Provides distributed tracing across gateway components and adapters.
- Remains lightweight for local development but scales to production.

## Decision Drivers
- **Operational insight** — Rapid diagnosis of issues without manual log scraping.
- **Security monitoring** — Detect anomalous traffic or signature failures.
- **Developer ergonomics** — Simple local setup; avoid bespoke agents.
- **Cost and vendor neutrality** — Prefer open standards (OpenTelemetry) to defer vendor commitment.

## Options Considered
1. **Stick with console logs + ad-hoc metrics** — Low effort but inadequate for complex workflows (Rejected).
2. **Adopt a vendor-specific APM suite early (e.g., Datadog, New Relic)** — Rich features but incurs cost/lock-in before MVP (Deferred).
3. **Standardise on OpenTelemetry for traces/metrics, Serilog for structured logs, with pluggable exporters** (Chosen).

## Decision
Implement an observability stack based on OpenTelemetry instrumentation and Serilog structured logging, with outputs configurable per environment (console/seq locally, OTLP to collector in higher environments).

### Architecture Overview
- **Logging** — Serilog pipelines capturing JSON logs with enrichers for trace IDs, tenant IDs, consent/application IDs. Default sinks:
  - Local/dev: console + optional Seq (docker).
  - Production: OTLP exporter to collector or direct to managed log service (configurable).
- **Tracing** — OpenTelemetry SDK integrated into Gateway.Api, Gateway.Application, MockBoard adapter. Propagate `traceparent` headers through HTTP clients. Expose OTLP traces.
- **Metrics** — OpenTelemetry metrics for key indicators: consent issuance counts, application submission latency, signature verification outcomes, retention sweep durations, queue lengths.
- **Collector** — Deploy OpenTelemetry Collector sidecar/agent with pipelines to vendor-neutral backends (Prometheus/Grafana, Jaeger) or cloud services.
- **Correlation** — Standardise correlation identifiers (`cb.trace_id`, `cb.tenant_id`, `cb.consent_id`, `cb.application_id`) across logs, traces, and metrics.

## Consequences
### Positive
- Unified view of system health; traces tie together consent flow hops.
- Metrics underpin SLA dashboards and alerting.
- Structured logs provide forensic detail with minimal parsing.

### Risks / Mitigations
- **Instrumentation overhead** — Keep sampling configurable; default 100% in staging, reduce in prod if necessary.
- **Collector management complexity** — Start with docker-compose setup, document Kubernetes deployment later.
- **Developer friction** — Provide scripts and docs to spin up local observability stack quickly (Seq/Jaeger/Prometheus).

## Implementation Plan
1. **Logging standardisation** — Configure Serilog enrichers and sinks across services; adopt consistent log schema.
2. **Tracing instrumentation** — Add OpenTelemetry middleware, instrument HTTP clients, and propagate context between services.
3. **Metrics** — Define metric catalogue, register instruments (counters, histograms), expose `/metrics` endpoint for scraping.
4. **Collector deployment** — Add docker-compose services for Seq/Prometheus/Jaeger and OTEL collector configuration; document usage.
5. **Alerts/dashboard templates** — Provide starter Grafana dashboards and alerting rules for critical signals (token issuance failure rate, audit hash verification errors).

## Follow-up Actions
- Integrate security monitoring to alert on anomalous metrics (e.g., spike in invalid signatures).
- Evaluate managed observability vendors post-MVP for long-term operations.
- Update runbooks with troubleshooting guidance using logs/traces/metrics.

## References
- OpenTelemetry specification (https://opentelemetry.io/)
- Serilog documentation (https://serilog.net/)
- ADR 0001, ADR 0002, ADR 0003 for related feature context
