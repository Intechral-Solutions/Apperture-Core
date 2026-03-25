# Epic 65 — Schedule Expectation Evaluator and Missed-Fire Truth

## Status

Accepted — implemented.

## Context

Epic 63 introduced **recurring-run expectation** and **unexpected silence** attention. Epic 64 corrected satisfaction to use **authorized schedule-origin runs** only (plus legacy `CorrelationKey` prefix when `ExecutionOrigin` is null).

Remaining gap: silence was still inferred from **“no schedule-origin run in a fixed lookback window”**, not from **whether the persisted schedule actually owed a fire** in that period. Operators could see silence when the cron produced no due occurrence, or miss nuance when a schedule was disabled or an integration was in grace.

## Decision

### 1. Shared schedule expectation evaluator

Introduce `IScheduleExpectationEvaluator` / `ScheduleExpectationEvaluator` in `Apperture.Core.Application.Scheduling`, returning `ScheduleExpectationEvaluation` with explicit status:

- `NoExpectation` — not eligible for recurring expectation, no qualifying enabled schedule, no cron occurrence in `(windowStart, utcNow]`, or inactive integration.  
- `Grace` — within `IntegrationFleetExpectedRunEvidence.SilenceGraceAfterIntegrationCreate` after integration creation (Epic 63 semantics).  
- `Satisfied` — at least one expected fire in the window and the latest **authorized schedule-origin** run is at or after the **most recent** expected fire instant.  
- `MissingExpectedRun` — expected fire(s) in the window but no qualifying schedule-origin run at/after that most recent fire.

Cron occurrences use **Cronos** and **schedule time zone**, consistent with `CronScheduleEvaluator` in persistence. Qualifying schedules match the fleet runner: enabled, bound to the integration, non-empty recurrence, `WorkflowExecution` or `ModuleOperationExecution` targets.

### 2. Fleet / readiness integration

`IntegrationReadinessService.ListFleetAsync` builds `ScheduleExpectationEvaluationContext` with:

- `WindowStartUtc` from operational evidence (`EvidenceWindowStartUtc`) or the same `utcNow - Lookback` used for run batch queries,  
- latest schedule-origin evidence from `IRunQueryService.GetLatestAuthorizedScheduleOriginRunsAsync`,  
- frozen time via injected `TimeProvider` (production: `TimeProvider.System`).

`IntegrationFleetAttention.FromScheduleExpectationEvaluation` maps into `IntegrationFleetExpectedRunAttention`, including evaluation status, last expected fire UTC, fire count in window, satisfied/grace flags, and a short `OperatorSummaryLine`.

`HasUnexpectedSilence` is true **only** when status is `MissingExpectedRun` (not for `NoExpectation` or `Grace`).

### 3. API and UI

`IntegrationFleetExpectedRunAttentionDto` gains explanation-oriented fields (`expectedRunEvaluationStatus`, `lastExpectedFireUtc`, `expectedFireCountInWindow`, `expectedRunSatisfied`, `expectedRunInGracePeriod`, `operatorSummaryLine`). The fleet table shows summary text, distinguishes “not due” vs OK vs missing scheduled run, and keeps links to last schedule-fired run and schedule.

### 4. Relationship to Epic 62 / 63 / 64

- **Epic 62** operational failure / last authorized run semantics are unchanged.  
- **Epic 63** grace duration and “expects recurring runs” eligibility (runnable, not config-blocked, qualifying schedule) remain; evaluation centralizes cron-based “was a fire due?”  
- **Epic 64** rule stands: only schedule-origin (or legacy correlation prefix) satisfies expectation; manual/procedure/API runs are invisible to satisfaction.

### 5. Data provenance and migrations

`ExecutionOrigin` continues to be set on new dispatch paths where applicable. Historical rows rely on **read-time** legacy fallback in `RunQueryService` unless a separate backfill is introduced. No schema change in this epic.

## Non-goals

- Generalized scheduling redesign, new user-editable schedule concepts, or finer manual-origin taxonomy.  
- Perfect accounting for every edge case of distributed scheduling; v1 prioritizes **honest, explainable** conclusions over exhaustive scheduler semantics.  
- Replacing Epic 62 “last run” operational views with schedule-only semantics.

## Future extensions

- Grace tied to schedule **enable/edit** events (not only integration create), if product defines stable persisted signals.  
- Dedicated schedule diagnostics UI built on the same evaluator inputs.  
- Optional SQL backfill to normalize `ExecutionOrigin` for historical schedule-dispatched rows, reducing reliance on correlation-prefix fallback.

## Consequences

- Silence attention is **defensible**: it implies a missed expected fire, not merely empty window activity.  
- Deterministic unit tests use a fixed `TimeProvider` instead of depending on wall-clock alignment with cron ticks.
