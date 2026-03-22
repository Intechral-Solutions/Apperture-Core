# API Contracts

## Overview

The Management API (control plane) exposes REST endpoints for module registry, workflow discovery, execution, runs, schedules, and audit. OpenAPI specs will be added when the API is implemented.

## Minimal v1 endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/modules` | List installed modules |
| GET | `/modules/{id}/workflows` | List workflows for a module |
| GET | `/operations` | List operation definitions visible to caller |
| GET | `/operations/{operationDefinitionId}` | Get one operation definition by stable id |
| POST | `/operations/{operationDefinitionId}/start` | Start an operation |
| GET | `/operations/runs` | List operation runs (operation-centric contract) |
| GET | `/operations/runs/{runId}` | Get operation run detail (operation-centric contract) |
| POST | `/workflows/{workflowId}/start` | Start a workflow (parameters, idempotency key) |
| GET | `/runs` | List operation runs |
| GET | `/runs/{id}` | Get run details |
| GET | `/runs/{id}/actions` | Get server-projected run action availability |
| GET | `/runs/{id}/timeline` | Get canonical persisted-evidence run timeline |
| POST | `/runs/{id}/park` | Request that a run park at the next safe boundary |
| POST | `/runs/{id}/resume` | Resume a parked run from the saved next step |
| GET | `/api/governance/descriptors` | List registered governed resource/action descriptors (Epic 39: with Family, Source, ModuleId) |
| GET | `/api/governance/effective-permissions` | Inspect caller effective permissions |
| GET | `/api/roles` | List roles (Epic 39: includes IsSystemRole) |
| GET | `/api/roles/{id}` | Role detail with grants (Epic 39) |
| GET | `/api/roles/{id}/permissions` | Read explicit role permission assignments |
| PUT | `/api/roles/{id}/permissions` | Replace explicit role permission assignments (Epic 39: 404 when role not found) |
| GET | `/api/users/{id}/effective-access` | Target user's effective access from roles (Epic 39) |
| GET | `/api/schedules/capabilities` | Schedule governance capability projection |
| GET | `/api/schedules` | List schedules (Epic 28/33) |
| GET | `/api/schedules/{id}` | Get schedule detail with execution state (query: runtimePlaneId) |
| POST | `/api/schedules` | Create schedule (Epic 38). Body: CreateScheduleRequestBody. Returns 201 with ScheduleDto; 400 with `{ "errors": ["..."] }` on validation failure. |
| PATCH | `/api/schedules/{id}` | Update schedule (Epic 38). Body: UpdateScheduleRequestBody (Name?, RecurrenceExpression?, TimeZone?, Enabled?). Returns 200 with ScheduleDto; 400 with errors on validation failure. Target is immutable. |
| POST | `/api/schedules/{id}/enable` | Enable schedule |
| POST | `/api/schedules/{id}/disable` | Disable schedule |
| GET | `/api/environments` | Inspect the current environment (authorization gated) |
| GET | `/api/runtime-instances` | Inspect governed runtime capabilities (validated capability ids + kind) list (authorization gated) |
| GET | `/api/runtime-instances/{runtimeId}/capabilities` | Inspect governed runtime capabilities for one runtime (capabilityId + kind) (authorization gated) |
| POST | `/api/runtime-instances/{runtimeId}/controls/shutdown/request` | Request runtime shutdown (governance gated + auditable) |
| GET | `/api/runtime-instances/{runtimeId}/controls/shutdown/requests` | List persisted runtime shutdown requests (permission-gated by `runtime.read`) |
| POST | `/api/enroll/invitation` | Operator-issued runtime enrollment invitation (governance gated + auditable) |
| POST | `/api/enroll` | Runtime enrollment using enrollment token (anonymous; token-scoped) |
| GET | `/api/control/center` | Unified operator control facade (targets + recent activity + attention) |
| GET | `/api/control/activity` | Unified operator activity feed (executions + durable runtime control requests) |
| GET | `/api/control/attention` | Cross-domain attention items for triage (permission-safe) |
| GET | `/api/control/runtime-health` | Runtime health snapshot (module/schedule/blocked-deferred diagnostics). Epic 30. |
| GET | `/api/control/recovery/blocked-work` | List blocked deferred work with remediation eligibility (query: runtimePlaneId, windowHours). Epic 31. |
| GET | `/api/control/recovery/blocked-work/{runId}` | Blocked item detail + eligibility. Epic 31. |
| POST | `/api/control/recovery/blocked-work/{runId}/retry` | Retry blocked workflow (409 if still ineligible; operations not supported). Epic 31. |
| GET | `/api/control/recovery/schedules/{id}` | Schedule remediation context (query: runtimePlaneId). Epic 31. |
| POST | `/api/control/recovery/schedules/run-cycle` | Trigger **global** scheduler run cycle (query: runtimePlaneId). Not schedule-specific. Epic 31. |
| GET | `/api/control/recovery/modules/{moduleId}` | Module remediation context. Epic 31. |

## Schedule API (Epic 28, Epic 38)

- **Create** (`POST /api/schedules`): Body `CreateScheduleRequestBody` with `Name`, `RecurrenceExpression`, `TimeZone` (default `UTC`), `Enabled` (default `true`), `TargetJobType` (0=Workflow, 1=Operation), `TargetJobConfiguration` (JSON: `{"workflowDefinitionId":"..."}` or `{"operationDefinitionId":"..."}`). Optional: `ExecutionPolicyRef`, `RuntimePlaneId` (default `local`). On validation failure returns **400** with `{ "errors": ["..."] }`. On success returns **201** with `ScheduleDto` in body.
- **Update** (`PATCH /api/schedules/{id}`): Body `UpdateScheduleRequestBody` with optional `Name`, `RecurrenceExpression`, `TimeZone`, `Enabled`. Target (kind, configuration) is immutable: if `TargetJobType` or `TargetJobConfiguration` is provided, returns **400** with `"Target cannot be changed after creation"`. On validation failure returns **400** with `{ "errors": ["..."] }`. On success returns **200** with updated `ScheduleDto` in body.
- **Validation**: Cron via Cronos format; timezone via IANA IDs; target config must be valid JSON with required key for chosen `TargetJobType`.
- **Permissions**: All schedule endpoints require `schedule.manage` (ScheduleGovernanceService.CanManageAsync). Create and edit are not audited (consistent with enable/disable).

## Recovery API (Epic 31)

Operator recovery and remediation workflows extend Epic 30 diagnostics with eligibility projection and safe retry actions.

- **Blocked work**: Workflow runs with `[Blocked]` failure summary are inspectable; eligibility (`RetryEligibleNow`) is derived from `composition.EffectivelyExecutableModuleIds` containing the definition's module.
- **Operation blocked work (inspection-only, intentional limitation)**: For `DefinitionKind=operation`, `RetryEligibleNow` is always false; `RetryEligibleReason` is "Operation retry not yet supported." Retry returns **400** with `inspectionOnly: true`, `definitionKind: "operation"`, and a message directing operators to inspect via GET blocked-work. No operation retry path exists in this epic.
- **Retry guard**: Before invoking retry, the API re-checks eligibility. If ineligible, returns 409 without calling dispatch; audits `recovery.blocked_work.retry_refused`.
- **Schedule remediation**: `RemediationAvailable` = (enabled && TargetModuleExecutable). Run-cycle triggers **global** scheduler re-evaluation; processes schedules due at cycle time. No per-schedule "force fire" in V1. **Epic 36 (corrective):** schedule detail and runtime health expose claim fields **only for an active in-progress** `ScheduleRuns` row (`Outcome = InProgress`); they do not describe historical “who claimed” after completion. Guarantee: lease-based coordination and at-most-once normal-path dispatch per fire—not universal exactly-once. Coordination audit: `scheduler.claim.recovered` only for stale-claim takeover; new claims are not audited (see Epic 36 ADR).
- **Module remediation**: `PrerequisiteHint` guides operators (e.g. "Enable module to allow new starts"); links to existing `POST /modules/{id}/enable`.
- **Audit actions**: `recovery.blocked_work.retry_requested`, `recovery.blocked_work.retry_dispatched`, `recovery.blocked_work.retry_refused`, `recovery.schedules.run_cycle_requested`.

## Operation definition and run contract notes

- Operation API identity is `operationDefinitionId` (stable, persisted).
- `OperationDefinition.Name` is execution-mapping metadata (single step-handler name), not API identity.
- Definition visibility is governed by operation read permission; a visible definition may still have `CanStart=false` when execute permission is missing.
- Run list supports operation-centric query filters via `/runs` query parameters:
  - `executionKind`
  - `operationDefinitionId`
  - `status`
  - `createdFrom`
  - `createdTo`
  - `moduleId`
  - `correlationKey`
- **Epic 35 — Run forensics**: `GET /runs/{id}` returns `RunDetailDto` with optional nested `ForensicContext` (`RunForensicContextDto`). Forensic contract (field provenance):
  - **ModuleId** — derived from workflow/operation definition via registry; null when resolution fails.
  - **OriginKind** / **OriginDetail** — derived from RetryOfRunId, start idempotency pattern, or hasStartRequest; one of unknown, manual, scheduled, retry.
  - **ScheduleId** / **ScheduleFireTimeUtc** — inferred from platform idempotency convention `schedule:{id}:{ISO}` when pattern matches; not a persisted FK. Null when not applicable.
  - **RetriedByRunIds** — direct query on OperationRuns where RetryOfRunId equals this run.
  - **ModuleExecutableNow** / **ModuleExecutionHint** — current runtime composition snapshot only; not historical.
  - **InvestigationHints** — derived hints (kind + optional targetId) for navigation; UI builds routes. When targetId is null, no link should be shown.
  - Unknown origin is returned when evidence is insufficient; the API does not fabricate causality. See [Epic35-Run-Inspection-Forensics.md](../02-decisions/Epic35-Run-Inspection-Forensics.md).
- Run kind resolution is deterministic from durable start-request ownership:
  - workflow run: present in `WorkflowStartRequests` only
  - operation run: present in `OperationStartRequests` only
  - ambiguous/unknown ownership is treated as non-projectable.

## Run result and output (Epic 20 + Epic 21)

- `GET /runs/{id}` returns `RunDetailDto` with `Result` (`RunResultDto`) and optional `ForensicContext` (Epic 35; see run list notes above).
- `RunResultDto` includes:
  - `Outcome`: terminal outcome derived from lifecycle status (`succeeded`, `failed`, `cancelled`, `in_progress`).
  - `Summary`: human-readable one-liner. For **succeeded** runs, if persisted captured-output summary is non-empty (`CapturedOutputSummary` from ADR-0019), the API uses that string; for **withheld** capture (ADR-0020), the API uses a fixed platform phrase instead of a fake payload; otherwise a fixed phrase for the outcome. This field blends “outcome headline” with “output headline” when capture provided a summary; `PayloadJson` remains the full captured JSON when inline policy allows it.
  - `PayloadJson`: operator-visible captured output JSON only when inline display is allowed: `OutputAvailability` is `captured` or `truncated`, and `OutputDisplayPolicy` is null (legacy Epic-20 plain return) or `inline`. For `summary_only` or `withheld`, full JSON is not exposed here even if it existed transiently during capture.
  - `OutputAvailability`: technical capture state: `none`, `empty`, `captured`, `truncated`, `unsupported` (ADR-0019), plus `summary_only` and `withheld` (ADR-0020 additive statuses).
  - `OutputDisplayPolicy`: null for legacy/plain returns; otherwise `inline`, `summary_only`, or `withheld` when the handler returned `OperatorRunOutput` (ADR-0020).
  - `DiagnosticsJson`: operator-visible failure diagnostics payload only when diagnostics status/policy allow inline display.
  - `DiagnosticsAvailability`: failure diagnostics state (`none`, `summary_only`, `captured`, `withheld`, `unsupported`) for failed runs.
  - `DiagnosticsDisplayPolicy`: diagnostics display policy when set (`inline`, `summary_only`, `withheld`).
- Captured output is computed on successful terminal completion from the last step’s return value (see ADR-0019, ADR-0020).
- Failure diagnostics are captured on terminal failure through run-grain fields (see ADR-0021).
- Module authors can now declare preferred success/failure operator intent via `OperatorExecutionResult` (ADR-0022); legacy plain success returns remain compatible.
- Legacy runs (pre-output-migration) show `OutputAvailability: none`; no payload.

## Governance API notes

- **Epic 39 — Admin surface**: Catalog (`GET /api/governance/descriptors`) returns `ResourceCatalogEntryDto` (ResourceType, ScopeKey, Family) and `PermissionCatalogEntryDto` (Action, ResourceType, ScopeKey, Family, Source, ModuleId). Source is `platform` or `module`; ModuleId derived from ScopeKey when `module:*`. `GET /api/roles` includes IsSystemRole. `GET /api/roles/{id}` returns role detail with grants. `PUT /api/roles/{id}/permissions` returns 404 when role not found. `GET /api/users/{id}/effective-access` returns merged permissions from assigned roles for a target user (role-membership-based; no scope containment simulation). Grant model: replace-only (assign = include in PUT body, revoke = omit). Permissions: user.read (catalog, roles, effective-access), permission.assign (SetPermissions), role.assign (user-role assignment). **Epic 39 Corrective**: Each admin endpoint's authorization (401/403) and grant/effective-access semantics are proven by unit tests; see Epic39 ADR.
- Permission assignment endpoints are intentionally API-first; UI can remain thin while governance semantics stabilize.
- Descriptor listing is sourced from `IResourceDescriptorRegistry` after platform seeding and module contribution registration.
- Module governance contributions require explicit `ResourceType` in manifest declarations (`GovernedResources`, `GovernedActions`); no operation-shaped defaults. Contributions use `ResourceFamily.Core` for generic module resources.
- Registry collision semantics: exact duplicate (same descriptor + family) is idempotent; conflicting duplicate (same key, different descriptor) fails with clear diagnostics.
- Role-permission assignment (`PUT /api/roles/{id}/permissions`) validates all requested permissions against the canonical descriptor catalog before persistence; unknown or malformed permissions return 400 with actionable error details.
- Environment/runtime governed surfaces:
  - Canonical environment descriptor uses `ResourceTypeConstants.Environment` with scope key `environment:{environmentId}`.
  - Canonical runtime (governed) descriptor uses `ResourceTypeConstants.RuntimeInstance` with scope key `environment:{environmentId}:runtime:{runtimeId}`.
  - Authorization actions:
    - `environment.read` gates environment inspection (`GET /api/environments`).
    - `runtime.read` gates runtime identity discovery (`GET /api/runtime-instances`).
    - `runtime.read` gates governed runtime capability inspection (`GET /api/runtime-instances/{runtimeId}/capabilities`).
    - `runtime.enroll.invitation.create` gates operator creation of enrollment invitations (`POST /api/enroll/invitation`).
    - `runtime.control.shutdown.request` gates request of runtime shutdown (`POST /api/runtime-instances/{runtimeId}/controls/shutdown/request`).
  - Privileged operator control (`POST /api/enroll/invitation`) writes durable audit records on successful authorization and invitation creation.
  - Runtime shutdown request creation (`runtime.shutdown.request`) writes durable audit records on successful authorization and capability checks.
  - Runtime enrollment (`POST /api/enroll`) remains token-authenticated with `AllowAnonymous`, and validates token-scoped environment mapping before registering runtime identity.
