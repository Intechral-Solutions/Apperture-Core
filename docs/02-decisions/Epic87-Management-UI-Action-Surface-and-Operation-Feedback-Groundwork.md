# Epic 87 — Management UI action surface and operation feedback groundwork

## Status

Accepted — implemented May 2026.

## Purpose

After Epics 80–86 (pilot redesigns, design-system consolidation, and state-branch consistency), inventory **how operators trigger work** and **where results appear** across the Management UI. This epic documents action categories and feedback placement rules and applies **minimal** presentation alignment where existing primitives already match.

This is **groundwork** for a future async operation shell — not the shell itself.

Canonical context: [Epic 78](Epic78-Management-UI-Modernization-Groundwork.md), [management UI design system](../01-architecture/management-ui-design-system.md), [Epic 79](Epic79-Management-UI-Design-System-Foundation.md), [Epic 85](Epic85-Management-UI-Pilot-Consolidation.md), [Epic 86](Epic86-Management-UI-State-Branch-Consistency-Sweep.md).

## Pilots reviewed

| Pilot | Route / component | Action focus |
|-------|-------------------|--------------|
| Integration Detail | `/integrations/{IntegrationId}` | Toolbar lifecycle, procedure launch, bindings, schedules, wizard; toasts + section warnings |
| Run Detail | `/runs/{RunId}` | Cooperative execution controls; `_actionMessage` + poll |
| Control Center | `/control/center` | Refresh; summary navigation cards (no mutating commands) |
| Schedules list/detail | `/schedules`, `/schedules/{ScheduleId}` | Enable/disable/trigger; edit/save; drill-ins |
| Notification definition cluster | `/config/notifications/definitions/{DefinitionId}`, edit/create | Authoring save/validate/preview; subscriptions; post-create prompt |
| Schedule create | `/schedules/new` | Form submit (layout pass from Epic 83) |

**Representative non-pilot pages inspected:** `ConnectionDetail.razor`, `ConnectionNew.razor`, `BlockedWork.razor`, `Governance/UserDetail.razor`, `Governance/RoleDetail.razor`, `Config/Auth/SignInExperience.razor`, `Config/Auth/EntraTenants.razor`, `IntegrationsList.razor`, `ManagedContactEditor.razor`.

## Action categories

### Navigation action

Moves the operator to another page or evidence surface. No operation feedback required.

**Examples:** Integration Detail → Execute, Notifications, Back; Control Center summary cards; Schedule Detail `DrillInLink` to runs/audit/blocked work; notification definition summary cards.

### Local UI action

Changes view state only (expand, edit mode, wizard step, checkbox).

**Examples:** Schedule Detail Edit toggle; Integration Detail wizard step selection; Run Detail Poll checkbox; notification authoring advanced JSON toggles.

### Form submit/save action

Persists operator-entered configuration.

**Examples:** `NotificationDefinitionAuthoringForm` Save/Create; Sign-in policy save; Entra tenant create/edit; Schedule Detail save in edit mode; Managed contact create/save.

**Feedback:** Validation stays in-form; save failures use **`PageMessage` Error** adjacent to actions (or validation list in `ErrorState` ChildContent for schedule server validation).

### Immediate command

API call with quick, discrete outcome.

**Examples:** Connection test/create (`ActionButton` with Active/Success/Error); governance role assign (toast); Entra validate OIDC (toast).

### Async/cooperative command

Triggers work that may outlive the click or depends on eligibility/state.

**Examples:** Run cancel/retry/park/resume; schedule enable/disable/trigger run cycle; blocked-work retry and run-cycle trigger; integration activate/deactivate/procedure launch/binding changes.

**Feedback today:** Run/Schedule/Blocked Work use **`PageMessage` Info** with Title “Action” and domain-specific `_actionMessage` text; Integration Detail predominantly uses **toasts**; Run Detail adds **new-run `DrillInLink`** on retry success.

### Destructive command

Delete, retire, revoke, or replace-with-loss operations.

**Examples:** Integration Delete (`ConfirmDeleteAsync`); Connection Retire; role permission replace (inline warning before save).

**Rule:** Preserve confirmation and permission behavior; keep warning copy visible before commit.

### Refresh/reload action

Reloads page or section data.

**Examples:** Standard `PageHeader` Refresh on pilots and list pages.

### Remediation / action-needed link

Guides operator to fix a blocker.

**Examples:** Integration readiness remediation links; notification “no deliverable recipients” attention; wizard “next action” guidance.

### Diagnostic drill-in action

Opens related evidence without mutating state.

**Examples:** Audit timeline filters; schedule diagnostics; notification diagnostics routes; run forensics sections.

## Feedback placement rules

| Situation | Use | Avoid |
|-----------|-----|-------|
| Field/form validation | Inline under fields; schedule validation list in `ErrorState` child | Whole-page error for single-field issues |
| Form save failure | `PageMessage` Error near submit | Toast-only on long forms without inline copy |
| Cooperative command result | Page-level `PageMessage` Info “Action” (Run, Schedule, Blocked Work) | Replacing Run Detail semantics with toasts |
| Dense hub mutations | Toast (`IntegrationDetail`, governance) | Hiding errors only in advanced sections |
| Section-scoped binding/create errors | `PageMessage` Warning in section | Page-level error that removes section context |
| Initial load failure | Epic 86 state wrappers | Confusing load failure with action failure |
| Future operation shell | TBD dedicated surface | Building shell components in this epic |

## Low-risk changes made

| File | Change |
|------|--------|
| `Organisms/Notifications/NotificationDefinitionAuthoringForm.razor` | Save/action `_error` → `PageMessage` Error (matches sign-in/Entra form pattern) |
| `Pages/Config/Contacts/ManagedContactEditor.razor` | Create/save `_error` → `PageMessage` Error |
| `docs/01-architecture/management-ui-design-system.md` | Added **Action surface and feedback (Epic 87)** section |
| `docs/02-decisions/Epic87-…md` | This document |
| `.ai/memory/decisions-index.md` | Epic 87 index entry |

No handler, confirmation, validation, route, or API changes.

## Deferred work

Explicitly **not** in Epic 87:

- Full **async operation shell / history** UI
- **Operation registry** or procedure abstraction layer
- **Schema-driven forms** beyond existing `ProcedureParameterForm` / notification authoring
- **Dashboard action composition** or module dashboards
- **Backend operation tracking** APIs
- Converting Run Detail toolbar to `ActionButton`
- Normalizing Integration Detail toast-only feedback to page banners
- Secondary muted `_error` lines at bottom of loaded pages (`IntegrationNotifications`, `NotificationDiagnostics`, `NotificationInstanceDetail`) — document-only until feedback contract is clarified

## Behavior preservation

Routes, action handlers, permission checks, confirmation dialogs, form validation, API calls, polling, and navigation targets were unchanged.

## Validation

Recorded at implementation time:

- `dotnet build` on `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
- `dotnet test` filtered with `FullyQualifiedName~ManagementUiSemanticsTests` (no shared semantics code changed; run for regression)

## Recommended next epic

**Async operation shell (tentative Epic 88+):** Start with Run Detail, Schedule Detail, and Blocked Work cooperative commands; adopt a shared operation banner/history model without breaking `_actionMessage` eligibility semantics or 403/409 copy.

**Implemented:** [Epic 88 — Cooperative action feedback shell pilot](Epic88-Cooperative-Action-Feedback-Shell-Pilot.md) (`ActionFeedback` on Run Detail and Schedule Detail; extended to Blocked Work and Module Detail in [Epic 90](Epic90-Management-UI-Action-Feedback-Corrective-Pass.md)). Operation history planning: [Epic 91](Epic91-Operation-History-and-Action-Feedback-Groundwork.md).
