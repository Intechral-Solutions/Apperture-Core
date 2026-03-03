# Observability

## Telemetry entities

| Entity | Purpose |
|--------|---------|
| WorkflowInstance | Workflow execution state, status, current step |
| WorkflowStepInstance | Step-level state, attempts, errors, timing |
| OperationRun | Top-level run record (surfaced in UI and API) |
| TelemetryEvent | Structured events with correlation/causation IDs |

## Correlation and causation

- Pass **correlation IDs** through the execution chain for tracing
- Idempotency keys for workflow starts: `ScheduleId:FireTimeUtc` or user-provided
- Causation IDs link child runs to parent actions

## Logging

TBD — Structured logging format (e.g. JSON), levels, sinks. Align with .NET OpenTelemetry where applicable.
