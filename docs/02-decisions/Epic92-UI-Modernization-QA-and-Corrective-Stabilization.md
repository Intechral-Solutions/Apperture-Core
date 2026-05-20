# Epic 92 — UI Modernization QA and Corrective Stabilization

## Status

Accepted — implemented May 2026.

> **Note:** Epic **91** is [Operation History and Action Feedback Groundwork](Epic91-Operation-History-and-Action-Feedback-Groundwork.md). This QA/stabilization pass is **Epic 92** to preserve sequential epic numbering.

## Purpose

Focused **manual-QA-oriented stabilization** over Epics **78–90** modernization work, especially [Epic 90](Epic90-Management-UI-Action-Feedback-Corrective-Pass.md) action feedback. Fix **concrete** state-branch and persistence regressions found by static review — not visual polish, not new features, not new frameworks.

## QA areas reviewed

### Pilot surfaces (static review)

| Route | Page | Result |
|-------|------|--------|
| `/integrations/{IntegrationId}` | `IntegrationDetail.razor` | **Fixed** not-found vs loading |
| `/runs/{RunId}` | `RunDetail.razor` | OK — 404 sets `_error`; ActionFeedback + preserve pattern verified |
| `/control/center` | `ControlCenter.razor` | OK — state branches consistent |
| `/schedules` | `SchedulesList.razor` | OK |
| `/schedules/{ScheduleId}` | `ScheduleDetail.razor` | **Fixed** not-found vs loading; action feedback branch retained |
| `/schedules/new` | `ScheduleCreate.razor` | **Fixed** forbidden flash before capabilities load |
| `/config/notifications/definitions/{DefinitionId}` | `NotificationDefinitionDetail.razor` | **Fixed** load error vs empty; action errors; stale `_error`; param reload |
| `/config/notifications/definitions/{DefinitionId}/edit` | `NotificationDefinitionEdit.razor` | OK |
| `/config/notifications/definitions/new` | `NotificationDefinitionCreate.razor` | OK — `_capabilitiesLoaded` pattern already present |

### Epic 90 action feedback surfaces

| Page | Persistence | Layout | Result |
|------|-------------|--------|--------|
| Run Detail | `preserveActionFeedback: true` on post-action reload; Refresh clears | Banner above detail | OK |
| Schedule Detail | Same | Banner above detail | OK |
| Blocked Work | Same + new-run link | Banner + table | OK |
| Module Detail | Same | Banner + module state | **Fixed** not-found vs loading |
| NotificationDiagnostics / InstanceDetail / IntegrationNotifications | N/A | `PageMessage` Error (Epic 90) | OK; instance load error branch **fixed** |

## Issues fixed

| ID | Severity | Issue | Fix |
|----|----------|-------|-----|
| **STAB-001** | High | `NotificationDefinitionDetail` — load exception showed `EmptyState` instead of error | Error branch before empty; clear `_detail` on reload |
| **STAB-002** | High | `NotificationInstanceDetail` — load failure showed `EmptyState`; error banner unreachable | Top-level `ErrorState` when `_error` set |
| **STAB-003** | Medium | `NotificationDefinitionDetail` — post-load action failure used top `ErrorState` while full page remained visible | Section `PageMessage` Error; clear stale `_error` on mutating handlers |
| **STAB-004** | Medium | `NotificationDefinitionDetail` / `NotificationInstanceDetail` — no `OnParametersSetAsync`; stale data on param change | `_dataLoadKey` + reload pattern (Run/Schedule detail) |
| **STAB-005** | Medium | `IntegrationDetail` — null integration showed perpetual `LoadingState` | `_loading` branch then `EmptyState` |
| **STAB-006** | Medium | `ScheduleDetail` — null schedule showed perpetual `LoadingState` | `_loading` vs `EmptyState` when `_detail` null |
| **STAB-007** | Medium | `ModuleDetail` — null module showed perpetual `LoadingState` | `_loading` vs `EmptyState` |
| **STAB-008** | Medium | `ScheduleCreate` — `ForbiddenState` flashed before capabilities loaded | `_capabilitiesLoaded` gate (NotificationDefinitionCreate pattern) |

## Deferred (documented, not fixed)

| ID | Issue | Rationale |
|----|-------|-----------|
| **DEF-STAB-001** | Run Detail **polling** clears `ActionFeedback` every ~2s | Epic 90 explicitly preserved poll behavior; changing requires product judgment on poll vs banner coexistence |
| **DEF-STAB-002** | Integration Detail toast-only mutation feedback | ADV-003 — intentional hub pattern; not a regression |
| **DEF-STAB-003** | Cooperative 403/409 shown as Info `ActionFeedback` | ADV-002 — copy/severity review epic |
| **DEF-STAB-004** | Operation history / entity-local activity panels | Epic 91 groundwork → Epic 93+ implementation |

## Behavior preservation

- Routes, API calls, permission gates, action handlers unchanged
- Epic 90 `preserveActionFeedback` / `RefreshAsync` patterns unchanged
- Polling interval and poll-triggered reload semantics unchanged (DEF-STAB-001)
- Domain copy unchanged except new empty-state titles for not-found cases

## Validation

Recorded at implementation time:

- `dotnet build` on `Apperture.Management.Ui.Client`
- `ManagementUiSemanticsTests` — 23 passed
- `Epic92UiStabilizationTests` — 5 passed (new static guards)

## Manual QA checklist (browser)

### Action feedback (Epic 90)

- [ ] Run Detail: cancel/retry/park/resume — banner persists after reload; retry shows new-run link; Refresh clears banner
- [ ] Schedule Detail: enable/disable/trigger — banner persists; detail visible; Refresh clears
- [ ] Blocked Work: retry — banner + table; new-run link when applicable; window change clears banner
- [ ] Module Detail: lifecycle action — banner + state visible; Refresh clears

### State branches (Epic 92)

- [ ] Invalid schedule id → “Schedule not found” empty state (not infinite loading)
- [ ] Invalid integration id → “Integration not found”
- [ ] Invalid module id → “Module not found”
- [ ] Notification definition load failure → error state (not “not found”)
- [ ] Notification instance load failure → error state
- [ ] Schedule create — no forbidden flash on first paint (brief loading OK)
- [ ] Navigate between two notification definitions — detail updates (no stale content)

### Pilots (smoke)

- [ ] Integration Detail — summary band + sections render when integration exists
- [ ] Control Center — cards load or show appropriate empty/forbidden
- [ ] Notification definition detail — toggle subscription after error clears prior error banner

## Recommended next epic

[Epic 91](Epic91-Operation-History-and-Action-Feedback-Groundwork.md) recommended **entity-local recent activity panel** (audit/timeline-backed) as Epic 93 tentative — after manual QA above passes.
