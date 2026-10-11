# CTX-3B: Integration Hard-Delete Referential-Lifecycle Inventory

## The Confirmed Gap

When an Integration is hard-deleted, the platform must ensure that all dependent entities—whether they reference the Integration by a literal `IntegrationId` column or through a generic `ScopeKind`/`ScopeId` pair—are either deleted, blocked, or cascaded through an established lifecycle operation. This document inventories the entire referential graph and specifies the treatment for each dependency.

## The Reachability Rule

An Integration delete can reach a dependent entity through one of two patterns:

1. **Direct reference via `IntegrationId` column**: The entity has a foreign key or scalar column named `IntegrationId` that points to the Integration being deleted.
2. **Scoped reference via `ScopeKind`/`ScopeId`**: The entity has a `ScopeKind` column with value `'Integration'` and a `ScopeId` column matching the Integration ID being deleted.

Any entity reachable by either pattern must have an explicit decision about how the delete cascade handles it. An entity with no decision in the inventory below and no code-level delete guard is a bug.

## Referential-Lifecycle Inventory

Grep results confirmed 13 entity files containing `IntegrationId`, and only `PlatformSettingValueEntity` contains `ScopeKind`/`ScopeId` references:

| Table | Reference | Classification | Decision | Clearable by |
|---|---|---|---|---|
| `Bindings` | `IntegrationId` | Current-state | Already blocked (existing) | `DELETE api/.../bindings/{id}` (exists) |
| `ScheduleDefinitions` | `IntegrationId` | Current-state | Already blocked (existing) | *No delete path — see follow-ups* |
| `NotificationDefinitions` | `IntegrationId` (no FK) | Current-state | **Block** (Task 4) | **`DELETE api/notification-definitions/{id}` — added by Task 2** |
| `NotificationSubscriptions` | `NotificationDefinitionId` **and** its own nullable `IntegrationId` | Current-state | **Block directly** (Task 4) | `SqlNotificationSubscriptionStore.RemoveAsync` (exists), plus Task 2's cascade |
| `IntegrationAttentionRoutings` | `IntegrationId` | Current-state | **Block** (Task 4) | **`DELETE api/integrations/{id}/attention-routing` — added by Task 3** |
| `PlatformSettingValues` | `ScopeKind = 'Integration'`, `ScopeId = <integrationId>` | Current-state, no independent meaning | **Cascade cleanup via the settings lifecycle** (Task 5) | n/a — cleaned automatically |
| `NotificationSubscriptionUserOverrides` | `NotificationDefinitionId`, `UserId`, `OperationalTeamId` — **no `IntegrationId`** | Current-state, downstream of `NotificationDefinitions` | No separate Integration-delete guard | Cascaded by Task 2's definition delete |
| `ScheduleAssignments` | `ScheduleDefinitionId` (no `IntegrationId`) | Current-state, **indirect** | No separate guard — transitively protected by the existing `ScheduleDefinitions` blocker | n/a |
| `ScheduleStates` | `ScheduleDefinitionId` (no `IntegrationId`) | Current-state, **indirect** | Same as `ScheduleAssignments` | n/a |
| `OperationRuns` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `OperationStartRequests` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `WorkflowInstances` | `IntegrationId` — **confirmed present** | **Historical (CTX-2)** | Not touched | — |
| `WorkflowStartRequests` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `OperationItemizationEntities` | `IntegrationId` — **found during review; absent from the first draft** | **Historical (CTX-2)** | Not touched | — |
| `PlatformEvents` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `NotificationInstances` | `IntegrationId` — a **literal column**, not join-derived as the first draft stated | **Historical (CTX-2)** | Not touched | — |
| `AttentionIncidents` | `IntegrationId` | **Historical (CTX-2)** | Not touched | — |
| `AuditLogs` | no `IntegrationId`; `ResourceContextId` snapshot only | **Historical (CTX-2)** | Not touched | — |

The **Clearable by** column is load-bearing: a blocker without an entry in it is a bug, not a policy.

## Decisions

### NotificationDefinitions — block delete, and make the blocker clearable

An Integration-scoped NotificationDefinition is operator-created, independently meaningful configuration: its own rule, template, severity, routing and subscriber list. Deleting the Integration must not silently discard it, which is the same judgment already encoded in the existing Binding/Schedule block. But blocking on something with no delete path would make any Integration that ever acquired a definition permanently undeletable, so Task 2 adds an explicit, authorized definition-delete operation. **Narrowing the blocker to enabled definitions was considered and rejected:** disabling does not make a definition safe to orphan.

### IntegrationAttentionRoutings — block delete, and make the blocker clearable

`IntegrationAttentionRoutingEntity` carries escalation policy, reminder intervals and primary/escalation operational-team references — independent operator configuration with its own semantics. It is an *override* of the instance default, so the natural clearing operation is "remove the override and fall back to the instance default," which Task 3 adds.

### NotificationSubscriptions — block delete, directly

The rationale here is *not* "independently meaningful resource," and getting it right matters. `NotificationSubscriptionEntity` carries a required `NotificationDefinitionId` **and**, separately, its own **nullable `IntegrationId`**. A subscription can therefore be scoped to an Integration whose definition is *not* Integration-scoped — for example a Platform- or Module-scoped definition with a subscription narrowed to one Integration. Those rows are not covered by the NotificationDefinition blocker, so subscriptions need a blocker of their own. This is the dependency graph the code actually has, not an assumption.

### PlatformSettingValues (Integration-scoped) — deliberate cascade cleanup, through the existing settings lifecycle

An Integration-scoped setting value has no independent existence outside its Integration; unlike a NotificationDefinition it is not a named, independently addressable resource. Blocking delete on stray setting overrides would be friction with no operator benefit. **But row deletion alone is not sufficient cleanup** — see the sensitive-settings finding below.

### Every CTX-2 historical/snapshot table — not touched

These intentionally retain a now-possibly-dangling `IntegrationId` string forever, with no FK, exactly as CTX-1/CTX-2 established. `NotificationInstances` keeps its `NotificationDefinitionId` after a definition is deleted, for the same reason.

## Sensitive-Settings Finding

Integration-scoped `PlatformSettingValues` **can** hold secrets by reference. `PlatformSettingDefinitionRegistry` states it plainly: a sensitive setting is *"Stored in the platform secret store; this setting retains only an opaque reference. Never stored as plaintext in PlatformSettingValues."* `IntegrationSettingsService` provisions those references through `PlatformSettingSecretMaterialProvisioner` at `ScopeKind = PlatformSettingScopeConstants.IntegrationKind`.

The established lifecycle for removing such a value — visible in `IntegrationSettingsService`'s reset and clear paths — is three steps, in order: delete the scoped value, call `PlatformSettingSecretMaterialProvisioner.TryDisableOwnedSecretAsync(key, previousRef)`, and write the per-key audit record. `TryDisableOwnedSecretAsync` is deliberately conservative: it disables only secrets the platform owns (name matches `platform-setting:{key}` and provider matches), never an arbitrary or configuration-supplied GUID that might be shared.

A raw `RemoveRange` on the setting rows would delete the **only pointer** to an **Active** secret in `ISecretStore`, permanently and silently. That is not acceptable cleanup. This plan therefore delegates to `IIntegrationSettingsService.ResetAsync` per key rather than removing rows directly.

**Not atomic, and deliberately so.** The SQL row deletion and the external `ISecretStore` mutation are two different stores; they do not form one transaction and this plan does not pretend otherwise. It follows whatever failure behavior `IntegrationSettingsService` already has for reset, and tests that behavior rather than inventing a new one.

> **Superseded by AUD-1.4 slice 3 (2026-10-09).** The delete no longer delegates to `ResetAsync` per key and is no longer a series of separate transactions. The SQL secret store joins the audited unit's transaction (AUD-1.4 slice 1), so the integration row, every integration-scoped override, the retirement of the secrets that can be shown to be safe, and the `integration.deleted` row commit together or not at all. Every persisted override is removed by its stored row id whether or not its key still has a definition; a secret is retired only under owner ruling H1 (provenance, matching setting key, no known surviving reference), and an undeterminable credential refuses the whole delete (HTTP 409). The five blockers in the table above are unchanged and are now counted under the integration row lock. See the [AUD-1.4 record](../audit-hardening/aud1-4-configuration-lifecycle-record.md) §11 to §13 and the [integration delete contract](../../../04-api/contracts.md#integration-delete). The inventory and the "Clearable by" entries above are otherwise still the reference for what the delete touches.

## Task 2 Audit Convention Note

`NotificationDefinitionService` had no pre-existing audit convention to mirror -- `CreateAsync`, `UpdateAsync`, and `SetEnabledAsync` do not audit today, and no other notification-module service does either. `DeleteAsync` (Task 2) therefore follows the general house `AuditRecord` pattern used elsewhere for "one audit record per destructive/mutating operation" (`ResourceContextManagementService`, `IntegrationSettingsService`), appending a single record per successful delete under the new `ActionConstants.NotificationDefinitionDeleted` (`notification.definition.deleted`) action, rather than inventing a new scheme.

## Follow-ups

- **`ScheduleDefinitions` blocks Integration delete and has no delete path either.** This is a pre-existing CTX-1 blocker, not one CTX-3B adds, and CTX-3A's closure already records "Schedules have no delete path at all" as deferred roadmap work. It is called out here because it is the one remaining Integration-delete blocker whose remediation path is missing, and because the reachability rule above should be applied to it when Schedule lifecycle work happens. CTX-3B deliberately does not widen into Schedule lifecycle.
- Connection true-delete eligibility, Connection retirement cleanup, and their own referential lifecycles are separate, later roadmap work.
- Whether the notification module wants an operator UI for definition deletion beyond the API added here is a notification-epic question, not a CTX-3B one. **Superseded in part, 2026-09-17:** the human partner directing this work explicitly requested a Plan 6a stabilization pass that added the smallest coherent operator UI for both new remediation operations (NotificationDefinition delete and Integration attention-routing clear) — not self-authorized scope creep into the broader notification-epic question above, which otherwise still stands.
