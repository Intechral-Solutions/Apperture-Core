# Durable Workflows

## Portable model

Workflow definitions are backend-agnostic. See [ADR-0003](../02-decisions/ADR-0003-runtime-portability.md).

- Typed parameter schema (for UI-generated forms)
- Step graph (linear sequence first; extensible later)
- Operational attributes: category (Query vs Command), retry policy, idempotency rules

## Persistence

- **On-prem:** SQL Server stores workflow instance state, step state, results, telemetry.
- **Cloud:** Durable Functions backing store (Azure Storage) plus consistent telemetry/audit emission to instance store.

## Standard output

- `OperationResult` — Consistent shape across workflows
- Standardized telemetry events
