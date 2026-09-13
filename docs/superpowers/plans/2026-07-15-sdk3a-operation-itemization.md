# SDK-3A Operation Itemization Persistence & Writer — Plan

> **For agentic workers:** Spec = `docs/audits/sdk3-itemized-operation-model-design-audit.md`. Hybrid platform store + module writer. No read APIs, UI, FileImport adapter, or retry execution.

**Goal:** Durable `OperationGroup`/`OperationItem` SQL store + `IOperationItemWriter` with Begin/Append/Complete, exposed on step execution context.

**Architecture:** Domain/contracts in Core.Application, EF entities in Persistence.SqlServer, context-bound writer wrapping store via `context.Signals.OperationItems`.

## Tasks

1. Domain records, statuses, bounds, validation — **done**
2. Entities + DbContext + migration + `IOperationItemStore` / Sql implementation — **done**
3. `IOperationItemWriter` + DefaultWorkflowExecutor/Harness wiring — **done**
4. Unit tests (in-memory) + SQL soft-skip integration test + docs — **done**
5. Build/test — **done**

## Out of scope (follow-ups)

- **SDK-3B** — read APIs / run-detail query model
- **SDK-3C** — FileImport adapter to platform writer
- **SDK-3D** — Run Detail itemization panel
- **SDK-4** — per-item retry execution and UI
