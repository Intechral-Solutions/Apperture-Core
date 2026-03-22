# Epic 34 — Management Authorization Hardening

## Status

Implemented

## Context

Epics 31–33 added recovery, audit timeline, runtime health, and management UI flows. Several APIs (Recovery, Audit, Runtime Health) had `[Authorize]` only — no permission checks. The UI showed action buttons regardless of operator capabilities. This epic hardens authorization: enforce permissions in Recovery, Audit, and Runtime Health APIs; introduce a minimal permission model for read vs action separation; reflect permissions in the UI; and audit privileged actions and denials.

## Permission Model

| Permission | Resource | Scope | Use |
|------------|----------|-------|-----|
| `recovery.read` | Recovery | Instance | View blocked work list, schedule/module remediation (read-only) |
| `recovery.manage` | Recovery | Instance | Retry blocked work, trigger run-cycle |
| `audit.read` | AuditPolicy | Instance | View audit timeline (list, get by id) |
| `runtime.health.read` | RuntimeHealth | Instance | View runtime health snapshot |
| `schedule.manage` | Schedule | Instance | List, detail, enable, disable schedules |
| `module.configure` | Module | Instance | List, detail, lifecycle (Install/Enable/Disable/Uninstall) |

**Note**: Trigger run-cycle (scheduler re-evaluation) is enforced by `recovery.manage`, not `schedule.manage`. The endpoint is `POST /api/control/recovery/schedules/run-cycle` in RecoveryController.

### Read vs Manage Separation

- **Recovery**: Split read vs manage. Inspection (blocked-work list, get item, schedule/module remediation GET) uses `recovery.read`. Retry and run-cycle use `recovery.manage`.
- **Audit**: Single `audit.read`. No separate "audit admin" in this epic.
- **Runtime health**: Single `runtime.health.read`.
- **Schedule**: `schedule.manage` for all; no `schedule.read` split.
- **Module**: `module.configure` for both list and lifecycle.

## API Enforcement

| Flow | API | Permission | 403 when denied |
|------|-----|------------|-----------------|
| Blocked work list | `GET /api/control/recovery/blocked-work` | recovery.read | Yes |
| Blocked work detail | `GET /api/control/recovery/blocked-work/{runId}` | recovery.read | Yes |
| Retry blocked work | `POST /api/control/recovery/blocked-work/{runId}/retry` | recovery.manage | Yes |
| Schedule remediation | `GET /api/control/recovery/schedules/{id}` | recovery.read | Yes |
| Run schedule cycle | `POST /api/control/recovery/schedules/run-cycle` | recovery.manage | Yes |
| Module remediation | `GET /api/control/recovery/modules/{moduleId}` | recovery.read | Yes |
| Audit list | `GET /api/control/audit` | audit.read | Yes |
| Audit get by id | `GET /api/control/audit/{id}` | audit.read | Yes |
| Runtime health | `GET /api/control/runtime-health` | runtime.health.read | Yes |
| Module lifecycle | `POST /modules/{id}/install`, enable, disable, uninstall | module.configure (via ModuleWorkflowListingService) | Yes |
| Schedule enable/disable | `POST /api/schedules/{id}/enable`, disable | schedule.manage (via ScheduleGovernanceService) | Yes |
| Operator capabilities | `GET /api/control/capabilities` | [Authorize] | Yes (401) |

## UI Authorization Behavior

### Capabilities Endpoint

`GET /api/control/capabilities` returns `OperatorCapabilitiesDto`:

- `CanManageSchedules` — schedule.manage
- `CanManageRecovery` — recovery.manage
- `CanViewAudit` — audit.read
- `CanViewRuntimeHealth` — runtime.health.read
- `CanManageModules` — module.configure

The UI fetches capabilities via `IOperatorCapabilitiesService` (cached per scope). Pages use this to hide/disable actions.

### Page-Specific Behavior

| Page | Permission-driven behavior |
|------|----------------------------|
| Blocked Work | Hide Retry buttons and "Trigger scheduler cycle" when `CanManageRecovery` is false |
| Schedule detail | Enable/Disable gated by `CanManageSchedules`; "Trigger scheduler re-evaluation" gated by `CanManageRecovery` (run-cycle endpoint is in RecoveryController, requires recovery.manage) |
| Module detail | Hide Install/Enable/Disable/Uninstall when `CanManageModules` is false |
| Control Center | Platform Health section: show only when `CanViewRuntimeHealth`; if 403 on fetch, show "Not available" |
| Audit Timeline | API returns 403 if no audit.read; UI shows "Not authorized" when 403 |

### 403 Handling

- Audit: UI catches 403 and displays "Not authorized to view the audit timeline."
- Runtime health: UI checks capabilities first; if lacking, does not fetch. If fetch returns 403, shows "Not available."
- Recovery GET: API returns 403; page load fails with error message.

## Audit of Privileged Actions and Denials

### Successful Actions (already audited)

- `recovery.blocked_work.retry_requested`, `retry_dispatched`, `retry_refused`
- `recovery.schedules.run_cycle_requested`

### Denial Audit Policy (Epic 34 Corrective)

**In scope — denials audited:**

| Action | Audit action name | Reason |
|--------|-------------------|--------|
| Recovery retry denied | `recovery.retry.denied` | High-value privileged action; explicit audit supports compliance and forensics |
| Recovery run-cycle denied | `recovery.run_cycle.denied` | Same as above |
| Audit read denied | `audit.read.denied` | Sensitive read; denial reveals attempted access to audit data |

Details include `reason=permission_missing`. One record per denied attempt.

**Explicitly out of scope — denials not audited (deferred):**

- Module lifecycle (install/enable/disable/uninstall): Governance exists; denial audit deferred to avoid noise until a broader operator-action audit story is defined.
- Schedule enable/disable: Same as module lifecycle; governance enforced; denial audit deferred.
- Runtime health read: Lower sensitivity than audit; denial audit deferred.

This policy is intentional and documented so it is not accidental or inconsistent. Future work may expand denial auditing to module/schedule actions.

## Implementation Summary

- **Governance**: [ActionConstants](src/Apperture.Core/Apperture.Core.Domain/Governance/ActionConstants.cs), [ResourceTypeConstants](src/Apperture.Core/Apperture.Core.Domain/Governance/ResourceTypeConstants.cs), [PlatformPermissionSeeder](src/Apperture.ControlPlane/Apperture.ControlPlane.Security/PlatformPermissionSeeder.cs) — Recovery, RuntimeHealth resources; recovery.read, recovery.manage, runtime.health.read
- **Governance services**: [IRecoveryGovernanceService](src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRecoveryGovernanceService.cs), [RecoveryGovernanceService](src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RecoveryGovernanceService.cs)
- **Controllers**: [RecoveryController](src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/RecoveryController.cs), [AuditController](src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuditController.cs), [RuntimeHealthController](src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/RuntimeHealthController.cs), [ControlCapabilitiesController](src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/ControlCapabilitiesController.cs)
- **Capabilities**: [IOperatorCapabilitiesQueryService](src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IOperatorCapabilitiesQueryService.cs), [OperatorCapabilitiesQueryService](src/Apperture.ControlPlane/Apperture.ControlPlane.Security/OperatorCapabilitiesQueryService.cs)
- **Client**: [GetOperatorCapabilitiesAsync](src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs)
- **UI**: [OperatorCapabilitiesService](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/OperatorCapabilitiesService.cs), BlockedWork, ScheduleDetail, ModuleDetail, ControlCenter, AuditTimeline
- **Tests**: [RecoveryControllerTests](tests/Apperture.UnitTests/ControlPlane/RecoveryControllerTests.cs), [AuditControllerTests](tests/Apperture.UnitTests/ControlPlane/AuditControllerTests.cs), [Epic34AuthorizationTests](tests/Apperture.UnitTests/ControlPlane/Epic34AuthorizationTests.cs), [Epic34CorrectivePassTests](tests/Apperture.UnitTests/ControlPlane/Epic34CorrectivePassTests.cs)

## Out of Scope / Deferred

- `schedule.read` vs `schedule.manage` split
- `module.read` vs `module.configure` split
- Full enterprise RBAC administration product
- Break-glass or force actions beyond existing patterns

## Related

- [authorization-and-governance-foundation](../01-architecture/authorization-and-governance-foundation.md)
- [Epic 31 — Operator Recovery Remediation](Epic31-Operator-Recovery-Remediation.md)
- [Epic 32 — Audit Timeline Discoverability](Epic32-Audit-Timeline-Discoverability.md)
- [Epic 33 — Management UI Deepening](Epic33-Management-UI-Deepening.md)
