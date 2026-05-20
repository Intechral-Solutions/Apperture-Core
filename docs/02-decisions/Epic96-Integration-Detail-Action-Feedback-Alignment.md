# Epic 96 — Integration Detail Action Feedback Alignment

## Status

Accepted — May 2026.

## Purpose

[Epic 80](Epic80-Integration-Detail-Redesign-Pilot.md) established Integration Detail as the layered entity hub. [Epics 87–90](Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md) introduced **`ActionFeedback`** for cooperative/action results on Run, Schedule, Blocked Work, and Module detail. Integration Detail remained **toast-heavy** for mutations.

This epic aligns the hub with the same **page-local persistent banner** pattern without changing handlers, APIs, permissions, validation, wizard behavior, or [Epic 94/95](Epic94-Entity-Local-Recent-Activity-Panels.md) recent activity.

## Action inventory

| Action / mutation | Previous feedback | New feedback | Toasts | Reload / persistence |
|-------------------|-------------------|--------------|--------|----------------------|
| **Activate** | Success/error toast | `ActionFeedback` Info/Error | Preserved | `LoadAsync(preserveActionFeedback: true)` |
| **Deactivate** | Toast | `ActionFeedback` | Preserved | Preserve on reload |
| **Set state** (readiness remediation) | Toast | `ActionFeedback` | Preserved | Preserve on reload |
| **Save settings** | Toast | `ActionFeedback` | Preserved | Preserve on reload |
| **Create binding** | Toast; conflict → `_bindingError` `PageMessage` Warning in bindings section | Success → `ActionFeedback`; errors unchanged in section | Preserved | Preserve on success reload |
| **Remove binding** | Toast | `ActionFeedback` | Preserved | Preserve on reload |
| **In-flow connection create** (eligible) | Toast | `ActionFeedback` | Preserved | No full page reload |
| **In-flow connection create** (not eligible) | `_inFlowCreateError` section | Unchanged (section) | N/A | N/A |
| **Create schedule** | Toast | `ActionFeedback` | Preserved | Preserve on reload |
| **Schedule validation failure** | Error toast | `ActionFeedback` Error | Preserved | No reload |
| **Procedure launch success** | Toast + navigate to run | Unchanged (leaves page) | Preserved | N/A |
| **Procedure launch failure** | Error toast | `ActionFeedback` Error | Preserved | Stays on page |
| **Select procedure** (load error) | `_procedureError` → `ErrorState` in Procedures section | Unchanged | N/A | N/A |
| **Delete integration** | Toast; success navigates to list | Unchanged | Preserved | N/A |
| **Delete failure** | Error toast | `ActionFeedback` Error | Preserved | Stays on page |
| **Start add binding / slot load error** | Error toast | Unchanged (deferred) | Preserved | N/A |
| **Eligible connections load error** | Error toast | Unchanged (deferred) | Preserved | N/A |
| **Readiness “Fix now”** | Navigation or delegated handlers above | Via `SetStateAsync` / binding flow | N/A | N/A |
| **Wizard step navigation** | None | Unchanged | N/A | N/A |
| **Toolbar Refresh** | Full reload | Clears `ActionFeedback` | N/A | `LoadAsync()` default |
| **Initial page load** | N/A | No stale banner | N/A | Clears feedback |
| **Static inactive warning** | `PageMessage` Warning | Unchanged | N/A | N/A |
| **Notification / execute links** | Navigation | Unchanged | N/A | N/A |

## Changes implemented

### `IntegrationDetail.razor`

- State: `_actionMessage`, `_actionFeedbackKind` (`PageMessageKind`, default Info).
- Helpers: `SetActionFeedback(message, kind)`, `RefreshAsync()` → `LoadAsync()`.
- `LoadAsync(bool preserveActionFeedback = false)` — clears banner unless preserving after mutation reload.
- **Placement:** `ActionFeedback` immediately after `IntegrationSummaryBand` (visible on loaded integration).
- Post-mutation reloads use `preserveActionFeedback: true` (Epic 90 pattern).

### Component surface

- No changes to `ActionFeedback.razor`.

## Behavior preservation

- All API calls, permission checks, confirm dialogs, validation, wizard steps, procedure launch navigation, recent activity load, and readiness semantics unchanged.
- **Toasts retained** for all actions that already showed toasts (additive banner, not replacement).
- Section-scoped feedback unchanged: `_bindingError`, `_inFlowCreateError`, `_procedureError`, readiness `PageMessage` warnings.
- Binding **403/Conflict** remain section `PageMessage` Warning + toast (not promoted to page-level Error).

## Explicit deferrals

- Global operation history / operation registry
- App-wide toast normalization
- ActionFeedback on other entity pages (connections, governance, etc.)
- Visual polish / density pass
- Removing duplicate toast+banner (would change perceived feedback unless product approves)
- Delete success banner (user navigates away immediately)
- Procedure launch success banner (navigates to run detail)

## Validation

- `dotnet build` `Apperture.Management.Ui.Client`
- `Epic96IntegrationActionFeedbackTests` (static adoption checks)
- `Epic94` / `Epic95` tests (regression)
- `ManagementUiSemanticsTests` — not required (semantics unchanged)

### Manual QA checklist

1. Activate / deactivate — banner + toast; banner survives reload; Refresh clears banner.
2. Save settings — same.
3. Add / remove binding — success banner; conflict still in bindings section Warning.
4. Create schedule — banner on success; validation errors show Error banner + toast.
5. Procedure launch failure — Error banner + toast; success still navigates with toast only.
6. Recent activity section still loads after mutations.
7. No double-noise regression beyond existing toast+banner pairing.

## Follow-on

- Consider toast-only-on-navigation actions if product wants to reduce duplication on stay-on-page mutations only.
- Extend ActionFeedback to Connection Detail / Schedule create flows when those pages are prioritized.
