# S6 — Runs & Schedules Operator Triage

Epic S6 applies the operator-first, summary-first pattern from S3–S5 to `/runs` and `/schedules`.

## Conventions

### Summary first, diagnostics second

Primary panels answer:

- **Runs list:** outcome/status, execution context, recency, links to integration/schedule/run detail.
- **Run detail:** what ran, status/outcome, attention reason, integration/procedure context, timestamps, summary cards for drill-in.
- **Schedules list:** name, enabled state, target summary, integration link, runs/audit links.
- **Schedule detail:** enabled/target/outcome pills, health summary, last attempt/dispatch, summary cards.

Technical identifiers, cron internals, forensic investigation, action eligibility matrices, correlation keys, and start-request payloads remain available behind `ManagementAdvancedSection`, `<details>`, or collapsed panels.

### Attention sorting

`RunScheduleOperatorUiHelpers` ranks runs and schedules for triage:

- Failed/rejected/parked runs surface before completed runs.
- Disabled schedules and schedules missing integration links rank for review on the list view.
- Schedule detail ranks stale claims and failed last outcomes highest.

List pages expose a **Needs attention** filter backed by these helpers (client-side on loaded rows).

### Links

Use existing routes only:

- `/integrations/{id}`
- `/runs/{id}` and `/runs?integrationId=…`, `?correlationKey=schedule:{id}…`
- `/schedules/{id}`
- `/control/center`, `/control/blocked-work`, `/control/audit?…`

No placeholder destinations.

### Empty states

- No runs / no schedules yet — setup guidance with links to related surfaces.
- No filter matches — clear filters action.
- Needs-attention filter with no matches — show-all action.

## Deferred

- **Calendar / date-periods** — not implemented; recurrence shown as stored cron expression.
- **Next-fire on schedule list DTO** — detail page only; list notes the gap.
- **Degraded-only schedule list API filter** — deferred until API supports it.

## Tests

`tests/Apperture.Management.Ui.Shared.Tests/S6RunScheduleOperatorTriageTests.cs` covers helper ranking/summary logic and static page conventions (Blazor route rendering not available in CI).
