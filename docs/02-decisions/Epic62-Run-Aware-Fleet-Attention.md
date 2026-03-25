# Epic 62: Run-Aware Fleet Attention and Operational Triage

Status: Accepted  
Date: 2026-03-25

## Context

Epic 61 added a fleet overview grounded in **operational state**, **readiness** (blocking vs advisory), **connection verification** issue codes, and **attention tokens**. That answers “is this integration configured and allowed to run?” but not “is it **failing in execution** right now?”

Operators need **run-aware** signals so they can triage **recent failures** without opening every integration or guessing from configuration alone.

## Decision

1. Extend fleet rows with a **small, evidence-backed operational slice** derived only from persisted `OperationRuns` (same **read authorization** rules as `GET /api/runs`).
2. **v1 operational signals** (bounded **7-day** lookback, global **row cap** on the batch query):
   - Count of **authorized** runs in the window whose status is **failed** (lifecycle `failed`).
   - **Most recent failed** authorized run id and time (for deep link to run detail).
   - **Latest authorized** run in the captured sample: id, status, time — to expose **“most recent run failed”** honestly within the window/cap.
3. **Attention tokens** (additive, no composite score):
   - `operational_failure`: **Active** integration with at least one such failure in the window.
   - `repeated_operational_failure`: **Active** and failed count **≥ 2** in the window.
4. **Inactive** and **Retired** integrations **do not** receive operational attention tokens and are **not** included in the batch run query (no “failing because idle” or “no recent runs” as failure).
5. **Configuration/readiness** tokens (`blocking`, `advisory_only`, verification tokens, etc.) stay **separate**; they may appear **together** with operational tokens (e.g. runnable + advisory + recent failures).
6. **API**: extend `GET /api/integrations/fleet` with optional `hasOperationalFailure` filter and sort keys for triage; extend `GET /api/runs` with `integrationId` and optional `take` (cap before authorization filtering).
7. **UI**: fleet table shows operational column(s), filter, and links to `/runs/{id}` and `/runs?integrationId=…&createdFrom=…` aligned to the server window.

## Non-goals

- Full observability, alerting, or streaming dashboards.
- Historical analytics, reporting warehouse, or arbitrary aggregation framework.
- Synthetic health scores or rolled-up “fleet health %”.
- Inferring failure from absence of runs (no schedule-aware “expected run” in v1).

## Honesty rules

| Situation | Behavior |
|-----------|----------|
| Inactive / Retired | No operational tokens; no run batch for that row. |
| Active, hard-blocked on readiness | Blocking/readiness signals unchanged; operational signals only from **real** failed runs in window. |
| Active, advisory-only | Advisory + operational tokens if failures exist. |
| Active, no failed runs in window | Operational counts zero; no operational tokens. |

## Follow-ups

- Per-integration or SQL window functions if batch caps skew “latest run” for very busy integrations.
- Schedule-aware expectations (“no run when none expected”).
- Pagination on `/fleet` and larger run windows behind explicit operator control.
