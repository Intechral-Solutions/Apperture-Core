# JOB-1 — Failed-Work Resolution and Bulk Retry

**Status:** Implemented  
**Date:** 2026-08-05  
**Track:** Track 6 — Job Operation Itemization & Retry (completion epic)  
**Related:** [SDK-3](./sdk3-itemized-operation-model-design-audit.md), [SDK-4](./sdk4-per-item-retry-design-audit.md), [post-UIX reentry](./post-uix-product-roadmap-reentry.md), [UIX-6](./uix6-interaction-progressive-disclosure-system.md), [BDS-6](./bds6-cross-run-business-record-activity-lineage.md)

---

## 1. Executive summary

Operators can select failed operation items within **one run + one operation group**, bulk-retry eligible items with truthful partial results, and acknowledge failures that will not be retried—without rewriting historical execution outcomes. Operation-level blocked-work retry remains **inspection-only** as an intentional final product decision; recovery is item-level.

## 2. Operator problem

Per-item retry existed (SDK-4), but large failure sets required repetitive single retries, had no acknowledgement path to clear attention, and left operation blocked-work as an unresolved “not yet supported” placeholder.

## 3. Final domain model

| Concept | Meaning |
| --- | --- |
| Execution outcome | `Pending` / `Succeeded` / `Failed` / `Skipped` on the operation item |
| Retry state | Durable `OperationItemRetryAttempt` rows (Requested/Dispatched/Succeeded/Failed/Blocked) |
| Attention state | Derived: needs attention when Failed, no active acknowledgement, no succeeded retry |
| Acknowledgement | Dedicated `OperationItemAttentionAcknowledgements` rows with disposition + reason |

## 4. Execution outcome versus attention

Acknowledgement **never** changes `Status` to Succeeded. A failed acknowledged item remains Failed with attention cleared. Child retry success also clears attention without mutating the parent outcome.

## 5. Bulk retry contract

```text
POST /api/runs/{runId}/operation-groups/{groupId}/items/retry
```

- Scope: one run + one group only  
- De-duplicates IDs; server max batch **100** (`OperationItemAttentionBounds.MaxBulkItemCount`)  
- Reuses `IOperationItemRetryService.RequestRetryAsync` per item  
- Per-item idempotency key: `bulk:{requestId}:{itemId}`  
- Continues after per-item failures; returns aggregate + per-item categories  

## 6. Batch bound and dispatch strategy

Sequential per-item dispatch (no unbounded `Task.WhenAll`). Conservative default of 100. Cancellation honored between items.

## 7. Idempotency behavior

Repeating the same bulk `requestId` + item set returns `AlreadyRequested` without duplicate child runs. Overlapping concurrent requests rely on existing unique idempotency indexes.

**Ack ↔ retry race rule:** acknowledgement is allowed while a retry is in flight and **does not cancel** dispatched retry work.

## 8. Acknowledgement dispositions

| Value | Operator label |
| --- | --- |
| `ResolvedExternally` | Resolved externally |
| `NoActionRequired` | No action required |
| `WillNotRetry` | Will not retry |

API accepts PascalCase or kebab-case aliases.

## 9. Reopen behavior

`POST .../items/reopen-attention` sets `ReopenedAtUtc` / `ReopenedByUserId` on the active row (history retained). A new acknowledgement may be created afterward.

## 10. Attention and rollup rules

```text
NeedsAttention = Failed && !activeAck && !hasSucceededRetry
```

Run summary adds `ActiveAttentionCount` and `AcknowledgedFailureCount`. Failed outcome counts remain historical. BDS-6 Activity attention filter excludes acknowledged / successfully-retried failures; original Failed events stay visible historically.

## 11. Blocked-work decision (Option B)

**Whole-operation retry is not supported** (final). Rationale: operations are itemized; whole-operation retry would re-run successful work. Supported recovery: Affected records → item retry and/or acknowledgement. API returns `inspectionOnly: true`, `finalDecision: true`, with actionable guidance.

## 12. API contract

| Method | Route |
| --- | --- |
| POST | `/api/runs/{id}/operation-groups/{groupId}/items/retry` |
| POST | `/api/runs/{id}/operation-groups/{groupId}/items/acknowledge` |
| POST | `/api/runs/{id}/operation-groups/{groupId}/items/reopen-attention` |

Authorization: same retry governance as single-item retry (`IWorkflowRunRetryAuthorizationService`). Actor from principal only.

## 13. UI workflow

Run Detail → Affected records: checkbox selection, bulk action bar, `ConfirmActionDialog` for retry and acknowledge, acknowledged badges (Failed remains), reopen attention, truthful partial-result summaries.

## 14. Authorization and audit

Audit actions:

- `execution.operation_item.bulk_retry.requested` / `.completed`
- `execution.operation_item.attention.acknowledged` / `.reopened`

## 15. Performance and safety

Server batch bound; sequential dispatch; structured logging of counts; no new job-batch orchestration subsystem.

## 16. Automated test results

| Suite | Result |
| --- | --- |
| Unit (JOB-1 + related regression filter) | **65 passed** |
| Management UI Shared (full) | **577 passed** |
| Architecture tests | **34 passed** |
| Management API / Client / Security builds | Succeeded |

Live Aspire operator smoke (checklist A–I in the epic) remains a manual gate after applying migration `20260805200000_Job1_OperationItemAttentionAcknowledgements`.

## 17. Runtime smoke results

Automated contracts and UI wiring are covered by the suites above. Full Aspire operator smoke (generate failures → bulk retry → partial result → acknowledgement → BDS-6 → blocked-work wording → console gate) should be run against a live instance before considering the epic operator-verified in production-like conditions.

## 18. Known limitations

- No cross-group / cross-run bulk retry  
- No automatic or scheduled retry policies  
- No select-all-matching-server-filter beyond visible page (visible select-all only)  
- Acknowledgement is not a BDS-6 activity event (state on item / detail first)  
- Operation blocked-work remains inspection-only by design  

## 19. Track 6 closure

Track 6 is **complete** with JOB-1: itemization (SDK-3), per-item retry (SDK-4), bulk/selective retry, acknowledgement, and final blocked-work decision.

## 20. Next roadmap handoff

Recommended next epic: **SDK-7 — Cross-integration shared-key contract** (per post-UIX reentry), or Track 2 profile/preferences / deferred UIX panel when criteria met. Do not reopen Track 6 for Explorer or drawer work.
