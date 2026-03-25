# Epic 67 — Schedule Diagnostics and Evidence Timeline

## Status

Accepted — implemented.

## Context

Epics 64–66 established a defensible “schedule-truth model” for expected recurring execution:
- Epic 64 corrected satisfaction to use **authorized schedule-origin runs** (plus legacy correlation-prefix fallback for historical reads).
- Epic 65 centralized cron/timezone “was a fire due?” reasoning in `ScheduleExpectationEvaluator` and made the result explicit via `ScheduleExpectationEvaluationStatus`.
- Epic 66 added persisted schedule lifecycle evidence (enable/disable/material-change timestamps) so grace judgments are explainable from durable facts.

Operators still lacked an inspection surface to audit *why* the platform concluded an integration is `Satisfied`, `MissingExpectedRun`, `Grace`, or `NoExpectation`. Without an auditable detail view, the system may be correct but is not verifiable by the human running it.

## Decision

### 1. Operator-facing diagnostics reuses evaluator truth

Introduce a dedicated schedule diagnostics surface that is grounded in the same evaluator inputs and semantics used for fleet/readiness projections:

- `IntegrationScheduleDiagnosticsService` computes current status by calling `ScheduleExpectationEvaluator.Evaluate(...)`.
- Diagnostics never re-implements schedule expectation semantics in UI or ad hoc query code.
- Occurrence generation (cron + timezone) is centralized in `ScheduleExpectedFireCollector` and used by both:
  - `ScheduleExpectationEvaluator`
  - Epic 67 diagnostics timeline construction

This avoids semantic drift between “fleet silence” and the “evidence timeline” operators inspect.

### 2. DTO/API contract: bounded expected-fire evidence list

Add a management API endpoint:

- `GET /api/integrations/{id}/schedule-diagnostics`

and a client contract:
- `IntegrationScheduleDiagnosticsDto`
- `ExpectedFireDiagnosticDto`

The diagnostics payload contains:
- Current evaluation status and grace explanation tokens (`EvaluationStatus`, `InGrace`, `GraceReason`, `GraceAnchorUtc`).
- Core evidence timestamps (most recent expected fire; lifecycle timestamps from persisted schedule evidence).
- A bounded recent expected-fire timeline (`RecentExpectedFires`) with per-instant outcomes:
  - `Satisfied`
  - `Missing`
  - `GraceSuppressed`

### 3. Bounded timeline model

For the “recent expected fires” inspector, the evidence timeline is bounded to keep diagnostics practical:

- Collect expected fire instants using the shared cron/timezone logic.
- Search window uses a max lookback (`TimelineSearchMaxLookback = 30 days`).
- Timeline output is capped to `Last 10` expected fire instants (newest first).

For each expected fire instant in the list:
- `Satisfied` if there exists an authorized schedule-origin run at/after the expected fire.
- `GraceSuppressed` if the evaluator is currently in grace and the expected fire is at/after the evaluator’s grace anchor, with `GraceReason` carried into the item.
- `Missing` otherwise.

### 4. Schedule-origin run evidence stays separate from “any activity”

Diagnostics preserves Epic 64 boundaries:
- Schedule-origin authorized runs satisfy schedule expectations.
- “Last authorized run of any kind” is provided for contrast only and does not affect satisfaction outcome.

To support the timeline without violating semantics, `IRunQueryService` is extended with:
- `GetAuthorizedScheduleOriginRunsAsync(...)` returning a bounded list of authorized schedule-origin runs within a `sinceUtc` window.

### 5. UI drill-down from fleet

Add a Blazor page:
- `Pages/Integrations/IntegrationScheduleDiagnostics.razor`

and a fleet drill-down link:
- “Schedule diagnostics” from `IntegrationsList.razor` into `/integrations/{id}/schedule-diagnostics`.

The UI shows:
- Current evaluation status and grace context.
- Lifecycle evidence timestamps that explain the active grace judgment.
- The bounded “recent expected fires” evidence timeline and satisfying-run links when applicable.

## Non-goals

- Replacing the evaluator with a diagnostics-specific “parallel truth model”.
- Unbounded event exploration, historical analytics, or deep scheduler internals.
- Redefining Epic 62 last-run/failure semantics.
- Redesigning satisfaction rules; timeline outcomes remain consistent with existing evaluator semantics.

## Limitations / Future Extensions

- The per-instant satisfaction attachment in v1 relies on the availability of authorized schedule-origin run evidence within the chosen bounded lookup window.
- Future iterations could enrich the timeline with more explicit “which schedule(s) contributed” provenance once the product defines stable UX needs.

## Consequences

- Operators can validate “correct silence” by seeing:
  - the evaluator’s status
  - the evidence anchor and grace reason
  - the bounded expected-fire instants and their outcomes
- Diagnostics is explainable from durable persisted lifecycle evidence (Epic 66) and does not drift from evaluator semantics (shared collector + shared evaluator inputs).

