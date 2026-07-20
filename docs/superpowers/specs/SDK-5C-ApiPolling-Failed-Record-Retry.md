# SDK-5C — ApiPolling Failed-Record Retry

**Date:** 2026-07-20  
**Status:** Implemented  
**Prior:** [SDK-5B](SDK-5B-ApiPolling-Durable-Sync-State.md) · [SDK-4A](../plans/2026-07-16-sdk4a-operation-item-retry-dispatch.md)

## Goal

Prove SDK-4 per-item retry works for API polling / sync-style records via `Sample.ApiPolling`.

## Behavior

| Piece | Detail |
|-------|--------|
| Handler | `ApiPollingOperationItemRetryHandler` |
| Eligibility | Failed + IsRetryable + metadata `snapshotId` + `sequence` (+ optional `includeDeleted`) + record exists in snapshot |
| Child params | `retryMode`, `retrySequence`, `snapshotId`, `parentOperationItemId`, `retryAttemptId` |
| Child group | `operationKind=api_poll_retry`, exactly one item |
| Child metadata | `retryAttemptId`, `parentOperationItemId`, `snapshotId`, `externalId`, `changeKind`, `sequence` |
| Signals | Existing `sync.*` (exactly one, fail-loud) |
| Watermark | Commit **that one record only** on Succeeded; discard on Failed; never resetScope |
| Parent item | Status / ErrorCode / ErrorMessage immutable |

## Metadata (SDK-5C+)

Failed items now include `sequence` and `includeDeleted` for stable relocate. Pre-SDK-5C items missing `sequence` are blocked (`missing_retry_metadata`).

## Deferred

Retry-all, batch/scheduled/automatic retry, policies, live HTTP/OAuth record refresh.
