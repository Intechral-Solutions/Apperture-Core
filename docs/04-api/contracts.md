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
| DELETE | `/api/integrations/{id}/attention-routing` | Clear the integration's override for a condition, reverting to the instance default. Same permission and re-check as the `PUT`. Success **204**, including when there was no override; unauthorized **403** with the override untouched; missing integration **404**. |
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
| Programming error of the audited write | **500** | Never a 400 or 409. |

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
| Any other audit, secret-retirement or infrastructure failure | **500** | Never reported as 400, 404 or 409. |

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
- **Failures that are not refusals are no longer read as refusals.** The routes above map an `InvalidOperationException` to their existing 400 or 404 only when it is a genuine refusal; an audit invariant, a transaction or infrastructure failure, an unconfirmed commit (`500`, `code: outcome_unknown`, with the operation event id) or a committed operation whose result could not be read back (`500`, `code: committed_result_unavailable`) reaches the host's handler.
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
- **A secret reference must be the connection's own.** A reference is accepted only if the stored secret has provider `platform-connection` and the name `connection:{id}:{field}`; a parseable id is not proof. `POST /api/connections` therefore refuses any non-empty `secretRefs` with a 400 (supply `secretValues`), and a `PATCH` is refused if it sets a reference the connection does not own, rotates a stored reference that is not its own, or changes config while the connection holds one. The refusal is the same for a missing secret, another provider, another owner or another field, and never repeats the reference. Dropping the reference and supplying `secretValues` in one `PATCH` repairs such a connection with a secret of its own.
- **Routes no longer report a programming error of an audited write as a refusal.** The connection, Application and move routes exclude an audit invariant from their `InvalidOperationException` mapping.
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
| SMTP test | `POST /api/notification-diagnostics/smtp-test` | `notification.diagnostics.smtp_test_attempted` (basis `external_attempt`): `accepted`, `rejected`, `failed` or `cancelled`; or `notification.diagnostics.test_refused` when no call was made |
| Platform pipeline test (live) | `POST /api/notification-diagnostics/platform-pipeline-test` with `dryRun: false` | `notification.diagnostics.pipeline_test_attempted`: what the run persisted and, when it dispatched, the counts of sends attempted, accepted, rejected, failed and unknown; or `test_refused` |

**What changed for callers**

- **A subscription and the managed contact it creates are one change.** `managedContactInput` with `createManagedContactIfMissing` used to commit the contact first, so any later refusal left a contact behind. Both now commit or roll back together.
- **Duplicates and collisions are refusals, not server errors.** Subscribing a recipient who is already subscribed, and renaming a definition onto another definition's name, are `400` (they were an unmapped `500` from the unique index). A managed contact address is unique by an application check that every writer of an address now serializes under one lock, so two concurrent writers of the same address (a manual create, a subscription that creates the contact, a directory import) leave one contact.
- **Writers re-check authorization once the rows they depend on are locked.** An ownership move that commits while a definition or subscription change waits is what the check reads. Subscriptions attach to a definition under the definition's row lock, so none can be added to a definition that is being deleted.
- **A change that changes nothing records nothing and no longer moves the row's update time**: setting a definition, subscription or contact to the state it already has, an update with the stored values, linking a contact to the user it is already linked to, an opt-out that already exists.
- **A directory import keeps its per-user results.** Each user is its own unit, so a failed user does not undo the others. A user whose outcome cannot be established is reported as `importFailed` with the operation id instead of as a success; a user whose contact already matches the directory records nothing (its sync time is still refreshed).
- **A diagnostic live send is recorded as what was observed.** `accepted` means the SMTP server took the message, never that it was delivered. A cancelled call is `cancelled` (outcome unknown): the message may or may not have left. Evidence is written after the send and is best-effort: a failure to write it never changes the response.
- **The immediate dispatch of a pipeline test sends only the outbox rows of the instance it created.** It used to claim the oldest pending rows of the whole system, so an operator's test could send other people's notifications and its response counted only its own.
- **Definition deletes are audited in the same transaction**, with counts and the name snapshot. The earlier row was written after the commit and carried a free-text sentence.
- Audit data excludes rule and template text, subscriber and contact addresses and names, directory object ids and principal names, the typed-in recipient of a diagnostic, subjects, bodies and the transport's error text. A recipient is identified by id only.

## Recovery API (Epic 31)

Operator recovery and remediation workflows extend Epic 30 diagnostics with eligibility projection and safe retry actions.

- **Blocked work**: Workflow runs with `[Blocked]` failure summary are inspectable; eligibility (`RetryEligibleNow`) is derived from `composition.EffectivelyExecutableModuleIds` containing the definition's module.
- **Operation blocked work (inspection-only, intentional limitation)**: For `DefinitionKind=operation`, `RetryEligibleNow` is always false; `RetryEligibleReason` is "Operation retry not yet supported." Retry returns **400** with `inspectionOnly: true`, `definitionKind: "operation"`, and a message directing operators to inspect via GET blocked-work. No operation retry path exists in this epic.
- **Retry guard**: Before invoking retry, the API re-checks eligibility. If ineligible, returns 409 without calling dispatch; audits `recovery.blocked_work.retry_refused`.
- **Schedule remediation**: `RemediationAvailable` = (enabled && TargetModuleExecutable). Run-cycle triggers **global** scheduler re-evaluation; processes schedules due at cycle time. No per-schedule "force fire" in V1. **Epic 36 (corrective):** schedule detail and runtime health expose claim fields **only for an active in-progress** `ScheduleRuns` row (`Outcome = InProgress`); they do not describe historical “who claimed” after completion. Guarantee: lease-based coordination and at-most-once normal-path dispatch per fire—not universal exactly-once. Coordination audit: `scheduler.claim.recovered` only for stale-claim takeover; new claims are not audited (see Epic 36 ADR).
- **Module remediation**: `PrerequisiteHint` guides operators (e.g. "Enable module to allow new starts"); links to existing `POST /modules/{id}/enable`.
- **Audit actions**: `recovery.blocked_work.retry_requested` (one row, committed with the retry's new run and naming it; it replaces the earlier pair of `retry_requested` before the work and `retry_dispatched` after it, and `retry_dispatched` is no longer written), `recovery.blocked_work.retry_refused`, `recovery.retry.denied`, `recovery.run_cycle.denied` and `recovery.schedules.run_cycle_requested` (succeeded, or failed when the cycle threw). Each row names the authenticated caller, or not signed in; none is recorded as `system`. See the [AUD-1.3 record](../01-architecture/transitions/audit-hardening/aud1-3-operator-action-record.md).
- **Run actions and their audit**: starting a run (the direct start endpoints and procedure launch), cancel, retry, park, resume and single-item retry each commit their durable request with one audit row in one transaction, and the request row records the requester (`RequestedBy`). A request that changes nothing (a replayed idempotency key, an existing cancel or control request, a duplicate that lost a race to a concurrent request) records nothing. Starts are recorded with parameter names only, never values; on the direct start endpoints the names are the caller's own keys, recorded as operator-supplied identifiers.
- **Failed-item acknowledge and reopen** (`POST /api/runs/{id}/operation-groups/{groupId}/items/acknowledge`, `.../reopen-attention`): every acknowledgement (or reopening) a request makes and one audit row on the run, listing the items it changed, commit together. Skipped items are reported per item as before; a request that changes no item records nothing. A reopen applies only to an acknowledgement that is still active, so a concurrent second reopen reports `NotAcknowledged` and never overwrites who reopened it.
- **Bulk item retry** (`POST /api/runs/{id}/operation-groups/{groupId}/items/retry`): each item's retry is its own transaction with its own audit row (`execution.operation_item.retry_requested` or `.retry_blocked`, naming the batch as `bulkRequestId`). The batch writes no row of its own (`execution.operation_item.bulk_retry.requested` and `.completed` are no longer written; earlier rows keep them). Results are per item and may be partial; an item whose commit could be neither confirmed nor rolled back is reported with category `OutcomeUnknown` (counted in `failedCount`): it may or may not have been requested, so the client reloads the item's retry attempts before asking again.
- **Uncertain and committed outcomes**: an audited run action whose commit can be neither confirmed nor ruled out returns 500 with `code: outcome_unknown` and `operationEventId`. One whose commit was confirmed but whose result could not be read back returns 500 with `code: committed_result_unavailable` and `operationEventId`: the change is applied, and the client reloads instead of repeating it. On the run action endpoints, a programming error of the audited write itself is a 500, never a 400 or 409 refusal.

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

- **Epic 39 — Admin surface**: Catalog (`GET /api/governance/descriptors`) returns `ResourceCatalogEntryDto` (ResourceType, ScopeKey, Family) and `PermissionCatalogEntryDto` (Action, ResourceType, ScopeKey, Family, Source, ModuleId). Source is `platform` or `module`; ModuleId derived from ScopeKey when `module:*`. `GET /api/roles` includes IsSystemRole. `GET /api/roles/{id}` returns role detail with grants. `PUT /api/roles/{id}/permissions` returns 404 when role not found. `GET /api/users/{id}/effective-access` returns merged permissions from assigned roles for a target user (role-membership-based; no scope containment simulation). Grant model: replace-only (assign = include in PUT body, revoke = omit). Permissions: user.read (catalog, roles, effective-access), permission.assign (SetPermissions), role.assign (user-role assignment). **Epic 39 Corrective**: Each admin endpoint's authorization (401/403) and grant/effective-access semantics are proven by unit tests; see Epic39 ADR. **Epic 40 — Auth management UI**: Management UI surfaces governance catalog (`/governance/catalog`), roles list/detail with replace-all permission editing (`/governance/roles`, `/governance/roles/{id}`), users list and effective-access inspection (`/governance/users`, `/governance/users/{id}/effective-access`). Operator capabilities: CanViewGovernance (user.read), CanAssignPermissions (permission.assign). See Epic40 ADR.
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
