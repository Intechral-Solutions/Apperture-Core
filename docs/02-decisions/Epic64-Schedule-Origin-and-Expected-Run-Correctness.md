# Epic 64 — Schedule-Origin Truth and Expected-Run Correctness

## Status

Accepted — implemented.

## Context

Epic 63 introduced **expected-run / silent** fleet attention using:

- recurring schedule expectation (active, runnable, not config-blocked, enabled integration schedule with non-empty recurrence), and  
- “no **authorized** run in the Epic 62 window” as silence.

That conflated **any** authorized execution (including manual or module procedure launches) with **schedule-fired** execution. A manual run could hide missing scheduled activity.

## Decision

### 1. Persist execution origin on runs

Add nullable `OperationRuns.ExecutionOrigin` with stable string values from `RunExecutionOriginNames`:

- `schedule` — control-plane dispatch from the schedule runner (`ScheduleDispatchRunner`).
- `manual` — operator/API workflow or operation start, module procedure launch, or workflow retry (inherits parent run’s origin when present).

Dispatch services set origin from `StartWorkflowRequest` / `StartOperationRequest`. Retries copy the original run’s `ExecutionOrigin`.

Legacy rows have `ExecutionOrigin` null. For fleet **schedule-origin** evidence only, the query also treats runs as schedule-like when origin is null **and** `CorrelationKey` starts with `schedule:` (the runner’s convention since before this column). This is a bounded, honest compatibility shim—not a general inference engine.

### 2. Expected-run silence uses schedule-origin evidence only

`HasUnexpectedSilence` is true when Epic 63 eligibility holds **and** there is **no** authorized **schedule-origin** run in the same lookback window (per integration), not merely “no authorized run of any kind.”

Operational failure attention (Epic 62) is unchanged: it still uses all authorized runs in the batch.

### 3. Per-integration latest schedule-origin lookup

`IRunQueryService.GetLatestAuthorizedScheduleOriginRunsAsync` loads, per integration id, the **latest** candidate run (by `CreatedAt`) that matches schedule-origin (explicit column or legacy correlation prefix), then applies the **same** per-run read authorization as `ListRunsAsync`. This avoids relying on a single global row cap to infer the correct “latest schedule run” per integration.

A hard cap on **candidate rows** before grouping remains possible if needed; v1 uses `GroupBy` + `OrderByDescending` in EF for correctness over unbounded in-memory scans.

### 4. API / fleet projection

`IntegrationFleetExpectedRunAttention` exposes optional `LastScheduleOriginAuthorizedRunId` / `LastScheduleOriginAuthorizedRunAt` for triage (e.g. link to `/runs/{id}`). `RunSummaryDto` exposes `ExecutionOrigin` for list surfaces.

## Non-goals

- Cron next-fire / missed-fire engine  
- Full origin taxonomy (subtypes of manual, cross-tenant analytics)  
- Health scores or streaming dashboards  
- Backfilling `ExecutionOrigin` for all historical rows (legacy correlation rule is sufficient for typical scheduler-started runs)

## Consequences

- Silent-attention is **schedule-expectation-based**; manual/procedure activity no longer clears it incorrectly.  
- Operators need brief UI copy that “expected run missing” refers to **scheduled** execution in the window.  
- New runs should always set `ExecutionOrigin` at dispatch; legacy + `schedule:` correlation remains valid for older data.

## Follow-ups

1. Optional SQL migration to set `ExecutionOrigin = 'schedule'` where `CorrelationKey` matches scheduler prefix and origin is null.  
2. Narrow “manual” further (e.g. API vs procedure) only if product needs distinct fleet signals.  
3. Shared cron evaluator for true missed-fire detection when a small evaluator exists.
