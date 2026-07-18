# SDK-4C — Run Detail Per-Item Retry UI

**Status:** Implemented  
**Date:** 2026-07-17  
**Depends on:** SDK-4A, SDK-4B, SDK-3D  
**Design:** [sdk4-per-item-retry-design-audit.md](../../audits/sdk4-per-item-retry-design-audit.md)

## Delivered

- Run Detail `RunOperationItemizationPanel` Retry action for failed + `isRetryable` items
- Optional reason (bounded), client-generated idempotency key, in-flight disable
- “Not retryable” label for failed non-retryable items
- Retry history (status, times, guidance/errors, child run `DrillInLink`)
- Success feedback with child run link; original item status unchanged in the table
- Management client error body parsing for retry POST/GET
- Parent `OnRetryCompleted` refresh of run detail summary

## Operator notes

1. Expand itemization → find a failed retryable row → Retry.
2. Optionally enter a reason → Submit retry.
3. A child run is created; open it from the success message or retry history.
4. Original item remains Failed.
5. Retry-all / batch retry is not available.

## Deferred

Retry-all, batch retry, policies, scheduled/automatic retry.
