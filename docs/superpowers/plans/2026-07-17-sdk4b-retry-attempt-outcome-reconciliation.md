# SDK-4B — Retry Attempt Outcome Reconciliation & Read Model

**Status:** Implemented  
**Date:** 2026-07-17  
**Depends on:** SDK-4A  
**Design:** [sdk4-per-item-retry-design-audit.md](../../audits/sdk4-per-item-retry-design-audit.md)

## Strategy

**Option C — explicit reconciliation service**, with:

1. Lazy reconcile on `GET .../retries` and idempotent `POST .../retry` replay
2. Best-effort Worker hook after child run `ProcessClaimedAsync` completes

No dedicated background scanner.

## Outcome mapping

| Child signal | Attempt status |
|--------------|----------------|
| Child item `Succeeded` | `Succeeded` |
| Child item `Failed` | `Failed` (copy child error) |
| Child item `Skipped` | `Failed` (`child_item_skipped` or child code) |
| Child run `failed` / `cancelled` with no items | `Failed` |
| Child run completed with no matching item | `Failed` (`missing_child_outcome` / `ambiguous_child_outcome`) |
| Child run still active | remain `Dispatched` |

Never mark `Succeeded` without a positive child item outcome. Parent item status/error remain immutable.

## Linking

Populate `ChildRunId` (SDK-4A), plus `ChildOperationGroupId` / `ChildOperationItemId` when a matching child item is found (prefer `metadata.retryAttemptId`).

FileImport retry mode writes `retryAttemptId` + `parentOperationItemId` into child item metadata.

## Deferred

SDK-4C — Run Detail retry buttons / UI actions.
