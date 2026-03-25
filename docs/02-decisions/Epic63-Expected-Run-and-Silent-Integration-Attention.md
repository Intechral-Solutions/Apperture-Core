# Epic 63: Expected Run and Silent Integration Attention

Status: Accepted  
Date: 2026-03-25

## Context

Epic 62 surfaces **recent failed runs** per integration. Operators still cannot see integrations that **should execute on a schedule** but show **no authorized activity** in the evidence window—silence is different from failure and from being paused or blocked.

## Decision

1. Add **expected-run / silent attention** only when the platform has a **truthful expectation** of recurring execution:
   - **Active** integration.
   - **Runnable** (`IsRunnable`) and **no config blocking readiness** (same notion as `HasConfigBlockingReadiness`: excludes `integration_not_active` but includes other blockers).
   - At least one **enabled** `ScheduleDefinition` with `IntegrationId` matching the integration and **non-empty** `RecurrenceExpression` (v1 proxy for “recurring schedule attached”; **no** cron next-fire or SLA math).

2. **Unexpected silence** when, in addition to the above:
   - **Grace** elapsed: integration **CreatedAt** is older than **`IntegrationFleetExpectedRunEvidence.SilenceGraceAfterIntegrationCreate`** (48h) from evaluation time.
   - **No authorized run** observed in the **same** lookback window as Epic 62 (`LastAuthorizedRunAt == null` on the fleet operational slice), meaning no run the operator could read appeared in the batch for that integration in the window.

3. **Stable token**: `expected_run_missing` when `HasUnexpectedSilence` is true.

4. **Not silent** when **any** authorized run exists in the window (`LastAuthorizedRunAt != null`), including failed runs—activity is visible via Epic 62.

5. **Inactive / Retired / hard-blocked / no qualifying schedules**: no silent attention; explanatory states only.

6. **Triage**: fleet row exposes `referenceScheduleId` (first matching schedule by id order) for `/schedules/{id}`; integration detail `/integrations/{id}#schedules` for context.

## Limitations (honest)

- Silence uses the **same** Epic 62 batch cap and per-run authorization: a **false “silent”** is possible if recent runs exist but fall outside the global row cap or are unreadable to the actor.
- Recurrence expression is **not** evaluated; “expects runs” means “has a recurring-style schedule record,” not “cron says a fire was missed.”

## Non-goals

- SLA / alerting engine, composite health scores, deep analytics.
- Full schedule simulation or next-fire computation in v1.

## Follow-ups

1. Per-integration or aggregated SQL for “latest authorized run in window” to reduce cap-related false silence.
2. Shared cron/next-fire helper when available.
3. Optional: silence only when **schedule-origin** runs are missing, if correlation can be grounded reliably.
