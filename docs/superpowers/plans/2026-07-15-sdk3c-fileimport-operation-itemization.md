# SDK-3C Sample.FileImport Adapter to Platform Operation Items — Plan

> Spec gate = SDK-3A writer + SDK-3B read APIs + FileImport SDK-2 behavior.

**Goal:** FileImport writes durable `OperationGroup`/`OperationItem` via `context.Signals.OperationItems` while keeping process-local `IImportOutcomeStore` for inspection/tests.

## Mapping

| Local | Platform |
|-------|----------|
| Import batch | `OperationGroup` (`operationKind=file_import`, `sourceLabel=fixtureId`) |
| Row outcome | `OperationItem` (sequence=rowNumber, subjectKey=external id) |
| Batch status | Group status (`Succeeded` / `CompletedWithErrors` / `Failed`) |
| Failed rows | `isRetryable=true` (execution deferred to SDK-4) |
| Skipped (maxRows / failOnFirst) | `Skipped`, not retryable |

## Process

1. **Parse** — structure check → `BeginGroup` → `CreateBatch(OperationGroupId)` (no orphan batch if writer missing)
2. **ProcessRows** — local append + `AppendItem` per outcome
3. **Complete** — `CompleteGroup` → `CompleteBatch` → exactly one signal

Structurally invalid fixtures: hard-fail in Parse with no group (documented).

## Status

- [x] Adapter implemented
- [x] Tests + docs
- [x] Follow-ups: SDK-3D UI panel, SDK-4 retry
