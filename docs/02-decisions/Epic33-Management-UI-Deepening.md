# Epic 33 — Management UI Deepening

## Status

Implemented (Corrective pass complete)

## Context

Epics 26–32 established module lifecycle, runtime composition, scheduler, runtime health, recovery/remediation, and audit discoverability. Backend truth and APIs matured, but the management UI remained comparatively thin. Operators had to piece together context across separate endpoints and screens. This epic deepens the management UI so operators can inspect and navigate modules, schedules, runs, runtime health, recovery context, and audit history as one coherent experience.

## Decision

### New / Updated Views

| View | Route | Purpose |
|------|-------|---------|
| **Control Center** | `/control/center` | Extended with Platform Health section: overall status, degraded modules, blocked schedules, blocked work count, anomalies; links to modules, schedules, blocked-work, audit |
| **Blocked Work** | `/control/blocked-work` | List blocked deferred work; workflow retry when eligible; operation items inspection-only; links to runs, modules, recovery audit |
| **Modules list** | `/modules` | List all modules with lifecycle state, effectively active, validation issues |
| **Module detail** | `/modules/{id}` | Lifecycle state, actions (Install/Enable/Disable/Uninstall), validation issues, links to audit and blocked work |
| **Schedules list** | `/schedules` | List schedules with target type, enabled state |
| **Schedule detail** | `/schedules/{id}` | Target, enabled, last outcome, Enable/Disable, “Trigger scheduler re-evaluation” (global); links to audit and blocked work |
| **Run detail** | `/runs/{id}` | Extended with Related context: blocked-work card when applicable, audit link, blocked-work link |
| **Audit Timeline** | `/control/audit` | Query params: `?moduleId=`, `?scheduleId=`, `?runId=` for deep links; fixed module/schedule related-reference links |

### Operator Workflows Supported

- **Health → drill-down**: Control Center Platform Health links to degraded modules, blocked schedules, blocked work; anomaly links to run/schedule/module
- **Module → audit / blocked work**: Module detail links to audit filtered by module, blocked work page
- **Schedule → run-cycle / audit**: Schedule detail has “Trigger scheduler re-evaluation” (global), audit link
- **Blocked work → retry vs inspection**: Workflow items show Retry when eligible; operation items show “Inspection only” and link to run
- **Run → blocked work / audit**: Run detail shows blocked-work context when present, links to audit filtered by run

### Actions Available from UI

| Action | Where | Scope |
|--------|-------|-------|
| Module Install/Enable/Disable/Uninstall | Module detail | Per module |
| Schedule Enable/Disable | Schedule detail | Per schedule |
| Trigger scheduler re-evaluation | Schedule detail, Blocked Work | **Global** — not schedule-specific |
| Retry blocked workflow | Blocked Work | Per run, when eligible |
| Run cancel/retry/park/resume | Run detail | Per run |

### API-Only or Out of Scope

- **Operation retry**: Blocked operation work is inspection-only; no retry from UI
- **Schedule-specific force-fire**: Only global run-cycle exists; no “fire this schedule now”
- **Full forensic tooling**: Run detail has context links but not exhaustive forensic tooling
- **Create schedule**: Out of scope; no menu link or create page

### Scope Limitations

- **Run-cycle is global**: “Trigger scheduler re-evaluation” processes all schedules due at cycle time; it is not a schedule-specific force-fire. UI, Epic33UiDisplayHelpers, and tests enforce this semantics contract.
- **Operation blocked work**: Inspection-only; link to run detail; no retry action.

## Implementation Summary

- **Management Client**: [IManagementClient](src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs), [ManagementClient](src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs) — `GetRuntimeHealthAsync`, `GetSchedulesAsync`, `GetScheduleAsync`, `EnableScheduleAsync`, `DisableScheduleAsync`, `GetBlockedWorkAsync`, `GetBlockedWorkItemAsync`, `RetryBlockedWorkAsync`, `TriggerSchedulerRunCycleAsync`
- **DTOs**: [RuntimeHealthDtos](src/Apperture.ControlPlane/Apperture.Management.Client/RuntimeHealthDtos.cs), [ScheduleDtos](src/Apperture.ControlPlane/Apperture.Management.Client/ScheduleDtos.cs), [RecoveryDtos](src/Apperture.ControlPlane/Apperture.Management.Client/RecoveryDtos.cs)
- **Pages**: [ControlCenter.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/ControlCenter.razor), [BlockedWork.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/BlockedWork.razor), [ModulesList.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Modules/ModulesList.razor), [ModuleDetail.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Modules/ModuleDetail.razor), [SchedulesList.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/SchedulesList.razor), [ScheduleDetail.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor), [RunDetail.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor), [AuditTimeline.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/AuditTimeline.razor)
- **Menu**: [ControlPanelMenuDefinitions](src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ControlPanelMenuDefinitions.cs) — added Blocked Work, Modules; Schedules has View All only (no Create link)
- **Epic33UiDisplayHelpers**: [Epic33UiDisplayHelpers](src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Epic33UiDisplayHelpers.cs) — testable display logic for blocked work, schedule action semantics, control center health, audit links
- **Tests**: [ManagementClientEpic33Tests](tests/Apperture.UnitTests/ControlPlane/ManagementClientEpic33Tests.cs), [Epic33UiTruthfulnessTests](tests/Apperture.UnitTests/ControlPlane/Epic33UiTruthfulnessTests.cs), [Epic33CrossNavigationTests](tests/Apperture.UnitTests/ControlPlane/Epic33CrossNavigationTests.cs) — client, UI truthfulness, cross-navigation

### Corrective Pass (Epic 33)

- UI truthfulness proven via `Epic33UiDisplayHelpers` and focused tests: blocked work retry vs inspection-only, schedule action global semantics, control center health counts, audit link formation
- Cross-navigation verified: Control Center → Modules/Schedules/Blocked Work/Audit; Module/Schedule/Run detail → Audit (with context query params), Blocked Work
- Schedule creation gap clarified: no `/schedules/new` link; creation remains out of scope

## Remaining Limitations

1. **Create schedule**: Out of scope; no create-schedule page or menu link
2. **Run ModuleId**: Run detail does not show module link when RunDetailDto lacks ModuleId (API does not expose it)
3. **Schedule TargetModuleExecutable**: Schedule detail does not show target-module executable; would require schedule remediation fetch
4. **API path consistency**: Some controllers use `api/` prefix, some do not; deployment must ensure client BaseAddress aligns with actual routes
