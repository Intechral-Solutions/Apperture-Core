# S7 — Procedure Execute & Run Launch UX

Epic S7 improves operator-facing procedure/workflow/operation launch without building the reusable wizard framework.

## Conventions

### Summary first, diagnostics second

Primary launch UI (`/execute` and Integration Detail procedures) shows:

- Integration and target context (name, description, launch kind)
- Readiness/runnable status and blocked reasons in operator language
- Parameter form with labels, help text, and required markers
- Launch button with disabled reason in `title` and helper text
- Success feedback with link to `/runs/{runId}`

Advanced sections hold idempotency key, correlation ID, renderer key, and raw target identifiers.

### Shared helpers

`ProcedureLaunchOperatorUiHelpers` (Ui.Shared, testable):

- `DescribeBlockedReason` — maps server blocked codes to actionable text
- `FormatLaunchError` — maps API error codes to operator messages
- `ValidateProcedureParameters` — client-side required-field check before submit
- `BuildLaunchDisabledReason` — single disabled-reason string for launch buttons
- `BuildExecuteHref` — deep links: `/execute?integrationId=&kind=&procedureId=`

### Post-launch behavior

- **Integration Detail:** success toast + navigate to run detail
- **Execute page:** success `ActionFeedback` + `DrillInLink` and explicit “Go to run detail” action

Unexpected failures (HTTP exceptions, dispatch errors) surface as error messages; structured procedure launch failures use `FormatLaunchError`.

### Empty/setup states

- No integrations configured
- No executables for selected integration/kind
- Integration not runnable (readiness warning + link to integration page)
- Procedure not launchable (blocked reason list)
- Missing required parameters (validation before server call)
- Permission/dispatch failures via mapped error text

## Entry points

| Route | Purpose |
|-------|---------|
| `/execute` | Unified launch (procedure default kind) |
| `/execute?integrationId=&kind=procedure&procedureId=` | Deep link from Integration Detail |
| Integration Detail → Procedures | Inline launch + “Open launch page” |

Legacy `/workflows/start` and `/operations/start` remain stubs redirecting to `/execute`.

## Deferred

- Reusable wizard framework
- Per-field server validation mapping for `invalid_parameters`
- Workflow/operation structured error bodies (still exception-based on client)
- Blazor route rendering tests in CI (static page convention tests used instead)

## Tests

`tests/Apperture.Management.Ui.Shared.Tests/S7ProcedureLaunchOperatorUxTests.cs`
