# Epic 88 — Cooperative action feedback shell pilot

## Status

Accepted — implemented May 2026.

## Purpose

Introduce a **small reusable presentation component** for cooperative/user-triggered action feedback, unifying the page-local `_actionMessage` banner pattern identified in [Epic 87](Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md).

This is a **UI presentation pilot only** — not backend operation tracking, not a global operation registry, and not operation history.

Canonical context: [Epic 81](Epic81-Run-Detail-Redesign-Pilot.md), [Epic 83](Epic83-Schedules-List-Detail-Redesign-Pilot.md), [management UI design system](../01-architecture/management-ui-design-system.md).

## Source patterns

### Run Detail (`RunDetail.razor`)

- Toolbar: cancel, retry, park, resume, poll, refresh (unchanged).
- After successful load, when `_actionMessage` is set:
  - `PageMessage` Info, Title **“Action”**, Message = domain copy.
  - On retry success: `ChildContent` with `DrillInLink` to new run (`_newRunId`).
- Failures (403, 409, HTTP, generic) also used **Info** kind with the same Title — copy preserved verbatim in `_actionMessage`.

### Schedule Detail (`ScheduleDetail.razor`)

- Toolbar: enable, disable, trigger scheduler cycle, refresh (unchanged).
- When `_actionMessage` is set (including after `ExecuteScheduleActionAsync` + reload):
  - `PageMessage` Info, Title **“Action”**, Message = success or exception message.
- Shown as a branch before loading/detail (unchanged control flow).

## Component introduced

**`ActionFeedback`** (`Molecules/ActionFeedback.razor`)

Thin wrapper over `PageMessage` with cooperative-action defaults.

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `Kind` | `PageMessageKind` | `Info` | Success/Warning/Error available for future pages; pilots keep Info for all outcomes |
| `Title` | `string` | `"Action"` | Matches prior banner title |
| `Message` | `string` | — | Page-local result copy |
| `ChildContent` | `RenderFragment?` | — | Drill-in links, optional detail |

**Usage rules**

- Use for **user-triggered cooperative command results** only.
- Do **not** use for load errors, empty/forbidden, form validation, static warnings, or toasts.
- Parent page retains `_actionMessage`, handlers, eligibility, and API calls.

## Pages updated

| Page | Change |
|------|--------|
| `Pages/Runs/RunDetail.razor` | `_actionMessage` banner → `<ActionFeedback>` with optional new-run `ChildContent` |
| `Pages/Schedules/ScheduleDetail.razor` | `_actionMessage` banner → `<ActionFeedback Message="@_actionMessage" />` |

## Behavior preservation

- Action handlers (`InvokeActionAsync`, `ExecuteScheduleActionAsync`, cancel/retry/park/resume, enable/disable/trigger) unchanged.
- Eligibility: `CanInvoke`, `ActionReason`, capability gates unchanged.
- 403/409/conflict and success copy unchanged (still assigned to `_actionMessage` in code).
- Polling, refresh, routes, permissions, and navigation unchanged.
- Schedule Detail branch order (error → action message → loading → detail) unchanged.

## Explicit deferrals

- Global async **operation shell** / **operation history** drawer
- **Operation registry** or operation IDs
- **Persistent operation tracking** / backend APIs
- **Toast normalization** (Integration Detail, governance, etc.)
- **Blocked Work** adoption (still `PageMessage` — equivalent but deferred to avoid scope creep)
- **Connection test** (`ActionButton` — different feedback model)
- Mapping Run/Schedule 403/409 to `Kind=Error` (would change visual semantics)

## Validation

Recorded at implementation time:

- `dotnet build` on `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
- `dotnet test` filtered with `FullyQualifiedName~ManagementUiSemanticsTests`

## Recommended next epic

**Operation history / blocked-work adoption:** Extend `ActionFeedback` to Blocked Work when ready; add history/persistence only when backend operation lifecycle contracts exist.

**Post–Epic 89 audit:** [Epic 89](Epic89-Management-UI-Modernization-Progress-Audit.md) found that `LoadAsync` clears `_actionMessage` after actions on Run/Schedule detail, so banners may not display until a corrective pass (COR-001) preserves messages across reload.
