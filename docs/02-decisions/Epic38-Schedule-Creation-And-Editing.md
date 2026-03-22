# Epic 38 — Schedule Creation and Editing

## Status

Implemented

## Context

The management API already supported `POST /api/schedules` (create) and `PATCH /api/schedules/{id}` (update), but the management client and UI did not expose these flows. Operators had no way to create or edit schedules through the management UI. Epic 38 closes this gap by adding backend validation, wiring the client, and building truthful create/edit forms.

## Summary

- **Create**: New page at `/schedules/new` with form for Name, target kind (Workflow/Operation), target identity (module→workflow or operation dropdown), recurrence (cron), time zone, enabled. Submits to `CreateScheduleAsync`; on success navigates to schedule detail.
- **Edit**: Inline edit on schedule detail page for Name, RecurrenceExpression, TimeZone, Enabled. Target is immutable after creation.
- **Validation**: Backend `IScheduleValidationService` validates cron (Cronos), timezone (IANA), target config shape before persist. Returns `400` with `{ "errors": ["..."] }` when invalid.
- **Permissions**: `schedule.manage` gates create and edit (same as enable/disable).
- **Audit**: Schedule create/edit are not audited today; consistent with enable/disable policy (Epic 34).

## Target Selection Source (Corrective Pass)

The create-form target lists are grounded in real platform truth:

- **Modules**: `GET /api/modules` → platform module catalog (`IModuleWorkflowListingService`). Permission-gated.
- **Workflows**: `GET /api/modules/{id}/workflows` → platform workflow definitions for the selected module.
- **Operations**: `GET /api/operations` → operation definitions visible to the caller (operation registry).

The UI does not use a looser client-side source. Options are authoritative for what exists in the platform. The create form displays a brief note to this effect.

## Backend Target Immutability (Corrective Pass)

Target immutability is enforced by the backend, not only reflected in the UI:

- **Update contract**: `UpdateScheduleRequestBody` accepts optional `TargetJobType` and `TargetJobConfiguration` for explicit rejection.
- **Enforcement**: If either is provided in a PATCH request, the API returns **400** with `"Target cannot be changed after creation. TargetJobType and TargetJobConfiguration are immutable."`
- **Management client**: Does not send target fields on update; only Name, RecurrenceExpression, TimeZone, Enabled.

## How to Create a Schedule

1. Navigate to `/schedules/new` (or use "Create Schedule" from the Schedules menu).
2. Enter a **Name** (required).
3. Select **Target kind**: Workflow or Operation.
4. Select **Target identity**:
   - Workflow: choose Module, then Workflow. Produces `workflowDefinitionId` (e.g. `Module.Namespace.Workflow`).
   - Operation: choose from operations list. Produces `operationDefinitionId`.
5. Enter **Recurrence** (cron expression, e.g. `0 * * * *` for hourly). Uses Cronos format.
6. Enter **Time zone** (IANA, e.g. `UTC`, `America/New_York`). Default `UTC`.
7. Set **Enabled** (default true).
8. Submit. On success, navigate to `/schedules/{id}`.

## How to Edit a Schedule

1. Open schedule detail at `/schedules/{id}`.
2. Click **Edit** (when `CanManageSchedules`).
3. Edit **Name**, **Recurrence**, **Time zone**, **Enabled**.
4. **Target type** and **Target configuration** are read-only; they cannot be changed after creation.
5. Click **Save** or **Cancel**.

## Supported Target Kinds

| Type   | TargetJobType | Target configuration key   |
|--------|---------------|----------------------------|
| Workflow  | 0             | `workflowDefinitionId`    |
| Operation | 1             | `operationDefinitionId`   |

## Required Fields (Create)

- **Name**: Non-empty.
- **RecurrenceExpression**: Valid Cronos cron expression.
- **TargetJobType**: 0 (Workflow) or 1 (Operation).
- **TargetJobConfiguration**: Valid JSON with the required key for the chosen type.

## Validation

- **Cron**: Parsed via Cronos `CronExpression.Parse`. Invalid expressions produce a 400 with an error message.
- **Time zone**: Validated via `TimeZoneInfo.FindSystemTimeZoneById` (IANA IDs).
- **Target config**: JSON must parse and contain `workflowDefinitionId` (Workflow) or `operationDefinitionId` (Operation) with non-empty string value.
- **Name (update)**: If provided, must be non-empty.

## Permissions

- Create and Edit require `schedule.manage` (same as enable/disable). Gated via `ScheduleGovernanceService.CanManageAsync` in the API and `CanManageSchedules` in the UI.
- **Update authorization**: Proven by `Update_WithoutScheduleManage_Returns403` — PATCH without `schedule.manage` returns 403.

## Unsupported (V1)

- Schedule-specific "run now" — only global scheduler re-evaluation (`POST /api/control/recovery/schedules/run-cycle`) is available.
- Target (kind, identity) is immutable after creation.
- Target existence (workflow/operation resolvable) is not validated synchronously; lifecycle guard blocks at dispatch time if invalid.
- `ExecutionPolicyRef` and `RuntimePlaneId` on create: optional, default `"local"`; no UI for multi-plane assignment in V1.

## First-Run Behavior

On first evaluation, only runs due in the last 5 minutes are dispatched to avoid a burst of historical firings. This is documented in the create form help text and in [CronScheduleEvaluator](../../src/Apperture.Persistence/Apperture.Persistence.SqlServer/CronScheduleEvaluator.cs).

## Audit

Schedule create and edit are not audited. This is consistent with enable/disable policy (Epic 34): "Schedule enable/disable: governance enforced; denial audit deferred."

## Files

| File                                  | Purpose                                      |
|---------------------------------------|----------------------------------------------|
| `ScheduleValidationService.cs`        | Backend validation (cron, timezone, target)  |
| `ScheduleCreate.razor`                | Create schedule page at `/schedules/new`     |
| `ScheduleDetail.razor`                | Extended with inline edit mode               |
| `ScheduleDtos.cs`                     | CreateScheduleRequest, UpdateScheduleRequest |
| `IManagementClient` / `ManagementClient` | CreateScheduleAsync, UpdateScheduleAsync  |
