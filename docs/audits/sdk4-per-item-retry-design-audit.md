# SDK-4 — Per-Item Retry Design Audit

**Status:** Complete (design). Implementation started in **SDK-4A**.  
**Date:** 2026-07-16

## Recommendation (staged hybrid)

1. **Execution = child run** — retry starts a new workflow run with `RetryOfRunId = originalRunId`.
2. **History = append-only attempts** — `OperationItemRetryAttempts`; do **not** overwrite parent item `Status` / `ErrorCode` / `ErrorMessage`.
3. Optional non-destructive parent updates: `RetryCount` / `LastAttemptedAtUtc` only.

## Why

- `IOperationItemStore` is append-oriented (no item status update API for retry overwrite).
- Run-level retry already creates child runs via `IWorkflowStartDispatchService`; per-item retry must use a real start idempotency key (unlike historical run retry which used `null`).
- FileImport already marks failed rows `IsRetryable` and stores safe `fixtureId` + `rowNumber` metadata (SDK-3C).

## Authz

Reuse execute-equivalent permission (`IWorkflowRunRetryAuthorizationService` / `workflow.execute` / definition `RequiredPermissions`). Do not expose retry to read-only operators.

## Staging

| Slice | Scope |
|-------|--------|
| **SDK-4A** | Attempt persistence, idempotent API, FileImport single-row retry dispatch; stop at `Dispatched` |
| **SDK-4B** | Attempt outcome reconciliation (`Succeeded`/`Failed`) + enriched read model (lazy + Worker hook) |
| **SDK-4C** | Run Detail per-item retry UI actions (complete) |

## Explicitly deferred

Batch retry, policies, scheduled/automatic retries, Systems/Services model, business entity store, cross-integration reconciliation.
