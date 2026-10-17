# API Contracts

## Overview

The Management API (control plane) exposes REST endpoints for module registry, workflow discovery, execution, runs, schedules, and audit. OpenAPI specs will be added when the API is implemented.

## Minimal v1 endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/modules` | List installed modules |
| GET | `/modules/{id}/workflows` | List workflows for a module |
| POST | `/api/modules/{id}/install`, `/enable`, `/disable`, `/uninstall` | Change a module's persisted lifecycle state (requires `module.configure`). Each change is one audited unit; a refused transition answers **200** with `succeeded: false` and its blockers; **409** `module_lifecycle_busy` when another change is in progress. See [Attention routing, module lifecycle and runtime shutdown request lifecycle](#attention-routing-module-lifecycle-and-runtime-shutdown-request-lifecycle). |
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
| GET | `/api/governance/descriptors` | List registered governed resource/action descriptors (with Family, Source, ModuleId, DisplayName, Description, Category, Sensitivity, RequiresStepUp) |
| GET | `/api/governance/effective-permissions` | Inspect caller effective permissions |
| GET | `/api/roles` | List roles (Epic 39: includes IsSystemRole) |
| GET | `/api/roles/{id}` | Role detail with grants (Epic 39) |
| GET | `/api/roles/{id}/permissions` | Read explicit role permission assignments |
| PUT | `/api/roles/{id}/permissions` | Replace explicit role permission assignments (Epic 39: 404 when role not found) |
| GET | `/api/users/{id}/effective-access` | Target user's effective access from roles (Epic 39) |
| PUT | `/api/users/{id}/username` | Rename a user's username, or reclaim one of their own retired names. Requires `user.manage`; no step-up. Body `{ "username": "…" }`, normalized (trim, lower-case) and checked against the current username policy. 200 `{ userId, username, outcome }` (`assigned`, `reclaimed`, `unchanged`); 400 `username_invalid` / `username_reserved`; 404 unknown user; 409 `username_taken` (held, or once held, by another user), `username_concurrent_change`, or `UnsafeOperation` for Recovery Access. Writes audit `governance.user.username.changed` (`previousUsername`, `newUsername`) when the name changes. The Local sign-in name follows the username; credentials, roles and sessions are unchanged. |
| GET | `/api/schedules/capabilities` | Schedule governance capability projection |
| GET | `/api/schedules` | List schedules (Epic 28/33) |
| GET | `/api/schedules/{id}` | Get schedule detail with execution state (query: runtimePlaneId) |
| POST | `/api/schedules` | Create schedule (Epic 38). Body: CreateScheduleRequestBody. Returns 201 with ScheduleDto; 400 with `{ "errors": ["..."] }` on validation failure. |
| PATCH | `/api/schedules/{id}` | Update schedule (Epic 38). Body: UpdateScheduleRequestBody (Name?, RecurrenceExpression?, TimeZone?, Enabled?). Returns 200 with ScheduleDto; 400 with errors on validation failure. Target is immutable. |
| POST | `/api/schedules/{id}/enable` | Enable schedule |
| POST | `/api/schedules/{id}/disable` | Disable schedule |
| DELETE | `/api/schedules/{id}` | Hard-delete a disabled schedule (LFC-1). Success **204**. Enabled **409** `{ "error": "Cannot delete schedule: it is still enabled. Disable it first." }`. Missing **404**. Unauthorized **403**. A commit that could be neither confirmed nor ruled out is **500** `code: outcome_unknown`. No request body. Historical firings are retained. |
| DELETE | `/api/integrations/{id}` | Hard-delete an integration that nothing depends on, with its stored setting overrides. See [Integration delete](#integration-delete). Requires `integration.delete`. Success **204**; unauthorized **403**; missing **404**; a dependent **400**; an undetermined stored credential **409** `code: integration_setting_credential_undetermined`; a concurrent settings change **409**; unconfirmed commit **500** `code: outcome_unknown`. No request body. |
| PUT | `/api/config/platform-settings/{key}` | Set an instance setting, or replace or clear its secret. See [Platform and integration settings API](#platform-and-integration-settings-api). Requires `platformSettings.manage`. |
| DELETE | `/api/config/platform-settings/{key}` | Reset an instance setting to its configured or default value. Success **200** with the resolved setting; unknown key or a key not applicable at instance scope **404**; concurrent change **409**. |
| PUT | `/api/integrations/{integrationId}/settings/{key}` | As the platform route, for an integration-scoped setting. |
| DELETE | `/api/integrations/{integrationId}/settings/{key}` | As the platform reset, for an integration-scoped setting. |
| PUT | `/api/integrations/{id}/attention-routing` | Create or change the integration's attention-routing override for a condition (query: `conditionKey`, default `expected_run_missing`). Requires `integration.update` on the integration, in addition to being able to read it; the permission is evaluated before the write and again once the integration row is locked, so an ownership move in between is seen. Success **200** with the effective routing; unauthorized **403** with nothing stored; missing integration or unknown team **404**. |
| GET, PUT | `/api/attention-routing/defaults` | Read, or change, the instance-wide attention-routing default for a condition (query: `conditionKey`, default `expected_run_missing`). Requires schedule management. `GET` answers **200** with the unconfigured default when none is stored. `PUT` is one audited unit and records nothing when it changes nothing; **403** when not permitted; **404** `{ error }` for a missing default or an unknown team. |
| DELETE | `/api/integrations/{id}/attention-routing` | Clear the integration's override for a condition, reverting to the instance default. Same permission and re-check as the `PUT`. Success **204**, including when there was no override; unauthorized **403** with the override untouched; missing integration **404**. |
| GET | `/api/environments` | Inspect the current environment (authorization gated) |
| GET | `/api/runtime-instances` | Inspect governed runtime capabilities (validated capability ids + kind) list (authorization gated) |
| GET | `/api/runtime-instances/{runtimeId}/capabilities` | Inspect governed runtime capabilities for one runtime (capabilityId + kind) (authorization gated) |
| POST | `/api/runtime-instances/{runtimeId}/controls/shutdown/request` | Request runtime shutdown (governance gated + auditable). **202** with the queued request (a runtime has at most one: a repeated or concurrent request receives it unchanged); **401**, **403**, **404**; **409** when the runtime does not declare the shutdown capability or the request is rejected. See [Attention routing, module lifecycle and runtime shutdown request lifecycle](#attention-routing-module-lifecycle-and-runtime-shutdown-request-lifecycle). |
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
- **Update** (`PATCH /api/schedules/{id}`): Body `UpdateScheduleRequestBody` with optional `Name`, `RecurrenceExpression`, `TimeZone`, `Enabled`; an omitted or `null` field is not changed. Target (kind, configuration) is immutable: if `TargetJobType` or `TargetJobConfiguration` is provided, returns **400** with `"Target cannot be changed after creation"`. On validation failure returns **400** with `{ "errors": ["..."] }`. Missing schedule returns **404**; enabling a schedule whose owning Application context is archived returns **409**. On success returns **200** with the committed `ScheduleDto` in body. See [Integration, binding and schedule lifecycle](#integration-binding-and-schedule-lifecycle).
- **Validation**: Cron via Cronos format; timezone via IANA IDs; target config must be valid JSON with required key for chosen `TargetJobType`.
- **Delete** (`DELETE /api/schedules/{id}`): No body. Disabled schedules return **204** after removing the definition, assignments, and assignment state. Enabled schedules return **409** and are not removed. Missing ids return **404**. Historical `ScheduleRuns`, start-request idempotency keys, audit rows, and notification instances that cite the schedule id are retained. There is no tombstone and no undelete. `POST /api/schedules` still requires `IntegrationId`.
- **Permissions**: All schedule endpoints require `schedule.manage` (ScheduleGovernanceService.CanManageAsync). There is no `schedule.delete` permission. Create (with its assignment), edit, enable, and disable are audited; see [Integration, binding and schedule lifecycle](#integration-binding-and-schedule-lifecycle). Successful delete appends `schedule.deleted`; an authorized attempt on an enabled schedule appends `schedule.delete_rejected`. Unknown ids and unauthorized callers write neither.

## Platform and integration settings API

Each change to a setting (`PUT` and `DELETE` on `/api/config/platform-settings/{key}` and `/api/integrations/{integrationId}/settings/{key}`) is one audited unit: the setting row, the secret a sensitive setting owns, the secret store's own evidence and the audit rows commit together or not at all. See the [AUD-1.4 record](../01-architecture/transitions/audit-hardening/aud1-4-configuration-lifecycle-record.md).

| Outcome | Status | Notes |
|---|---|---|
| Changed | **200** | The body is the setting as it resolves when the response is read. A concurrent writer's later value can appear in it; it is not a snapshot of what this request produced. The audit row records what this request did. |
| Identical value (`PUT`) | **200** | The stored value already equals the submitted one: nothing is written, the row version does not change and no audit row is recorded. A stale `rowVersionBase64` is still a **409** first, even when the value is identical. |
| Reset of an absent override (`DELETE`) | **200** | Nothing is removed and nothing is recorded; the body is the setting's current resolved value. |
| Concurrent change (`PUT` or `DELETE`) | **409** | A stale row version, a lost first-insert race, or an override removed by another request between this request's read and its delete. Nothing this request stored survives. `DELETE` returns 409 here as `PUT` always did; it no longer surfaces as an unhandled error. |
| Invalid request (`PUT`) | **400** | Unknown key, not editable, a payload that does not parse, a value that is not an allowed option, or the notification safety check. Nothing is changed and no row is recorded. A missing or invalid `rowVersionBase64` for an existing override is also **400**. |
| Unknown setting, or not applicable at the route's scope (`DELETE`) | **404** | |
| Not permitted | **403** | `platformSettings.manage`, or the integration manage permission. |
| Commit could be neither confirmed nor ruled out | **500** `code: outcome_unknown`, `operationEventId` | The change and its audit rows are together committed or together absent. The client reloads before trying again. |
| Commit confirmed, response could not be read back | **500** `code: committed_result_unavailable`, `operationEventId` | The change is applied, including when the commit was acknowledged directly and only the response read failed. The client reloads and does not repeat the request. |
| Audit invariant violated (`AuditInvariantException`: a precondition of the audited write, such as an undeclared action or a missing unit) | **500** | The route excludes it from its refusal mapping, so it is never a 400 or 409. A malformed audit event (a producer or schema argument error) is an `ArgumentException`, which the host maps to 400. |

A malformed stored value of an ordinary setting can still be reset: the reset is recorded without a previous value. The audit rows of a setting change carry values only as the setting's definition allows (design gate §12.3): a sensitive setting never; an ordinary setting only when its definition declares it, or its shape bounds it. The sender address (`Notifications.Email.FromAddress`) records both values; the SMTP username (`Notifications.Email.SmtpUser`) records the change by name only.

## Integration delete

`DELETE /api/integrations/{id}` has no request body. It removes the integration, every stored setting override that belongs to it, and the secrets those overrides provisioned where that can be shown to be safe, as one audited unit: all of it and the `integration.deleted` audit row commit together, or none does. The integration is gone for good (no tombstone, no undelete). Historical records that cite its id (runs, audit rows, notification instances, attention incidents) are kept.

**Permission.** `integration.delete` on the integration. It is evaluated once as an early answer and again against the integration as it is locked inside the transaction, so an ownership move that commits while the delete waits is what the decision reads. Deleting never needs `integration.update`.

| Outcome | Status | Body and notes |
|---|---|---|
| Deleted | **204** | No body. Also returned when the response to the commit was lost and verification found this request's own `integration.deleted` row committed: the delete is reported as done, from that row, never from the integration being absent. |
| Not permitted, or no authenticated actor | **403** | Nothing changed. |
| Integration does not exist | **404** | `{ "error": "Integration {id} not found." }`. Includes an integration that another request deleted and committed first. Nothing is recorded. |
| A dependent still refers to it | **400** | `{ "error": "Cannot delete integration: …" }`, one of the five families below. Nothing changed and nothing recorded. |
| A stored credential could not be safely disposed of | **409** | `{ "error": "Cannot delete integration: one or more of its settings hold a stored credential whose ownership could not be established, so it cannot be safely removed. Nothing was changed.", "code": "integration_setting_credential_undetermined" }`. The whole delete is refused. The body names no setting key, secret, reference or classification detail. Nothing changed and nothing recorded. |
| Its settings changed during the delete | **409** | `{ "error": "The integration's settings were changed by another request. Reload and try again." }`, no `code`. Nothing changed. |
| Commit could be neither confirmed nor ruled out | **500** `application/problem+json` with `code: outcome_unknown` and `operationEventId` | The delete and its audit row are together committed or together absent. The client reloads before acting. |
| The operation could not be completed within its bounded reruns | **500** with `code: operation_not_completed` and `operationEventId` | A rollback was confirmed; nothing was changed. |
| Any other audit or secret-retirement failure | **500** | An audit invariant is excluded from the route's refusal mapping, so it is not reported as 400, 404 or 409. |

A repeated `DELETE` is a new request, not a replay: there is no idempotency key, so once the first has returned 204 the second is **404**. A client that received `outcome_unknown` reloads the integration to see whether it exists.

**The five dependents.** Each has no foreign key, and each is counted inside the transaction after the integration row is locked, in this order; the first one found is the one reported.

| Dependent | Message | Cleared by |
|---|---|---|
| Bindings | `…N binding(s) reference it.` | Deleting the bindings |
| Schedules | `…N schedule(s) reference it.` | Deleting the schedules (a schedule is disabled first) |
| Notification definitions scoped to the integration | `…N notification definition(s) reference it. Delete them first.` | `DELETE /api/notification-definitions/{id}` |
| Notification subscriptions that name the integration, whether the definition is scoped to it or not | `…N notification subscription(s) reference it. Remove them first.` | The existing subscription removal |
| Attention-routing overrides | `…N attention routing override(s) reference it. Clear them first.` | `DELETE /api/integrations/{id}/attention-routing` |

**Settings and their secrets.** Every stored override of the integration is removed, whether or not the setting's definition still exists or is still integration-scoped. A secret an override named is retired only when it was provisioned for that setting, nothing else in the known database locations refers to it, and the secret store permits it; one that is still referred to is kept and its override is still removed. If a stored credential's ownership cannot be established, the delete is refused (the 409 above) rather than guessed. The check covers the three known database locations and is not a serializable or globally complete search: a reference held elsewhere, or written at the same moment by an uncoordinated writer, is not seen. The reasoning and limits are in the [AUD-1.4 record](../01-architecture/transitions/audit-hardening/aud1-4-configuration-lifecycle-record.md) §11 and §12.

**Audit.** One `integration.deleted` row (Configuration category) with the integration's name, module and `settingsRemoved`, the number of overrides removed. No setting key, value, secret or reference is recorded. Each secret retired has its own `integration.settings.secret.retired` row (Secrets category, reason `integration_deleted`). No per-setting reset rows are written. A blocked or refused delete records nothing.

**What changed from the earlier endpoint.** Before audit hardening the delete answered **400** for a missing integration and for every `InvalidOperationException`, reset each setting in a separate transaction, deleted the integration in another, and appended a text audit record afterwards, so a failure partway left a half-deleted integration and an audit failure could not be told from a refusal. Now a missing integration is **404**, only the typed dependents are **400**, the credential refusal is a new **409**, and the whole delete is one transaction with one audit row. The management client surfaces any non-success with its `error` text, so its behaviour is unchanged.

## Integration, binding and schedule lifecycle

Every change below is one audited unit: the change and its one audit row commit together or not at all, and a commit whose acknowledgement is lost is verified from the committed row and reported as the success it was. Request and response shapes, permissions and the success and refusal statuses of the existing routes are unchanged; this section states what is new.

| Operation | Route | Audit row (Configuration, standard) |
|---|---|---|
| Create integration | `POST /api/integrations` | `integration.created`: name, module, whether settings were supplied (never their keys or values) |
| Update integration | `PATCH /api/integrations/{id}` | `integration.updated`, or the transition's row when the state changes: `integration.activated`, `integration.deactivated`, `integration.retired` (both states, plus the names of any other fields changed) |
| Activate, deactivate, retire | `POST /api/integrations/{id}/activate`, `/deactivate`, `/retire` | The transition's row |
| Create binding | `POST /api/integrations/{id}/bindings` | `binding.created`: module, slot, integration, connection or capability |
| Delete binding | `DELETE /api/integrations/{id}/bindings/{bindingId}`, `DELETE /api/modules/{m}/slots/{s}/binding` | `binding.deleted` |
| Create schedule | `POST /api/schedules`, `POST /api/integrations/{id}/schedules` | `schedule.created`: name, integration, enabled, runtime plane and the number of assignments created |
| Update schedule | `PATCH /api/schedules/{id}` | `schedule.updated` (names of the changed fields: name, recurrence, time zone), or `schedule.enabled` / `schedule.disabled` when the enabled flag changes |
| Enable, disable schedule | `POST /api/schedules/{id}/enable`, `/disable` | `schedule.enabled`, `schedule.disabled` |

**What changed for callers**

- **A request that changes nothing records nothing and writes nothing.** Activating an active integration, renaming to the current name, sending the settings it already has, enabling an enabled schedule or disabling a disabled one all answer 200 and no longer move `UpdatedAt`, the lifecycle timestamps or the row version. One difference: enabling an already enabled schedule whose owning Application context is archived used to answer 409 (the reactivation guard ran unconditionally) and now answers 200 as a no-op, because nothing becomes live.
- **A schedule is created with its assignment as one operation.** The create routes no longer commit the schedule and then assign it; a failure after the schedule can no longer leave one with no assignment.
- **Concurrent updates of one integration are serialized**, and an update merges over the integration as it is when the update runs, so two updates of different fields both survive. `integration.update` is evaluated again against the locked integration, so an ownership move that commits while a request waits is what the decision reads.
- **A schedule `PATCH` applies only the fields its body carries**, over the schedule as it is when the update runs (the request waits for the schedule row and reads it under that lock). A field the body leaves out (or sends as `null`) keeps its stored value and is never restored from an earlier read: a rename that overlaps another operator's disable leaves the schedule disabled, and two overlapping `PATCH` requests for different fields both survive. When two requests set the same field, the one that commits last wins. Only a body with `Enabled: true` can enable a schedule, and it is still refused with **409** when the owning Application context is archived. Target, execution policy and ownership are not rewritten. The audit row names only the fields that actually changed against the stored schedule, and a body that changes nothing (including one whose values another request already committed) records nothing.
- **The `PATCH` response is the schedule as committed**, including changes another request committed first (for example `Enabled: false` after an overlapping disable), not the body merged over an earlier read. A schedule that does not exist, or that is deleted while the request waits, answers **404** (a schedule deleted mid-request used to answer 400). Validation and its **400** responses are unchanged.
- **Failures that are not refusals are no longer read as refusals.** The routes above map an `InvalidOperationException` to their existing 400 or 404 only when it is a genuine refusal; an audit invariant, an unconfirmed commit (`500`, `code: outcome_unknown`, with the operation event id) or a committed operation whose result could not be read back (`500`, `code: committed_result_unavailable`) reaches the host's handler.
- A binding that another request deleted first is still a quiet no-op (the HTTP routes answer 404 or 204 as before). There is no unique index on a binding slot: two creates for one slot are serialized by the integration row lock and the second is refused as "already bound".
- Audit data excludes integration settings keys and values, schedule recurrence, time zone, target configuration and execution policy, and anything a connection holds. A binding records the connection's id, which is an entity id and not a secret reference.

## Connection, application and ownership-move lifecycle

Every change below is one audited unit: the change, anything that moves with it, and its audit rows commit together or not at all, and a commit whose acknowledgement is lost is verified from the committed row and reported as the success it was. A commit that could be neither confirmed nor ruled out is **500** with `code: outcome_unknown`; a verified commit whose result could not be read back is **500** `committed_result_unavailable`. Request and response shapes, permissions and the success and refusal statuses of the existing routes are unchanged except where this section says so.

| Operation | Route | Audit row |
|---|---|---|
| Create connection | `POST /api/connections` | `connection.created` (Configuration): name, connector, how many secret fields were stored; plus one `connection.secret.stored` (Secrets) per stored field |
| Update connection | `PATCH /api/connections/{id}` | `connection.updated` (Configuration): the names of the changed config fields; or `connection.secret_rotated` (Secrets, elevated) when any secret field changed, counting the fields, plus one `connection.secret.stored` per field whose material was stored |
| Retire connection | `DELETE /api/connections/{id}` | `connection.retired` |
| Verify connection | `POST /api/connections/{id}/test-connect` | `connection.verified` (basis `external_attempt`): what the verifier answered (`succeeded` for a success, `failed` for a failure or a verifier that threw, `unknown` for an unavailable answer), never its summary |
| Browse structure | `POST /api/connections/{id}/tools/sql-structure/{schemas\|tables\|columns}` | `connection.structure_browsed` (required event): the operation, how it ended and counts, never what was browsed; or `connection.structure_browse_failed` (attempt) when the request threw or was cancelled |
| Create Application | `POST /api/resource-contexts` | `resource_context.created` (Governance): name and slug |
| Update Application | `PATCH /api/resource-contexts/{id}` | `resource_context.updated`: the names of the changed fields (name, slug, description) |
| Archive, unarchive Application | `POST /api/resource-contexts/{id}/archive`, `/unarchive` | `resource_context.archived`, `resource_context.unarchived` |
| Move integration, connection, schedule | `POST /api/integrations/{id}/ownership/move`, `/api/connections/{id}/ownership/move`, `/api/schedules/{id}/ownership/move` | `{integration\|connection\|schedule}.ownership.moved_out` (related to the source context) and `.moved_in` (related to the target), sharing one correlation id; an integration move counts its attached schedules and lists their ids (up to 500). A blocked move: `.ownership.move_rejected` (attempt, related to the source), written after the unit |

**What changed for callers**

- **A connection credential changes only if the request commits.** A request that supplies `SecretValues` stores the material inside the same transaction as the connection row and its audit rows. A refusal, a failed save, a failed audit write or an uncommitted unknown outcome leaves the previous credential live, and a failed create leaves no secret behind. Before, the new version was activated first and a later failure left it live.
- **A request that changes nothing records nothing.** Retiring a retired connection, an unchanged connection `PATCH`, an Application `PATCH` that changes no field, archiving an archived Application and unarchiving an active one (the reserved Shared context included) answer as before and write nothing.
- **A connection `PATCH` merges over the connection as it is when the update runs**, so two overlapping updates of different fields both survive. **Verification writes only the verification columns**, so a rename, rotation or retirement made while the remote host was being probed is kept (a retired connection used to be written back as Active).
- **An Application `PATCH` is all or nothing.** It used to commit a rename before a refused re-slug and audit the rename; the refusal now leaves nothing changed. A create or re-slug that loses a race on the slug is the same **400** as a slug already in use (it was an unmapped **500**).
- **Archiving while the ownership gate is busy is a retryable `409`** with `code: ownership_operation_busy`, like every other ownership route (it was a **500**).
- **A browse that threw or was cancelled** is recorded as `connection.structure_browse_failed`; a remote refusal is a delivered answer and is recorded as `connection.structure_browsed`. A browse whose required row cannot be written returns no result.
- **A secret reference must be the connection's own.** A reference is accepted only if the stored secret has provider `platform-connection` and the name `connection:{id}:{field}`; a parseable id is not proof. `POST /api/connections` therefore refuses any non-empty `secretRefs` with a 400 (supply `secretValues`), and a `PATCH` that changes config is refused while the connection keeps a reference that is not its own. The refusal is the same for a missing secret, another provider, another owner or another field, and never repeats the reference. A `PATCH` can no longer send `secretRefs` at all, and a poisoned field is repaired with `secretValues` or `clearSecretFields`; see [Connection secret ownership and the secretFields read model (SEC-2.2)](#connection-secret-ownership-and-the-secretfields-read-model-sec-22).
- **Routes no longer report an audit invariant violation as a refusal.** The connection, Application and move routes exclude `AuditInvariantException` from their `InvalidOperationException` mapping.
- A blocked move still answers with its preview and changes nothing. Its rejection row is written after the transaction as a best-effort attempt of the same principal.
- Audit data excludes config values, secret values and references, connection strings, the verifier's summary and what a remote system returned. A secret is identified only by the connection and the connector's declared field name.

## Notification, managed contact and diagnostics lifecycle

Every change below is one audited unit: the change, anything that moves with it, and its audit rows commit together or not at all, and a commit whose acknowledgement is lost is verified from the committed row and reported as the success it was. A request that changes nothing records nothing. The diagnostic sends are the exception that proves the rule: they reach an external server, so they are recorded afterwards as best-effort attempts.

| Operation | Route | Audit row |
|---|---|---|
| Create, update definition | `POST /api/notification-definitions`, `PUT /api/notification-definitions/{id}` | `notification.definition.created` (name, integration, enabled, severity, primary event type); `notification.definition.updated` naming the parts that changed (`displayName`, `description`, `rule`, `template`, `enabled`, `severity`, `toastDefault`), never their content |
| Enable, disable definition | `PATCH /api/notification-definitions/{id}/enabled` | `notification.definition.enabled`, `notification.definition.disabled` |
| Delete definition | `DELETE /api/notification-definitions/{id}` | `notification.definition.deleted`: the name snapshot and how many subscriptions, user opt-outs and per-user preferences went with it |
| Add, remove, patch subscription | `POST /api/notification-definitions/{id}/subscriptions`, `DELETE .../subscriptions/{sid}`, `PATCH .../subscriptions/{sid}` | `notification.subscription.added` (recipient kind and id, delivery mode), `.removed`, `.updated` (`enabled`, `disabled` or `delivery_mode`); a subscription that creates its managed contact also records `managed_contact.created` in the same unit |
| Set, remove user opt-out | `POST`, `DELETE /api/notification-definitions/{id}/user-opt-outs` | `notification.subscription.user_opt_out_set`, `.user_opt_out_removed` (the user and team by id) |
| Create, update, enable managed contact | `POST /api/managed-contacts`, `PUT .../{id}`, `PUT .../{id}/enabled` | `managed_contact.created` (source, how it was created, whether it has an address), `managed_contact.updated` naming the changed fields |
| Link, unlink managed contact | `PUT /api/managed-contacts/{id}/link-user`, `/unlink-user` | `managed_contact.user_linked`, `managed_contact.user_unlinked` (elevated; the user by id) |
| Import directory users, groups | `POST /api/managed-contacts/import/entra-users`, `/import/entra-groups` | One unit per user or group: `managed_contact.created` (via `entra_import`) or `managed_contact.updated`; `managed_contact.group_imported` |
| SMTP test | `POST /api/notification-diagnostics/smtp-test` | `notification.diagnostics.smtp_test_attempted` (basis `external_attempt`): `accepted`, `rejected`, `failed` or `no_answer`; or `notification.diagnostics.test_refused` when no call was made |
| Platform pipeline test (live) | `POST /api/notification-diagnostics/platform-pipeline-test` with `dryRun: false` | `notification.diagnostics.pipeline_test_attempted`: what the run persisted and, when it dispatched, the counts of sends attempted, accepted, rejected, failed and unknown; or `test_refused` |

**What changed for callers**

- **A subscription and the managed contact it creates are one change.** `managedContactInput` with `createManagedContactIfMissing` used to commit the contact first, so any later refusal left a contact behind. Both now commit or roll back together.
- **A subscription's `deliveryMode` is validated on add** (`immediate`, `muted`, `digestOnly` or `immediateAndDigest`, as `PATCH` already did), and a `recipientRef` must be an id: an email address typed there is refused (use `managedContactInput`), because the recipient is recorded by id.
- **Duplicates and collisions are refusals, not server errors.** Subscribing a recipient who is already subscribed, and renaming a definition onto another definition's name, are `400` (they were an unmapped `500` from the unique index). A managed contact address is unique by an application check that every writer of an address now serializes under one lock, so two concurrent writers of the same address (a manual create, a subscription that creates the contact, a directory import) leave one contact.
- **Writers re-check authorization once the rows they depend on are locked.** An ownership move that commits while a definition or subscription change waits is what the check reads. Subscriptions attach to a definition under the definition's row lock, so none can be added to a definition that is being deleted.
- **A change that changes nothing records nothing and no longer moves the row's update time**: setting a definition, subscription or contact to the state it already has, an update with the stored values, linking a contact to the user it is already linked to, an opt-out that already exists.
- **A directory import keeps its per-user results.** Each user is its own unit, so a failed user does not undo the others. A user whose outcome cannot be established is reported as `importFailed` with the operation id instead of as a success; a user whose contact already matches the directory records nothing (its sync time is still refreshed).
- **A diagnostic live send is recorded as what was observed.** `accepted` means the SMTP server took the message, never that it was delivered. A call that was cancelled, timed out or lost after the session began is `no_answer` (outcome unknown): the message may or may not have left. An address or credential the platform could not use is a refusal, not an attempt. Evidence is written after the send and is best-effort: a failure to write it never changes the response.
- **The immediate dispatch of a pipeline test sends only the outbox rows of the instance it created.** It used to claim the oldest pending rows of the whole system, so an operator's test could send other people's notifications and its response counted only its own.
- **Definition deletes are audited in the same transaction**, with counts and the name snapshot. The earlier row was written after the commit and carried a free-text sentence.
- Audit data excludes rule and template text, subscriber and contact addresses and names, directory object ids and principal names, the typed-in recipient of a diagnostic, subjects, bodies and the transport's error text. A recipient is identified by id only.

## Attention routing, module lifecycle and runtime shutdown request lifecycle

Every change below is one audited unit: the change and its audit row (or rows) commit together or not at all, and a commit whose acknowledgement is lost is verified from the committed row and reported as the success it was. A request that changes nothing records nothing and does not move the row's update time. Authorization and the early existence checks answer a refused request before the unit opens; the checks the decision depends on (authorization, and for routing the team ids) run, or run again, inside the unit once the rows they depend on are locked.

| Operation | Route | Audit row |
|---|---|---|
| Update the instance default routing | `PUT /api/attention-routing/defaults` (query: `conditionKey`, default `expected_run_missing`) | `attention_routing.default.updated` (Configuration, standard; target the instance): the condition key and the names of the settings that changed |
| Create, change an integration's override | `PUT /api/integrations/{id}/attention-routing` | `attention_routing.override.created`, `attention_routing.override.updated` (target the integration, its name as a snapshot, its owning context): the condition key and, for an update, the names of the settings that changed |
| Clear an integration's override | `DELETE /api/integrations/{id}/attention-routing` | `attention_routing.override.cleared` |
| Install, enable, disable, uninstall a module | `POST /api/modules/{id}/install`, `/enable`, `/disable`, `/uninstall` | `module.lifecycle.install`, `.enable`, `.disable`, `.uninstall` (Configuration, standard; target the module): the persisted version, the persisted state before and after, and the restart effect the transition reports. Enabling a module that is not installed also records `module.lifecycle.install` in the same unit; both rows carry `implicitInstall` |
| A refused module transition | the same routes | `module.lifecycle.install_blocked`, `.enable_blocked`, `.disable_blocked`, `.uninstall_blocked` (outcome `blocked`, basis `attempt`): the persisted state and the blocker codes, written after the transaction as a best-effort attempt |
| Request a runtime shutdown | `POST /api/runtime-instances/{runtimeId}/controls/shutdown/request` | `governance.runtime.shutdown.request.created` (basis `request`, outcome `requested`; target the runtime instance): the runtime id, the request id and the control kind |

Permissions are unchanged: the instance default needs schedule management, an integration override needs `integration.update` on the integration (see the routes above), module lifecycle needs `module.configure`, and the shutdown request needs `runtime.control.shutdown.request` on the runtime plus the runtime's declared shutdown capability.

**Responses**

| Case | Status | Notes |
|---|---|---|
| Module transition applied, or already in the requested state | **200** `{ succeeded: true, moduleId, action, state, effect, blockers: [] }` | Already satisfied (installing an installed module, enabling an enabled module at the discovered version, disabling a disabled one) answers as before and records nothing. Blockers are evaluated first, so an enabled module whose contributions now conflict with another enabled module answers the refusal row below, not this one. |
| Module transition refused by a blocker or because the module is not installed | **200** `{ succeeded: false, …, blockers: [{ code, message }] }` | Nothing changed. The blocker codes are recorded as an attempt; the messages are not. Disabling or uninstalling a module that is not installed is the blocker `not_installed`. |
| Module not discovered by this host | **400** `{ succeeded: false, …, blockers: [{ code: "invalid_operation", message }] }` | Nothing changed and nothing is recorded. |
| Another module change is in progress | **409** `{ error, code: "module_lifecycle_busy" }` | Module transitions are serialized, because whether one module can be enabled depends on which others are enabled. A request waits up to 15 seconds for the change in progress, then answers 409 with nothing changed or recorded; a wait that is cancelled or chosen as a deadlock victim answers the same 409 at once. The client may retry. |
| Not permitted | **403** | Nothing changed. |
| Shutdown request queued, or already queued | **202** `{ requestId, runtimeId, controlKind, status }` | A runtime has at most one queued shutdown request. A second request, including a concurrent one, receives the request already queued (its id and status). The stored request keeps its original requester and time, and no second audit row is written. A request that finds the runtime deleted, or its capability withdrawn, once the runtime's row is locked answers 409 `RuntimeShutdownRequestRejected`, not 404. |
| Shutdown request, no authenticated actor / not permitted / unknown runtime | **401** / **403** / **404** | Nothing stored. |
| Shutdown request, runtime does not declare the capability / request rejected | **409** `{ error: "RuntimeMissingShutdownControlCapability" }` / `{ error: "RuntimeShutdownRequestRejected" }` | Nothing stored. |
| Routing: missing integration, missing instance default or unknown team | **404** | Nothing changed. A missing integration found before the write answers a bare 404; one found under the lock, a missing instance default and an unknown team answer `{ error }`. |
| Commit could be neither confirmed nor ruled out | **500** `code: outcome_unknown`, `operationEventId` | The change and its audit rows are together committed or together absent. The client reloads before trying again. |
| Commit confirmed, response could not be read back | **500** `code: committed_result_unavailable`, `operationEventId` | The change is applied. The client reloads and does not repeat the request. |
| Audit invariant violated (`AuditInvariantException`) | **500** | The routes exclude it from their refusal mapping, so it is never a 400, 404 or 409. A malformed audit event is an `ArgumentException`, which the host maps to 400; an `InvalidOperationException` that is not an audit invariant (for example an infrastructure error) keeps each route's existing 404 (routing) or 400 `invalid_operation` (module lifecycle) mapping. |

**What changed for callers**

- **A blocked module enable changes nothing.** It used to persist `Enabled = true` on an installed, disabled module, or the implicit installation of a module that was not installed, under a row that said it was blocked.
- **Enabling a module that is not installed is one atomic step.** The installation, its enablement and both audit rows commit together; a blocked or failed enable leaves no installation behind. The response is the one the endpoint always gave.
- **A refused module transition is recorded as an attempt, not as the change.** The row names blocker codes only; blocker messages stay in the response.
- **The restart effect is what the transition reports, not a claim about a running process.** An install is immediate, an enable or disable is partial until restart, and an uninstall requires a restart. A row records the persisted state and that effect; it never says that a runtime process has or has not picked the module up.
- **A shutdown request is attributed to the authenticated principal that created it.** The queued request's `RequestedBy` and the audit row's actor are the same value, and neither can be supplied by the caller. A request that finds a queued request, or loses a race to create one, records nothing; before this change every call recorded a success under its own name.
- **Concurrent shutdown requests for one runtime queue on the runtime's identity row.** Exactly one queued request and one audit row result, and each caller receives that request.
- **Attention routing is written under row locks.** An integration's override is written under the integration row (so an integration delete, an ownership move and a routing write are serialized, and the authorization is read again once the row is held); the instance default is written under its own row. Setting a value to what it already is changes nothing and records nothing; clearing an override that does not exist is still **204**.
- **Failures that are not refusals are no longer reported as refusals.** An audit invariant, an unconfirmed commit or a confirmed commit whose result could not be read back on these routes is a 500, not a 400, 404 or 409. Other infrastructure exceptions keep each route's existing mapping.
- Audit data excludes team ids, durations and flag values, recipients and addresses, blocker messages and any condition key outside the closed set (`expected_run_missing`, or `other`). A module is identified by its id, and a runtime by its scope key.

## Attention incident acknowledgement (SEC-2.1)

`POST /api/integrations/{id}/attention-incidents/{incidentId}/acknowledge` with the optional body `{ "note": string | null }` acknowledges an active attention incident. Acknowledging changes how the integration alerts: on a route whose attention routing sets `SuppressRemindersWhenAcknowledged` or `SuppressEscalationWhenAcknowledged` it stops that incident's reminders and escalations. It is therefore a change to the integration, and the permission is **`integration.update`** on the integration (it used to be `integration.read`). The permission is checked before the unit opens and again inside it once the integration row is locked, so an ownership move that commits in between is what the decision reads.

It is one audited unit: the three acknowledgement columns (`AcknowledgedAtUtc`, `AcknowledgedBy`, `AcknowledgementNote`) and one `attention_incident.acknowledged` audit row commit together or not at all, and a commit whose acknowledgement is lost is verified from the committed row and reported as the success it was. The acknowledger is the authenticated user the unit records, the same value the audit row carries as its actor; nothing in the request names who acknowledged.

| Outcome | Status | Notes |
|---|---|---|
| Acknowledged | **204** | The incident was active and unacknowledged. One audit row is recorded. |
| Already acknowledged | **204** | The incident is active and was already acknowledged, by anyone. Nothing is written or recorded, and the first acknowledger, time and note are kept. Of two concurrent acknowledgements one wins and the other is this case. |
| Not permitted | **403** | Without `integration.read` or without `integration.update`, including after an ownership move to a context where the caller lacks it. Nothing changed and nothing is recorded. |
| No authenticated actor | **401** | |
| Unknown integration, unknown incident, or an incident of another integration | **404** | Nothing changed. |
| The incident is resolved | **409** `{ error, code: "attention_incident_not_active" }` | Nothing changed, including when it was acknowledged while it was active. An acknowledgement that races the observation worker's resolution ends either acknowledged and then resolved, or as this 409; the acknowledgement is never lost. |
| Note over 1024 characters (after trimming) | **400** `{ error, code: "attention_note_too_long" }` | Previously an unhandled 500. Nothing changed. A blank or whitespace-only note is stored as null. |
| Commit could be neither confirmed nor ruled out | **500** `code: outcome_unknown`, `operationEventId` | The change and its audit row are together committed or together absent. The client reloads before trying again. |
| Commit confirmed, response could not be read back | **500** `code: committed_result_unavailable`, `operationEventId` | The acknowledgement is applied. |

**What changed for callers**

- **A principal with only `integration.read` can no longer acknowledge.** The Management UI offers Acknowledge only to an operator whose capabilities include `CanUpdateIntegrations`; the incident history stays visible to everyone who can open the page.
- **A repeat is not an overwrite.** It used to replace the acknowledger, time and note; it is now a 204 that changes nothing.
- **A resolved incident cannot be acknowledged.** It used to be written.
- **The audit row** (category `operations`, standard severity, basis `change`, target the integration, the integration's owning context as the related reference) names the integration (a name snapshot), the incident id, the condition key (`expected_run_missing`, or `other`), when the incident started, and `noteProvided`. The note's text, recipients and notification content are not fields and are never recorded in an audit row, a log line or an exception message.
- The tracker that opens, observes and resolves incidents no longer has an acknowledgement write. A resolution writes only the columns it changes.

## Connection secret ownership and the secretFields read model (SEC-2.2)

A connection row names each of its secrets by id, and an id says nothing about who owns the secret. Two changes follow. Every place that turns a connection into a secret now checks, on the secret's own stored descriptor, that the secret belongs to that connection field before the secret is read. And no response, and no request, carries a reference any more.

**Ownership.** A connection field owns exactly the secret stored under provider `platform-connection` with the name `connection:{connectionId}:{fieldKey}`, compared exactly (case sensitive). Worker slot resolution, the integration settings options (Campus Nexus campuses), test-connect for the SQL, Sftp and Campus Nexus verifiers, and structure browse all resolve through one boundary that applies this rule before the version row or payload of any secret is read. A reference that is blank, unreadable, missing, disabled, or names another connection's, another field's or another provider's secret is refused identically: the step or probe fails with the fixed text it already used (`A secret this connection needs could not be resolved.` for a run; the existing masked message for test-connect and browse), and one `connection.secret.resolution_refused` audit row (category `secrets`, elevated, basis `attempt`) records the field key and the kind of operation (`run`, `verify`, `browse` or `settings_options`), attributed to the worker on a run and to the signed-in operator otherwise. The row never holds the reference, the secret found or its owner. One call to the boundary writes one row, whichever layers it passed through; a retry or a new request is a new operation and writes its own. If the row cannot be written, the refusal stands and the secret is still not delivered.

**Read model (breaking).** `secretRefs` is removed from every connection response (list, detail, create `201`, `PATCH` and test-connect) and replaced by:

```json
"secretFields": [ { "key": "Password", "configured": true }, { "key": "OldApiKey", "configured": true } ]
```

The list holds the connector's declared secret fields, in the connector's own order, followed by every other key the connection still stores (a field the connector no longer declares), in key order. `configured` is true when a non-blank reference is stored. It says nothing about whether the secret is the field's own.

**Write contract (breaking, no deprecation period).**

| Request | Result |
|---|---|
| `PATCH` body with a `secretRefs` property, whatever it holds (even `{}`) | **400** `{ error }` naming `secretValues` and `clearSecretFields`. Nothing changed. |
| `secretValues: { "<declared field>": "<value>" }` | The value becomes a new active version of the secret the field owns. If the field names nothing, or names a secret it does not own (another connection's, another provider's, a missing or unreadable reference), a new secret the connection owns is stored and the field is repointed to it in the same audited unit. The secret the field named before is never read, versioned, disabled, adopted or otherwise changed. |
| `clearSecretFields: ["<key>", ...]` | Each key's stored reference is removed. Any stored key is accepted, a field the connector no longer declares included (matched by its own spelling first, then case-insensitively). Clearing a declared field that holds nothing is a no-op. The secret the reference named is untouched. |
| A key that is neither a secret field of the connector nor a stored key; a declared non-secret field; a blank key; more than 64 keys; the same field cleared and given a value | **400** `{ error }`, naming the key and never a reference. |
| Clearing a field the connector requires | **400** (`Required connection field '<key>' is missing`). |
| A `PATCH` that changes config while a field it leaves alone holds a reference that is not its own | **400**, naming the field key and the two repairs (`secretValues`, `clearSecretFields`). Nothing changed. |
| `POST /api/connections` with a non-empty `secretRefs` | **400**, as before: a new connection owns no secret yet. |

A `PATCH` that changes config is still refused while the connection keeps a reference that is not its own, because config decides where the credential is sent; the request that repairs or clears that field may change config. A request that only renames, or leaves config as stored, is not turned away by a legacy reference. An update that changes only secret fields is audited as `connection.secret_rotated` (a count of the fields changed) with one `connection.secret.stored` row per field that received a value (`replaced` for a rotation, `created` for a new or repointed secret); a clear alone is the `connection.secret_rotated` row without a `stored` row.

**Compatibility note.** The known consumers of `secretRefs` are the Management client's `ConnectionDto` and `UpdateConnectionRequest` and the Management UI, which are updated together with this change: `ConnectionDto.SecretRefs` is now `SecretFields`, and `UpdateConnectionRequest(Name, Config, SecretValues, ClearSecretFields)` no longer has a `SecretRefs` member. An external script that read `secretRefs` or wrote it with `PATCH` must change. The management API is instance-local and publishes no OpenAPI document.

**Persistent installations** run the read-only inventory (`scripts/sec2/Get-ConnectionSecretOwnershipInventory.sql`) before upgrading and clear or re-enter each listed field; see the SEC-2 implementation record.

## Setting secret ownership (SEC-2.3)

A sensitive setting stores its value in the secret store and keeps only an opaque reference in its row. A reference says nothing about who owns the secret it names, so every place that writes, clears, resets, retires or reads a setting's secret now proves, on the secret's own stored descriptor, that the secret belongs to that setting. **No request or response shape changes**: no field is added or removed, and the status codes stay as they were. What changes is which secret is acted on.

**Ownership** (exact, case-sensitive comparison of provider and name):

| Holder | Provider | Name |
|---|---|---|
| Instance setting `K` (`PUT /api/config/platform-settings/{key}`) | `platform-setting` | `platform-setting:K` (unchanged, so the SMTP password stays valid) |
| Setting `K` of integration `I` (`PUT /api/integrations/{integrationId}/settings/{key}`) | `integration-setting` | `integration-setting:I:K` |

Integration ids and setting keys are not character-validated, so in `I` and in `K` the characters `%` and `:` are written `%25` and `%3A`. That makes the name unambiguous and reversible; an id or key with neither character has exactly the name above. Two integrations that set the same key therefore own two different secrets.

| Operation | Behaviour |
|---|---|
| **Replace** (a secret replace on a sensitive setting) | A new active version is added only to the secret the setting owns. If the row names nothing, or names a secret the setting does not own (another integration's, an instance setting's, a connection's, an Entra registration's, a missing or unreadable reference, a disabled secret), a new secret the setting owns is stored and the row is repointed to it in the same audited unit. The secret the row named before is never read, versioned, adopted or disabled, and its descriptor, versions, payload and evidence are untouched. |
| **Clear** and **reset** | Disable only the secret the setting owns. A row that names any other secret is removed and that secret is left exactly as it was. Integration B clearing or resetting a setting never disables integration A's secret for the same key. A *legacy* secret (below) is retired only on the evidence below. |
| **Read** by a module (`IIntegrationSettingsAccessor.GetSecretAsync`) | The reference is resolved only if the secret is the setting's own, and the secret's payload is not read or unprotected otherwise. A module whose setting names another holder's secret, a missing or disabled one, or text that is not a reference sees the setting as **not configured** (`missing_secret`). It never receives a foreign value, and the error names no reference. |
| **Read** by the SMTP password resolver | The same rule applies to a reference that came from the platform setting's row: the password reads as not configured and no mail is sent with a foreign credential. A reference supplied by deployment configuration (`Notifications:Email:SmtpPasswordRef` in an application settings file or the environment) is not a setting row and is outside the rule, as are all other configured values (deployment configuration is trusted input). |
| **Integration delete** | The H1 classifier accepts the integration-specific name next to the legacy shape. A secret named for another integration is never retired by this delete: it is retained while that integration's own override holds it, and otherwise the delete is refused as ambiguous (reset the setting, then delete). |

**Reclassification.** A GUID that was stored as an ordinary value does not become a usable secret reference because a module later declares the key sensitive. At read it is refused like any other reference that is not the setting's own, and a replace stores a new owned secret without versioning the secret the text named.

**Legacy references.** Before this change an integration setting's secret was stored under the instance shape (`platform-setting` / `platform-setting:K`), which cannot say which integration made it. Such a reference is **never adopted or renamed**. The row is refused at read until an operator re-enters the value, which stores a new secret the row owns and repoints the row. The legacy secret is then retired only if it is usable, is named for that very setting, and nothing else in any scope (any other setting row, a connection or an Entra registration) refers to it; otherwise it is kept untouched. A `integration.settings.secret.retired` row with reason `replaced`, `cleared` or `reset` records a retirement.

**Evidence.** A refused read writes one audit row per top-level operation, whatever the reason, with no reference, owner or condition in it. The audit action is `integration.settings.secret.resolution_refused` (target the integration setting, field `path` of `run` or `management`) or `platform.settings.secret.resolution_refused` (target the platform setting, field `path`). They are `secrets` category, `elevated` severity, W3 attempt rows: they are best effort, and a row that cannot be written never changes the refusal.

**Persistent installations** run the read-only inventory (`scripts/sec2/Get-SettingSecretOwnershipInventory.sql`) before upgrading and re-enter each listed value; see [configuration-secret-ownership-upgrade.md](../03-operations/configuration-secret-ownership-upgrade.md).

## Recovery API (Epic 31)

Operator recovery and remediation workflows extend Epic 30 diagnostics with eligibility projection and safe retry actions.

- **Blocked work**: Workflow runs with `[Blocked]` failure summary are inspectable; eligibility (`RetryEligibleNow`) is derived from `composition.EffectivelyExecutableModuleIds` containing the definition's module.
- **Operation blocked work (inspection-only, intentional limitation)**: For `DefinitionKind=operation`, `RetryEligibleNow` is always false; `RetryEligibleReason` is "Operation retry not yet supported." Retry returns **400** with `inspectionOnly: true`, `definitionKind: "operation"`, and a message directing operators to inspect via GET blocked-work. No operation retry path exists in this epic.
- **Retry guard**: Before invoking retry, the API re-checks eligibility. If ineligible, returns 409 without calling dispatch; audits `recovery.blocked_work.retry_refused`.
- **Schedule remediation**: `RemediationAvailable` = (enabled && TargetModuleExecutable). Run-cycle triggers **global** scheduler re-evaluation; processes schedules due at cycle time. No per-schedule "force fire" in V1. **Epic 36 (corrective):** schedule detail and runtime health expose claim fields **only for an active in-progress** `ScheduleRuns` row (`Outcome = InProgress`); they do not describe historical “who claimed” after completion. Guarantee: lease-based coordination and at-most-once normal-path dispatch per fire—not universal exactly-once. Coordination audit: `scheduler.claim.recovered` only for stale-claim takeover; new claims are not audited (see Epic 36 ADR).
- **Module remediation**: `PrerequisiteHint` guides operators (e.g. "Enable module to allow new starts"); links to the existing `POST /api/modules/{id}/enable`.
- **Audit actions**: `recovery.blocked_work.retry_requested` (one row, committed with the retry's new run and naming it; it replaces the earlier pair of `retry_requested` before the work and `retry_dispatched` after it, and `retry_dispatched` is no longer written), `recovery.blocked_work.retry_refused`, `recovery.retry.denied`, `recovery.run_cycle.denied` and `recovery.schedules.run_cycle_requested` (succeeded, or failed when the cycle threw). Each row names the authenticated caller, or not signed in; none is recorded as `system`. See the [AUD-1.3 record](../01-architecture/transitions/audit-hardening/aud1-3-operator-action-record.md).
- **Run actions and their audit**: starting a run (the direct start endpoints and procedure launch), cancel, retry, park, resume and single-item retry each commit their durable request with one audit row in one transaction, and the request row records the requester (`RequestedBy`). A request that changes nothing (a replayed idempotency key, an existing cancel or control request, a duplicate that lost a race to a concurrent request) records nothing. Starts are recorded with parameter names only, never values; on the direct start endpoints the names are the caller's own keys, recorded as operator-supplied identifiers.
- **Failed-item acknowledge and reopen** (`POST /api/runs/{id}/operation-groups/{groupId}/items/acknowledge`, `.../reopen-attention`): every acknowledgement (or reopening) a request makes and one audit row on the run, listing the items it changed, commit together. Skipped items are reported per item as before; a request that changes no item records nothing. A reopen applies only to an acknowledgement that is still active, so a concurrent second reopen reports `NotAcknowledged` and never overwrites who reopened it.
- **Bulk item retry** (`POST /api/runs/{id}/operation-groups/{groupId}/items/retry`): each item's retry is its own transaction with its own audit row (`execution.operation_item.retry_requested` or `.retry_blocked`, naming the batch as `bulkRequestId`). The batch writes no row of its own (`execution.operation_item.bulk_retry.requested` and `.completed` are no longer written; earlier rows keep them). Results are per item and may be partial; an item whose commit could be neither confirmed nor rolled back is reported with category `OutcomeUnknown` (counted in `failedCount`): it may or may not have been requested, so the client reloads the item's retry attempts before asking again.
- **Uncertain and committed outcomes**: an audited run action whose commit can be neither confirmed nor ruled out returns 500 with `code: outcome_unknown` and `operationEventId`. One whose commit was confirmed but whose result could not be read back returns 500 with `code: committed_result_unavailable` and `operationEventId`: the change is applied, and the client reloads instead of repeating it. On the run action endpoints, an audit invariant violation (`AuditInvariantException`) is a 500, not a 400 or 409 refusal.

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

- **Epic 39 — Admin surface**: Catalog (`GET /api/governance/descriptors`) returns `ResourceCatalogEntryDto` (ResourceType, ScopeKey, Family) and `PermissionCatalogEntryDto` (Action, ResourceType, ScopeKey, Family, Source, ModuleId). Source is `platform` or `module`; ModuleId is the contributing module (see **Permission definitions** below). `GET /api/roles` includes IsSystemRole. `GET /api/roles/{id}` returns role detail with grants. `PUT /api/roles/{id}/permissions` returns 404 when role not found. `GET /api/users/{id}/effective-access` returns merged permissions from assigned roles for a target user (role-membership-based; no scope containment simulation). Grant model: replace-only (assign = include in PUT body, revoke = omit). Permissions: user.read (catalog, roles, effective-access), permission.assign (SetPermissions), role.assign (user-role assignment). **Epic 39 Corrective**: Each admin endpoint's authorization (401/403) and grant/effective-access semantics are proven by unit tests; see Epic39 ADR. **Epic 40 — Auth management UI**: Management UI surfaces governance catalog (`/governance/catalog`), roles list/detail with replace-all permission editing (`/governance/roles`, `/governance/roles/{id}`), users list and effective-access inspection (`/governance/users`, `/governance/users/{id}/effective-access`). Operator capabilities: CanViewGovernance (user.read), CanAssignPermissions (permission.assign). See Epic40 ADR.
- **Permission definitions**: each `PermissionCatalogEntryDto` also carries `DisplayName`, `Description`, `Category` (`AccessControl`, `Authentication`, `Integrations`, `Modules`, `Operations`, `Runtime`, `Notifications`, `Security`, `Platform`), `Sensitivity` (`Standard`, `Elevated`, `Critical`) and `RequiresStepUp`. These are additive fields. `Source` (`platform` or `module`) and `ModuleId` now come from the definition's origin and module id, not from parsing the scope key. The permission identity (`Action`, `ResourceType`, `ScopeKey`) is unchanged, so stored grants and audit identities are unaffected.
- Permission assignment endpoints are intentionally API-first; UI can remain thin while governance semantics stabilize.
- Descriptor listing is sourced from `IResourceDescriptorRegistry` after platform seeding and module contribution registration.
- Module governance contributions require explicit `ResourceType` in manifest declarations (`GovernedResources`, `GovernedActions`); no operation-shaped defaults. Contributions use `ResourceFamily.Core` for generic module resources.
- Registry collision semantics: exact duplicate (same descriptor + family) is idempotent; conflicting duplicate (same key, different descriptor) fails with clear diagnostics.
- Role-permission assignment (`PUT /api/roles/{id}/permissions`) validates all requested permissions against the canonical descriptor catalog before persistence; unknown or malformed permissions return 400 with actionable error details.
- **Anti-escalation and typed refusals (IAM-10.2a).** Changes to roles and role assignments are decided inside the audited unit, from the actor's roles and the target role as they are locked, so a concurrent change cannot slip between the decision and the write. Rule 1: `POST /api/roles` with grants and `PUT /api/roles/{id}/permissions` require the actor to hold every permission the change *adds* (keeping or removing a permission needs nothing further). Rule 2: `POST /api/users/{id}/roles` and `DELETE /api/users/{id}/roles/{roleId}` require the actor to hold every permission of the role, and only a Platform Administrator can assign or remove the Platform Administrator role. The check is made whether or not the request would change anything: an authorized no-op is **204** and writes no row; an unauthorized no-op is refused. A role whose stored permissions cannot be read is not assignable (409) and can be replaced by an explicit permission set (Rule 1 then applies to every permission in it).
  - Every refusal body carries a stable `code` and a `message`; the `/api/roles` routes also keep `error` with the same value. **Breaking change:** a business refusal that was reported as 404 (or an unhandled error) now has its own status, and 404 means only that the user or role does not exist.
  - **403** `grant_exceeds_authority`, `assignment_exceeds_authority`, `removal_exceeds_authority`, `platform_administrator_restricted`, with `missing` (at most 20 permission identities, `action@resourceType:scopeKey`) and `missingCount`. Each writes one refusal row (`governance.role.mutation_rejected` for grants, `governance.role.assignment_rejected` for assignments and removals). A caller who lacks the underlying permission (`role.assign`, `permission.assign`, `role.manage`) is still the plain **403**.
  - **409** `system_role`, `assignments_remain` (with `assignmentCount`), `duplicate_name`, `role_permissions_unreadable`, `last_viable_administrator`, and `emergency.access.unsafe_operation` for Recovery Access. **404** `user_not_found`, `role_not_found`. **400** `invalid_permission`, `role_name_required`, `role_name_too_long`, `role_description_too_long`.
  - An audit infrastructure failure is never one of these: `outcome_unknown` and `committed_result_unavailable` are unchanged, and an audit programming error or any unrecognized failure is **500**.
- **`role.manage` no longer implies other permissions, and four checks moved (IAM-10.2a).** A grant of `role.manage` allows `role.manage` and nothing else. The Platform Administrator role keeps a fixed floor of eight instance-scope combinations whatever its stored grants say (`user.read`, `user.manage` and `credential.manage` on User; `permission.assign` and `role.assign` on Role; `auth.configuration.read`, `auth.ui_policy.manage` and `auth.entra.manage` on InstanceConfiguration). The checks that moved, with no period in which the old permission is also accepted:
  - **Governance read** (`GET /api/governance/descriptors`, the `CanViewGovernance` capability and the governance dashboard card) is `user.read` on **User**, which every governance read API already enforced. `user.read` on Role is obsolete and nothing checks it. A holder of `user.read` on User now sees the governance pages and can no longer be told apart from the APIs they could already call.
  - **Team management** (`/api/operational-teams`, every route) is `operational_team.manage` on OperationalTeam, no longer `schedule.manage`. The attention contact group routes stay on `schedule.manage`.
  - **Directory search** (`POST /api/config/auth/entra-tenants/{id}/directory/users` and `.../directory/groups`) is `auth.directory.search` on InstanceConfiguration, no longer `auth.configuration.read` or `auth.entra.manage`. `validate-openid` is unchanged.
  - **Test-connect** (`POST /api/connections/{id}/test-connect`) is `connection.verify` on Connection, no longer `connection.read`. Verify is not additive to read, so a caller holding only `connection.verify` receives the connection's (secret-free) detail in the response. The runtime's own use of a connection during a run is not a route and is not checked.
  - **Capabilities.** `GET /api/control/capabilities` gains `canSearchDirectory` (`auth.directory.search`) and `canVerifyConnections` (`connection.verify`), both additive and `false` when absent. Each mirrors exactly one route permission. Like every capability flag they are instance-wide.
  - **Startup data step.** On every start, after the migrations, one audited transaction gives every **custom** role what it needs to keep doing what it did: a role with `role.manage` on Role at instance scope receives each of the eight catalog permissions the implication gave that it does not already hold (`permission.assign` and `role.assign` on Role; `auth.configuration.read`, `auth.ui_policy.manage`, `auth.entra.manage`; `user.read`, `user.manage`, `credential.manage` on User) as ordinary explicit grants; a holder of `schedule.manage` on Schedule receives `operational_team.manage`; a holder of `auth.configuration.read` or `auth.entra.manage` (including those just given by the first rule) receives `auth.directory.search`; a holder of `connection.read` receives `connection.verify`; and the obsolete `user.read` on Role is removed. Grants are matched on action and resource type ignoring case, at exactly the instance scope. System roles, roles whose stored permissions cannot be read, and grants with no resource are never changed (the last two are listed in the summary). New roles created after the release must be granted the new permissions explicitly. If the step fails the host does not start; a second start changes nothing.
  - **Audit.** The step writes one `governance.authorization.grants_migrated` row (category `governance`, elevated, basis `change`, a required mutation committed with the changes, actor `system:data-step`) only when it changed something. It records exact counts (`rolesChanged`, `grantsAdded`, `grantsRemoved`, `unreadableRoleCount`, `broadGrantRoleCount`) and, bounded to 100 entries each, the changed role ids, the role-and-permission changes (`roleId|+identity`, `roleId|-identity`) and the role ids it left alone. It records no role name, no stored JSON and no user data. The operator can trim any broad preserving grant from the role editor.
  - **Grant events** `governance.role.created`, `governance.permission.assignment.updated` and `governance.role.assignment.created` gain an optional `maxSensitivity` (`standard`, `elevated` or `critical`): the most sensitive catalog permission the role is created with, the change adds, or the assigned role holds. It is omitted when no involved permission is in the catalog. The action's own severity is unchanged.
  - **Upgrade window.** Serving is not held back while the step runs. A new binary that serves requests before the step has run can deny what the old rules allowed (a custom role holding only `role.manage` loses its implied access; a `schedule.manage` holder cannot manage teams until it is granted) and never allows more.
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
  - Runtime shutdown request creation records `governance.runtime.shutdown.request.created` (a required mutation with basis `request` and outcome `requested`) in the same transaction as the queued request, after the authorization and capability checks. The row names the runtime, the request id and the control kind, and its actor is the requesting principal. A duplicate request returns the request already queued and records nothing.
  - Runtime enrollment (`POST /api/enroll`) remains token-authenticated with `AllowAnonymous`, and validates token-scoped environment mapping before registering runtime identity.
