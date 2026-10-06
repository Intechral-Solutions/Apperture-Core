# LFC-1 — Connection / Schedule lifecycle cleanup — design gate

Date: 2026-09-29
Status: **Complete. Merged to `master`.** LFC-1.0–LFC-1.3 satisfy the §3 rulings. Closure audit is §27 (recorded merge-ready at closeout). Product rulings in §3 are unchanged.
Branch: `lfc-1-connection-schedule-lifecycle`
Merge to `master`: `776a2bd8c9c46dcf3686947ea26f93e09eadbc19` (Merge pull request #2 from Intechral-Solutions/lfc-1-connection-schedule-lifecycle)
LFC-1.0 audited HEAD: `4c725ba8ad4362dbc15a7cfef935aa5579922270`
Implementation HEAD at closure: `a1f883585ea5d9a7d2d83d1feb81ed7c2e66569f`

This document began as the LFC-1.0 lifecycle contract. LFC-1.0 itself authorized no product change. Later slices implemented the contract. §2 is the inventory at the LFC-1.0 audited HEAD; supersession notes record what later slices changed.

Everything below is grounded in the repository at the commits cited above unless a supersession note says otherwise. Where an older note in this gate said IAM-8, INT-1, JOB-1, or SDK work was "next," that reflected earlier roadmap text. At the 2026-09-29 closeout, LFC-1 was the selected core-platform body; IAM-8 and navigation / cross-context grouping were not started. **Current scheduling:** see [`pre-release-product-roadmap.md`](../../00-overview/pre-release-product-roadmap.md) **Current next** — no next epic selected; LFC-1 is merged to `master`.

Labels used throughout:

- **Code fact** — verified in the repository at this commit.
- **Ruling** — this gate's product decision (`APPROVE`, `REJECT`, or `DEFER`).
- **Contract** — what a later implementation slice must build, and only after this gate is accepted.

---

## 1. Problem statement

CTX-3A accepted a refined Application archive rule because Connections can only be retired and Schedules cannot be deleted (`ctx3-design-gate.md` §6.5, decision D2). CTX-3B then shipped Integration hard-delete and left one current-state blocker with no remediation: any `ScheduleDefinitions` row, including a disabled one, blocks Integration delete (`ctx3b-integration-delete-referential-inventory.md` follow-ups). Connection true-delete was explicitly deferred beside that gap.

Operators can create, update, and retire a Connection, and can create, enable, and disable a Schedule. They cannot remove a Schedule. `DELETE /api/connections/{id}` already means retire, not row removal.

The question this gate decides: which of those missing operations are real lifecycle gaps, and which current behaviors are already the right terminal state.

Two domain facts drive the answer. A Connection is a credentialed workspace: Services identity is `ConnectionId`, and the retired row is what still explains bindings, secret presence, and audit. A Schedule is a firing rule owned by an Integration (or, rarely, standing alone). Once disabled it does not fire, but the row still makes Integration hard-delete unreachable. Those are different lifecycles. This gate does not force them to match.

---

## 2. Current-state inventory

### 2.1 Connection

**Code fact. State model.** `ConnectionState` is `Active` or `Retired` only (`ConnectionState.cs`). `Connection` carries `ConnectorDefinitionId`, `Name`, `Config`, `SecretRefs`, `ResourceContextId` (default Shared), and verification fields (`Connection.cs`). There is no un-retire method on `IConnectionManagementService`. A unit test locks the enum to those two values (`ResourceContextReactivationGuardTests`).

**Code fact. Persistence.** Table `Connections`. The only foreign key on the row is `ResourceContextId` → `ResourceContexts`, `ON DELETE RESTRICT` (`AppertureDbContext` Connection entity configuration). `IConnectionRepository` can add, update, get, and `SetResourceContextIdAsync`. It has no delete.

**Code fact. Logical references (no FK to `Connections`).**

| Store | Reference | Role |
|---|---|---|
| `Bindings.ConnectionId` | nullable string | Current binding target. Enumerated by `IBindingRepository.GetByConnectionIdAsync`. |
| `SecretDescriptors` / `SecretVersions` | name `connection:{connectionId}:{fieldKey}`, provider `platform-connection`; GUID stored in `Connection.SecretRefs` | Credential material. No FK. |
| `AuditLogs.Details` | JSON `resourceId` / display name on `connection.created`, `connection.updated`, `connection.secret_rotated`, `connection.retired`, `connection.verified`, `connection.structure_browsed` | Append-only evidence. `AuditRecord.Resource` is null by design on these connection audits. |
| Ownership-move audits | `connection.ownership.moved_out` / `moved_in` / `move_rejected` | Context movement evidence. |
| Readiness / workspace DTOs | in-memory `ConnectionId` | Not a stored historical snapshot. |

No current persistence entity other than `BindingEntity` has a `ConnectionId` column. `OperationRuns`, `WorkflowInstances`, start requests, `ScheduleRuns`, `PlatformEvents`, `NotificationInstances`, and `AttentionIncidents` do not store `ConnectionId`.

**Code fact. Service and API.**

| Operation | Entry | Permission | Effect |
|---|---|---|---|
| Create | `POST /api/connections` | `connection.create` | Active row. Context must exist and be Active. |
| Update | `PATCH /api/connections/{id}` | `connection.update` | Name, config, secret rotation. Does not change `State`. |
| Retire | `DELETE /api/connections/{id}` → `RetireConnectionAsync` | `connection.delete` | Sets `State = Retired`. 204. No binding check. No secret cleanup. |
| Verify | `POST /api/connections/{id}/test-connect` | `connection.read` | Verification metadata + `connection.verified`. |
| Move | `ownership/move-preview`, `ownership/move` | `connection.update` | Context change under the ownership gate. Retired connections may move. |
| Browse | connection tool routes | `connection.read` and `connection.browse` | Refuses a non-Active connection. |

There is no `connection.manage` permission. There is no hard-delete route.

**Code fact. Runtime after retire.** `SlotBoundResolver` returns null when `connection.State != Active`. `IntegrationDispatchReadinessEvaluator` raises blocking `ConnectionInactive` for a bound connection that is not Active, and `ConnectionMissing` when the id does not resolve. New bindings are offered only from active connections (`BindingEligibilityService` / `GetActiveAsync`). Existing binding rows are left in place.

**Code fact. UI.** Connection detail offers Retire, with a confirm dialog, only when the caller has `connection.delete` and the connection is not already retired (`ConnectionDetail.razor`). Retired detail is read-only. `ConnectionWorkspacePresentation.RetiredNotice` says existing bindings keep working until they are changed. That sentence disagrees with the resolver and the readiness evaluator.

**Supersession (LFC-1.3).** The retired-notice sentence in the paragraph above was the copy at the audited HEAD. It is no longer the product copy. See §26.

**Code fact. Archive.** `BlocksArchive` is `LiveIntegrations + ActiveConnections + EnabledSchedules > 0` (`ResourceContextOwnershipSummary`). A retired connection does not block archive. The summary comment records the current inability to delete as the reason those rows are ignored, not as a separate archive predicate.

**Code fact. Inventory.** HSK-D / `InventoryQueryService.MapConnection` lists every connection state, including `Retired`. Component catalog usage counts Active connections only.

**Code fact. Notifications.** No connection-scoped notification definition or attention incident type was found. Integration attention is keyed by `IntegrationId`.

### 2.2 Schedule

**Code fact. State model.** `ScheduleDefinition` is not an enum lifecycle. `Enabled` is the only on/off flag. Fields include `Name`, `RecurrenceExpression`, `TimeZone`, `Target` (job type + configuration JSON), optional `IntegrationId`, `ResourceContextId`, and `ScheduleLifecycleEvidence` timestamps (`ScheduleDefinition.cs`, `ScheduleDefinitionEntity`). There is no `ConnectionId` on the schedule. Attached schedules (`IntegrationId` set) take the Integration's context. Standalone schedules (`IntegrationId` null) are legal in the domain and default toward Shared.

**Code fact. Persistence.** Table `ScheduleDefinitions`. Foreign key: `ResourceContextId` → `ResourceContexts`, `ON DELETE RESTRICT`. `IntegrationId` is an indexed string with no FK. `IScheduleRepository` has no delete.

**Supersession (LFC-1.1).** The repository now exposes `DeleteDisabledScheduleGraphAsync`. There is still no tombstone, undelete, or schema flag. See §24.

**Code fact. Logical references (no FK to `ScheduleDefinitions`).**

| Store | Column | Role |
|---|---|---|
| `ScheduleAssignments` | `ScheduleId` | Current runtime placement on a plane. |
| `ScheduleStates` | `AssignmentId` | Current due/attempt cursor for an assignment. Not a firing history. |
| `ScheduleRuns` | `ScheduleId`, `AssignmentId`, `FireTimeUtc`, outcome, optional `RunId`, `ResourceContextId` snapshot | Historical firing record. No name, cron, or timezone column. |
| `AuditLogs` | `ScheduleId` | Dispatch and ownership-move evidence. Definition create/update/enable/disable do not append audit. |
| `PlatformEvents` | `ScheduleId` | Historical event correlation. |
| `NotificationInstances` | `ScheduleId` | Historical delivery correlation. |
| Start-request idempotency | `schedule:{scheduleId}:{fireTimeUtc:O}` | Forensic schedule identity on runs. Parsed by `RunOriginClassifier`. Not a persisted FK (`IRunQueryService` forensic remarks). |

`AttentionIncidents` have `IntegrationId` and no `ScheduleId`. `ScheduleExpectationEvaluator` considers only enabled schedules for an Active integration.

**Code fact. Service and API.** `IScheduleManagementService` exposes get, list, create, update, assign, enable, and disable. Repo search finds no `DeleteSchedule` / `RemoveSchedule`. `POST /api/schedules` rejects a missing `IntegrationId` and tells the caller to use `POST /api/integrations/{integrationId}/schedules`. The UI create page uses that integration route (`ScheduleCreate.razor`). Schedule detail offers Edit, Enable, Disable, and Move for standalone schedules. Attached schedules move only with the Integration. There is no delete control.

**Supersession (LFC-1.1 / LFC-1.2).** `DeleteScheduleAsync` and `DELETE /api/schedules/{id}` exist. Schedule detail offers confirmed Delete for a disabled manageable schedule. `POST /api/schedules` still requires `IntegrationId`. Standalone create remains unexposed. See §24 and §25.

**Code fact. Authorization.** The only schedule permission is instance-scoped `schedule.manage` (`ActionConstants.ScheduleManage`, `ScheduleGovernanceService.CanManageAsync`). Enable, disable, update, create, and schedule move all use it.

**Supersession (LFC-1.1).** Delete uses the same `schedule.manage` check. There is still no `schedule.delete` permission.

**Code fact. Enable vs disable.** Enable (and a PATCH that flips `Enabled` from false to true) acquires `IResourceOwnershipMutationGate` and refuses when the owning context is archived (`ApplyGatedScheduleTransitionAsync`). Disable is an ordinary update and is allowed on an archived context. The due evaluator skips disabled definitions.

**Code fact. Integration delete.** `DeleteIntegrationAsync` counts `ScheduleDefinitions` rows for that `IntegrationId` and refuses when the count is greater than zero, before any other cleanup (`IntegrationManagementService`). The inventory's "Clearable by" cell for that row is still empty.

**Supersession (LFC-1.1).** Integration delete still counts remaining schedule rows and still does not cascade. Operators clear that blocker by deleting the disabled schedule first. The Integration delete API itself is unchanged.

### 2.3 Application archive and context

**Code fact.** `ResourceContextService.ArchiveAsync` refuses when `BlocksArchive` is true. The operator message tells them to retire integrations, retire active connections, and disable enabled schedules. Retired connections and disabled schedules may remain on an archived Application. D2 calls that outcome semantically correct, and defers the inability to delete Connections and Schedules to later work. CTX-3 must not have added delete merely to make archive reachable; movement and the refined rule already did that.

**Supersession (LFC-1).** `BlocksArchive` is unchanged. Disabled-schedule delete is optional cleanup, not an archive requirement. Connection hard-delete remains rejected; retired connections may still remain on an archived Application.

Live means: Integration `State != Retired` (Active and Inactive both count), Connection `State == Active`, Schedule `Enabled == true`.

ResourceContexts themselves are archive-only. Shared cannot be archived or deleted.

Movement: bindings may reference the same context or Shared (`ResourceContextReferenceRule`). Connection move is blocked when a binding would become illegal. Attached schedules cannot move alone. Historical run rows are not rewritten on move.

### 2.4 Integration hard-delete, as a reference

**Code fact.** `DELETE /api/integrations/{id}` requires `integration.delete` on the integration identity. Blockers, all evaluated before any cleanup:

1. Bindings
2. Schedule definitions
3. Integration-scoped notification definitions
4. Notification subscriptions with that `IntegrationId`
5. Integration attention-routing overrides

The only deliberate cascade is Integration-scoped `PlatformSettingValues`, via `IIntegrationSettingsService.ResetAsync`, which disables a platform-owned secret through `TryDisableOwnedSecretAsync` instead of dropping the only pointer to an Active secret. CTX-2 historical tables are not updated; `IntegrationId` strings may dangle. One `integration.deleted` audit is appended after commit. A blocked delete writes no audit (`IntegrationDeleteReferentialLifecycleTests`). The UI confirm dialog lists what is automatic and what the operator must remove first.

`ConnectionSecretMaterialProvisioner` has create and rotate only. It has no disable method. The settings provisioner must not be reused against connection secrets: connection secret names use a different provider and a different name shape.

---

## 3. Product rulings

| Id | Question | Ruling |
|---|---|---|
| R1 | Should a Schedule be deletable? | **APPROVE** hard-delete of a **disabled** Schedule definition. |
| R2 | Schedule delete only when disabled? | **APPROVE.** Enabled is a refusal, not a silent disable. |
| R3 | Schedule tombstone instead of row removal? | **REJECT.** A remaining row would still fail the Integration delete count. |
| R4 | Should a retired Connection be hard-deleted? | **REJECT.** |
| R5 | Is Retired the terminal Connection state? | **APPROVE.** Un-retire is **REJECT** for LFC-1. |
| R6 | Must Schedule and Connection lifecycles match? | **REJECT.** They stay asymmetric. |
| R7 | Expose standalone Schedule creation? | **REJECT.** Leave the current API and UI restriction in place. |
| R8 | Change the Application archive predicate? | **REJECT.** |
| R9 | New `schedule.delete` permission? | **REJECT.** Use existing `schedule.manage`. |
| R10 | Disable connection secrets on retire? | **DEFER.** |
| R11 | Auto-revoke bindings when a connection is retired? | **REJECT.** |
| R12 | Snapshot schedule name/cron onto historical runs before delete? | **REJECT.** Do not invent per-fire configuration from the last definition. |

### R1–R3. Disabled Schedules are hard-deleted

The operator-facing hole is the Integration delete blocker with no remediation. CTX-3B named it and refused to widen into Schedule lifecycle. A disabled schedule does not fire, does not block archive, and is already excluded from expectation evaluation. Keeping the row forever makes "remove this firing rule, then delete the integration" impossible.

Hard-delete removes `ScheduleDefinitions` plus the operational rows that exist only to drive that definition (`ScheduleAssignments`, `ScheduleStates`). It leaves firing history in place (section 9).

Tombstone was considered because the last name, cron, timezone, and target JSON live only on the definition. It loses on reachability: Integration delete counts rows, not a deleted flag. Excluding tombstones from that count would be a second contract (a soft delete that every consumer must remember). The established Integration model already accepts a dangling id and the loss of the live display name. Schedule delete follows that model for execution evidence, and states the configuration loss explicitly (R12).

### R4–R5. Retired is the terminal Connection state

D2 already accepted an archived Application that still owns retired Connections. Archive does not need connection row removal. The Services workspace is the connection: identity, configuration, secret presence, used-in, and the retire audit all hang off that row. `DELETE /api/connections/{id}` is already the retire contract (`ManagementClient.RetireConnectionAsync`). A second destructive operation would collide with that verb and with `connection.delete`.

Removing the row would also drop the only product pointer to Active `platform-connection` secrets (`SecretRefs` plus the `connection:{id}:{field}` name) and would turn `Bindings.ConnectionId` into an id that readiness already calls `ConnectionMissing`. Today a retired connection is a readable explanation: the binding remains, the state is Retired, resolution returns null, readiness reports `ConnectionInactive`. That is a better operator fact than a missing connection.

Un-retire would be a new live transition. The reactivation guard and the two-value enum test exist because that transition does not exist. LFC-1 does not add it. An operator who needs a live connection creates one.

### R6. Asymmetry is the contract

A Connection retains credentials and is the thing bindings point at. A Schedule is a rule the Integration owns. Disable is enough to stop firing; it is not enough to finish Integration delete. Symmetry would either delete credentials to match schedules, or leave schedules undeletable to match connections. Neither matches the current domain.

### R7. Standalone creation stays unexposed

`POST /api/schedules` requires `IntegrationId`. The create page calls the integration route. Domain code still allows a null `IntegrationId`, and standalone rows can still be moved. LFC-1 does not add a create UX, a nav entry, or a new default. If a standalone row already exists, the same disabled-only delete applies. Delete eligibility does not depend on `IntegrationId`.

### R8. Archive stays "nothing live"

The refined rule is the product rule. Schedule delete is not required to archive, and connection retention is not a compromise that LFC-1 must undo. See section 10.

### R9. `schedule.manage` authorizes delete

Integration hard-delete uses `integration.delete` because that family already splits create, read, update, and delete, and retire goes through `integration.update`. Schedules have one mutation permission, and notification-definition delete already uses its manage permission for a destructive operation. Adding `schedule.delete` would be a new catalog entry for a family that has never been split. Connection `connection.delete` stays the retire permission and is not reused for schedules.

### R10. Secret deactivation on retire is deferred

Retire leaves secret material Active and referenced. Runtime will not resolve it, because the connection is not Active. Disabling it would destroy credentials on an operation this gate keeps as terminal retention, and `ConnectionSecretMaterialProvisioner` has no safe disable helper today. That is a security-hygiene change to retire, not the lifecycle question LFC-1 has to close. It stays deferred so a later gate can decide it with the secret-store failure semantics in view.

### R11. Bindings stay until an operator removes them

Retire already refuses new bindings and already stops resolution. Automatically revoking bindings would hide slots the operator still has to rebind, and would be a silent cascade. The operator removes or retargets a binding on the Integration, which is the existing binding API.

### R12. No fabricated fire-time configuration

`ScheduleRuns` and run idempotency keys store schedule id and fire time. They do not store name, cron, timezone, or target JSON. Copying the current definition onto old runs at delete time would present the last configuration as if it were the configuration that fired. That is less trustworthy than a dangling id. LFC-1 does not add snapshot columns.

---

## 4. Connection lifecycle contract

No new operation.

| From | Operation | To | LFC-1 |
|---|---|---|---|
| — | Create | Active | Unchanged |
| Active | Update, rotate secret, verify, move | Active | Unchanged |
| Active | Retire (`connection.delete`, `DELETE /api/connections/{id}`) | Retired | Unchanged |
| Retired | Update via API | Retired | Unchanged at the service. The workspace stays read-only. |
| Retired | Un-retire | — | **REJECT** |
| Retired | Hard-delete | — | **REJECT** |
| Retired | Move | Retired | Unchanged |

Terminal state is Retired for as long as the row exists. An archived Application may own retired connections. Inventory continues to list them with status Retired. Secret refs stay on the row. Binding rows stay. Resolution and readiness keep treating the connection as not Active.

The one Connection change in this epic is copy, not lifecycle: the retired notice and the retire confirm dialog must describe the runtime rule (section 15).

---

## 5. Schedule lifecycle contract

| From | Operation | To | LFC-1 |
|---|---|---|---|
| — | Create under an Integration | Disabled or Enabled, as today | Unchanged. Standalone create stays rejected at the API. |
| Disabled | Update name / cron / timezone, staying disabled | Disabled | Unchanged, including on an archived context |
| Disabled | Enable | Enabled | Unchanged. Still gated. Still refused on an archived context. |
| Enabled | Disable | Disabled | Unchanged |
| Disabled | **Delete** | row removed | **New** |
| Enabled | Delete | Enabled | **Refused.** Disable first. |

**Preconditions, in order, after the caller is authorized:**

1. Acquire `IResourceOwnershipMutationGate` and re-read the definition inside that gate. This serializes with enable, which is the transition that makes a schedule archive-blocking. Delete does not call the reactivation guard. A disabled schedule on an archived Application may be deleted; that is cleanup, same as disable.
2. If the row is gone, stop. No audit.
3. If `Enabled` is true, write `schedule.delete_rejected` and refuse. Delete nothing.
4. In the same transaction, remove `ScheduleStates` for this schedule's assignments, remove those `ScheduleAssignments`, then remove the `ScheduleDefinitions` row.
5. Commit.
6. After commit, append `schedule.deleted`. A lost audit row is acceptable; refusing a completed delete to retry the audit is not. This matches `integration.deleted`.
   *Superseded (product-owner ruling, 2026-10-06; [AUD-1.0 design gate](../audit-hardening/aud1-design-gate.md) §3 OR-1, §13.6).* The delete and its `schedule.deleted` audit row will commit atomically in one transaction (AUD-1.4). If the audit row cannot be written, the delete rolls back, or the outcome is reported as unknown. The step 6 text above is kept as the historical ruling. The other steps are unchanged.

**Retained:** `ScheduleRuns`, `AuditLogs` rows that already cite `ScheduleId`, `PlatformEvents`, `NotificationInstances`, start-request idempotency keys, `OperationRuns` / workflow rows. None of these are rewritten.

**Not a blocker:** open or resolved attention incidents (no `ScheduleId` column). Enabled-only expectation means a disabled schedule is already outside that evaluation. In-flight dispatch that already claimed a fire may finish with the existing missing-definition outcome; new dues are not selected once the definition is disabled, and assignments are removed with the delete so a later poll cannot claim from them.

**Integration id.** Delete does not require the Integration to be retired. The point of the operation is to clear a schedule from an Integration the operator may still be using, or to clear the last schedule so Integration delete can proceed. Integration delete's schedule count is unchanged in code: after the schedule row is gone, that count drops. No special case is added to `DeleteIntegrationAsync`.

**Target immutability** on PATCH stays as it is. Delete removes the target with the definition. It does not version it.

---

## 6. Dependency / referential-integrity matrix

### 6.1 Schedule delete

| Dependent | Kind | Decision | Clearable by |
|---|---|---|---|
| `ScheduleDefinitions` row | The resource | Hard-delete when disabled | This operation |
| `ScheduleAssignments` | Current, no independent meaning | Remove in the same transaction | Automatic |
| `ScheduleStates` | Current cursor for those assignments | Remove in the same transaction | Automatic |
| Enabled flag | Preconditions | Block | `POST /api/schedules/{id}/disable` (exists) |
| `ScheduleRuns` | Historical firing | Retain, dangling `ScheduleId` allowed | — |
| `AuditLogs.ScheduleId` | Historical | Retain. New delete audits are additional rows. | — |
| `PlatformEvents.ScheduleId` | Historical | Retain | — |
| `NotificationInstances.ScheduleId` | Historical | Retain | — |
| Run idempotency `schedule:{id}:{fire}` | Historical provenance | Retain | — |
| `OperationRuns` and other CTX-2 execution tables | No `ScheduleId` column | Untouched | — |
| `AttentionIncidents` | No `ScheduleId` | Untouched | — |
| `Bindings` | No schedule reference | Untouched | — |
| Owning `Integrations` row | Parent | Untouched. Becomes deletable once its other blockers are also clear. | Existing Integration delete |
| `ResourceContexts` | FK from the schedule row outward | Schedule delete does not delete a context. Context delete stays restricted by remaining rows. | — |

There is no database FK from assignments, states, or runs back to `ScheduleDefinitions`. The transaction above is the integrity rule. A raw `DELETE` of only the definition row is not an acceptable implementation.

### 6.2 Connection retire (unchanged) and rejected hard-delete

| Dependent | On retire today | If the row were removed |
|---|---|---|
| `Bindings.ConnectionId` | Row kept. New binds excluded. Resolution null. Readiness `ConnectionInactive`. | Dangling id. Readiness becomes `ConnectionMissing`. **Do not remove the connection.** |
| `SecretRefs` + `platform-connection` secrets | Kept, still Active, still named for this connection | Only pointer dropped while secrets stay Active. **Do not remove the connection.** |
| `Connections.ResourceContextId` FK | Row remains a child of the context | Context FK would not block removing the connection. Irrelevant, because the row stays. |
| Runs | No `ConnectionId` column | Would not break run rows. Also not a reason to delete. |
| Audit details | `connection.retired` already records the id and display name | Further delete audit is unnecessary. |
| HSK-D / workspace | Retired row stays listed and openable | Would 404 the workspace that is the Service. |

### 6.3 What transfers from Integration delete

| Integration rule | Schedule delete | Connection |
|---|---|---|
| Refuse before any destructive write | **Transfers.** Enabled check and audit-rejection happen before assignment removal. | No delete. |
| Every blocker has a real remediation | **Transfers.** Disable already exists; this epic adds the missing delete. | Retire is the remediation archive already uses. |
| Cascade only dependents with no independent meaning | **Transfers** for assignments and schedule state. | Bindings are independently meaningful. **Does not transfer.** |
| Settings secret disable via `ResetAsync` | **Does not transfer.** Schedules own no secrets. | **Does not transfer.** Connection secrets stay referenced (R10). |
| Historical tables left dangling | **Transfers** for `ScheduleRuns` and `ScheduleId` correlations. | No delete, so no new dangling connection ids. |
| Post-commit success audit | **Transfers** as `schedule.deleted`. | Existing `connection.retired` remains the lifecycle audit. |
| Blocked attempt writes no audit | **Does not transfer.** See section 13. | — |
| Skip the ownership gate | **Does not transfer.** Enable must not race delete into removing an enabled schedule. | Retire stays ungated, as today. It only moves a connection out of the active count, so it cannot leave an archived Application owning a newly live connection. |
| Permission `*.delete` distinct from update | **Does not transfer.** `schedule.manage` (R9). | `connection.delete` already means retire. |
| UI confirm that lists automatic cleanup vs operator blockers | **Transfers** in schedule-specific words (section 15). | Retire confirm stays, with corrected binding copy. |

---

## 7. Secret handling

**Code fact.** Connection secret values are written only through `ConnectionSecretMaterialProvisioner` into `ISecretStore` (`provider = platform-connection`, name `connection:{connectionId}:{fieldKey}`). The connection stores the GUID in `SecretRefs`. Non-secret configuration stays in `Config`. Secret values are not returned on the workspace; the UI shows Set / Not set.

**Code fact.** `RetireConnectionAsync` does not call the secret store.

**Contract.**

- LFC-1 does not delete, disable, or rotate secrets.
- LFC-1 does not add `TryDisableOwnedSecretAsync` to the connection provisioner.
- Because the connection row remains, secret GUIDs stay reachable from the retired connection and nowhere else needs a new pointer.
- Schedule delete has no secret step. `ExecutionPolicyRef` and `TargetJobConfigurationJson` are definition fields removed with the row. They are not a secret store.

A later decision to disable material on retire (R10) must use an owned-name check as conservative as the platform-setting helper, must not disable an arbitrary GUID, and must define the two-store failure behavior. That work is outside these slices.

---

## 8. Binding handling

**Code fact.** `Binding` points at an optional `ConnectionId` and an optional `IntegrationId`. There is no FK. Shared-or-same-context is enforced at bind and at connection move. Capability bindings may have a connection and a null `IntegrationId`.

**Contract.**

- Retire does not create, update, or delete bindings.
- Delete of a connection does not exist, so bindings cannot be orphaned by LFC-1.
- Schedule delete does not read or write bindings.
- Shared-context rules are unchanged.
- The operator still removes a binding explicitly. That remains the way to clear the Integration delete binding blocker, which is a separate, already-shipped path.

The dishonest part is copy, not data: section 15.

---

## 9. Historical Run / provenance handling

**Code fact. What a run stores.**

| Family | Integration | Connection | Schedule | Context |
|---|---|---|---|---|
| `OperationRuns`, workflow instances, start requests, operation groups | `IntegrationId` string, no FK, no name snapshot | none | none on the row | `ResourceContextId` snapshot, no FK |
| Run forensic UI | link by `IntegrationId` | none | `ScheduleId` parsed from `schedule:{id}:{fireTimeUtc:O}` | live context directory for the label |
| `ScheduleRuns` | none | none | `ScheduleId` string, no FK, no name/cron | `ResourceContextId` snapshot at claim |

**Code fact.** Integration hard-delete already leaves dangling `IntegrationId` values. Run detail links to `/integrations/{id}` and `/schedules/{id}` as convenience. The execution record does not join current display names to reconstruct history.

**Contract for schedule delete.**

- Do not update `ScheduleRuns`, idempotency keys, `OperationRuns`, or context snapshots.
- After delete, a run whose forensic schedule id no longer resolves still shows that id and fire time. The schedule page may 404. That is the same honesty model as a deleted Integration.
- Do not backfill name, cron, timezone, or target onto historical rows (R12).
- `ScheduleRuns.ResourceContextId` remains the context at claim time even if the schedule's owning Application is later archived or the definition is gone.

**Contract for connections.** No run column cites `ConnectionId`. Retire does not rewrite runs. LFC-1 adds no connection snapshot.

This is enough to keep execution evidence trustworthy: outcome, timestamps, run id, integration id, schedule id, fire instant, and context snapshot survive. What does not survive schedule delete is the last definition content. The gate accepts that loss. It does not pretend the history still contains it.

---

## 10. Application archive interaction

**Ruling R8.** `BlocksArchive` stays `LiveIntegrations + ActiveConnections + EnabledSchedules > 0`.

Schedule delete does not change that predicate. An enabled schedule still blocks archive until it is disabled. A disabled schedule that the operator never deletes still does not block archive, which is the D2 rule. A deleted schedule is simply absent from the counts.

Connection retire continues to be what removes a connection from the active count. Retired connections on an archived Application stay correct.

Implementation may update the comment on `ResourceContextOwnershipSummary` that says disabled schedules can never be deleted, so the comment matches the new operation. It must not change the predicate or the archive error text. That text already tells the operator to disable enabled schedules; delete is optional cleanup, not an archive requirement.

No archive/move race is introduced. Delete of a disabled schedule does not flip `BlocksArchive`. Holding the ownership gate still matters so a concurrent enable cannot commit and then have its row removed.

---

## 11. ResourceContext interaction

Delete eligibility does not depend on Shared versus Application, and does not depend on whether the schedule is attached or standalone.

- Attached schedule: context remains the Integration's context until delete. Delete does not move the Integration or sibling schedules.
- Standalone schedule: same disabled-only delete. Move of a standalone schedule stays a separate operation.
- Archived owning context: delete of a disabled schedule is allowed. Enable stays forbidden until unarchive.
- Shared: same rule. Shared itself is not archived by this epic.
- Connection retire and connection move: unchanged. A retired connection may still be moved, including out of an archived Application, under the existing move rules.

No nested contexts, grouping, or domain membership are introduced. `/connections` and `/connections/{id}` stay the workspace identity.

---

## 12. Authorization

| Action | Permission | Notes |
|---|---|---|
| Schedule delete | `schedule.manage` at instance scope, via the existing schedule governance check | Same check as enable, disable, update, and schedule move. |
| Connection retire | `connection.delete` | Unchanged. Means retire. |
| Connection hard-delete | none | Not a product operation. |
| Integration delete | `integration.delete` | Unchanged. Becomes reachable once schedules are gone and the other blockers are clear. |

Do not add `schedule.delete`. Do not retarget `connection.delete`. Do not require `resource_context.manage` for schedule delete; schedule move already established that context managers are not the schedule authority.

Unauthorized callers receive the same forbid path as the other schedule mutations, and no delete-rejection audit (section 13).

---

## 13. Audit requirements

Schedule definition create, update, enable, and disable do not audit today. Dispatch and ownership move do. LFC-1 does not backfill audits for the non-destructive operations.

| Event | When | Details |
|---|---|---|
| `schedule.deleted` | After the delete transaction commits | Actor, schedule id, display name, owning `ResourceContextId`, whether an `IntegrationId` was set, and that assignments were removed. No cron secret-like payload beyond the name already shown in the product. No target JSON. |
| `schedule.delete_rejected` | After authorization, when the re-read shows `Enabled == true` | Actor, schedule id, display name, owning context, reason `enabled`. No rows removed. |

Not found and unauthorized attempts do not write these actions. There is no resource decision to record for a missing id, and authorization failure is not a lifecycle outcome.

This is intentionally stricter than Integration delete, which does not audit a blocked attempt. Schedule mutations otherwise leave no definition-level audit, and a refused delete must be visible without inferring it from the row still existing.

Connection retire continues to write `connection.retired`. No `connection.deleted` action. No secret-cleanup audit, because no secret cleanup runs.

Retained historical audits are not rewritten when a schedule is deleted. Their `ScheduleId` may dangle, which is the evidence that the schedule once existed.

---

## 14. API requirements

**New:** `DELETE /api/schedules/{id}`

- Authorized with `schedule.manage`.
- Success: `204 No Content`.
- Enabled: `409 Conflict` with a stable error body, for example `{ "error": "Cannot delete schedule: it is still enabled. Disable it first." }`.
- Missing: `404 Not Found`, matching enable/disable.
- Unauthorized: `403`.
- Archived context: not a conflict. Disabled delete succeeds.

Do not add a second delete route under `/api/integrations/{integrationId}/schedules`. One id is one operation. The integration page links to the schedule it already lists.

**Unchanged:** `DELETE /api/connections/{id}` retires and returns `204`. Clients that call it, including `RetireConnectionAsync`, keep that meaning.

**Unchanged:** `POST /api/schedules` still rejects a missing `IntegrationId`.

No request body on schedule delete.

---

## 15. UI requirements

Operator-facing strings must not contain epic ids, track names, or planning labels.

### 15.1 Schedule detail

- Show **Delete** when the caller can manage schedules and the schedule is disabled.
- When the schedule is enabled, do not offer Delete as a destructive confirm. Show that the schedule has to be disabled first. Disable remains the existing action.
- Confirm copy, in product language:
  - Title: `Delete schedule?`
  - Body: this removes the schedule. Past firings and the runs they started stay in history. Opening this schedule from those records will no longer be available.
  - Confirm: `Delete schedule`
- On success, leave the detail page for the schedules list (the same shape as integration delete returning to `/integrations`).
- Do not add a standalone-schedule creation flow. Do not add delete as a new list-only gesture in this epic; detail is the confirmation surface. A list affordance may call the same API later and is not required to close LFC-1.

### 15.2 Connection workspace copy

Replace the retired notice and the matching retire-confirm sentence so they match resolution and readiness:

> This connection is retired. It is read-only here and no longer eligible for new bindings. Existing bindings stay in place and do not resolve until they are changed.

Do not add a delete control. Do not change retire's permission, route, or success navigation.

### 15.3 Inventory

HSK-D already lists schedules in either enabled state and connections in either state. A deleted schedule disappears because the definition query no longer returns it. No new inventory endpoint, state filter, or Service row is in scope. State filtering stays deferred, as HSK-D left it.

---

## 16. Failure / status semantics

| Situation | HTTP | Persisted effect | Audit |
|---|---|---|---|
| Disabled schedule, authorized | 204 | Definition, assignments, and states gone. Runs remain. | `schedule.deleted` |
| Enabled schedule | 409 | None | `schedule.delete_rejected` |
| Unknown id | 404 | None | None |
| No `schedule.manage` | 403 | None | None |
| Concurrent enable wins the gate | 409 | None, if the re-read sees Enabled | `schedule.delete_rejected` |
| Concurrent delete wins the gate, then enable | Enable's existing not-found path | Definition already gone | `schedule.deleted` only |
| Integration delete while a schedule row remains | Existing 400 from Integration delete | None | None (existing Integration rule) |
| Integration delete after the schedule is gone and other blockers are clear | Existing 204 | Integration delete's own contract | `integration.deleted` |

Schedule delete does not return 400 for the enabled case. Enable-on-archived-context already uses 409 for a state conflict; enabled-vs-delete is the same kind of conflict.

Partial cleanup is a defect. Assignments, states, and the definition commit together. Historical tables are outside that transaction on purpose.

---

## 17. Test strategy

No product test suite is required to close LFC-1.0. Later slices prove the contract.

**Schedule delete**

- Enabled schedule: 409, definition and assignments remain, one `schedule.delete_rejected`, zero `schedule.deleted`.
- Disabled schedule: definition, assignments, and their states are gone; `ScheduleRuns`, a pre-existing audit row, a platform event, and a notification instance with that `ScheduleId` remain; one `schedule.deleted`.
- Disabled schedule on an archived Application: delete succeeds.
- Enable on an archived Application: still 409 (regression).
- Missing id: 404, no audit.
- Caller without `schedule.manage`: 403, no audit.
- After deleting the only schedule, and with no other Integration blockers, `DeleteIntegrationAsync` succeeds. With the schedule still present, it still fails with the existing schedule message.
- `POST /api/schedules` without `IntegrationId` still fails.
- Archive of a context whose only schedule is disabled still succeeds whether or not that schedule is then deleted. Archive of a context with an enabled schedule still fails.
- A test locks the ownership-gate participation: delete re-reads inside the gate (same class of regression as the enable path's fresh read). A full SQL interleaving test is required only if the slice touches the gate's lock ordering; do not invent a second lock.

**Connection copy**

- Source or presentation test: retired notice and retire confirm state that existing bindings stay and do not resolve.
- Retire still sets `Retired`, still writes `connection.retired`, still leaves secrets and bindings in place.
- `ConnectionState` remains the two-value enum.

**Out of slice tests.** Do not add connection hard-delete tests. Do not change `BlocksArchive` expectations except where a comment assertion would claim schedules can never be deleted.

---

## 18. Migration / data implications

No schema migration.

Schedule delete removes rows that already exist. It does not add a deleted flag, a tombstone table, or snapshot columns. Existing disabled schedules become eligible once the API ships; nothing is deleted by the migration that does not exist. Existing enabled schedules stay until an operator disables them.

Connection rows, secret descriptors, and bindings are not migrated.

Audit actions are new string constants written as ordinary audit rows. They do not need a catalog migration beyond whatever the codebase already does for `ActionConstants` consumers. If a permission seeder snapshot test enumerates schedule actions, update it to include the two new audit names and to keep the permission list unchanged (`schedule.manage` only).

The ownership-summary comment update is code, not data.

---

## 19. Deferred work

| Item | Why it waits |
|---|---|
| Disable or delete connection secret material on retire (R10) | Changes retire and credential lifetime. Needs its own secret-store failure contract. |
| Connection un-retire | A new live transition, including the reactivation guard. Not required to finish archive or Integration delete. |
| Connection hard-delete | Rejected for this epic. Reopen only if retired retention itself becomes the operator problem. |
| Backfill of schedule create/update/enable/disable audits | Useful, separate from the destructive-operation audit. |
| Per-fire schedule configuration snapshots | Would be new provenance, not a repair. Easy to fake from the last definition. |
| HSK-D state filters | Already deferred. Lifecycles still differ. |
| Standalone schedule authoring UX | Explicitly out of scope (R7). |
| IAM-8, navigation / cross-context grouping | Not this epic. Roadmap order stands. |

---

## 20. Explicit non-goals

- Implementing delete, copy, or archive changes in LFC-1.0.
- Redesigning Application archive, movement, or ResourceContext archive-only rules.
- Changing Integration delete's blocker list, secret cleanup, or audit, except that operators can now clear the schedule blocker by deleting disabled schedules.
- Cascading Integration delete into schedules. The operator deletes schedules first, on purpose.
- Revoking bindings on retire, or deleting secrets on retire.
- A `/services` route, a Service entity, or any grouping / domain model.
- Occupying `DELETE /api/connections/{id}` with a different meaning.
- Exposing standalone schedule creation.
- Query-console, connector tool, or `connection.browse` work.
- Rewriting historical runs, schedule runs, or audits.

---

## 21. Implementation slice plan

Smallest sequence that follows the rulings. Do not start it until this gate is accepted.

### LFC-1.1 — Schedule delete

Service, repository removal of assignments and states, gate participation, audits, `DELETE /api/schedules/{id}`, and the tests in section 17 except the UI copy tests. Include the Integration-delete reachability test here; it is the acceptance of the schedule operation, not a change to Integration delete.

Update the ownership-summary comment in this slice so it no longer claims schedules cannot be deleted. Leave the predicate alone.

### LFC-1.2 — Schedule delete UI

Schedule detail confirm dialog and the enabled/disabled affordance in section 15.1. No new create flow. No list redesign.

### LFC-1.3 — Retired connection copy

Presentation and retire-confirm copy in section 15.2, plus the tests that lock it. No API change.

**Not scheduled:** an archive slice, a connection-delete slice, an inventory slice, a secret-disable slice. Archive and inventory stay correct without them. Connection lifecycle is the decision to leave retire as terminal, plus the copy fix.

LFC-1.1 is first because it is the smaller, clearer capability and it is the one that makes an existing Integration operation reachable.

---

## 22. Acceptance criteria

**LFC-1.0 (this document)**

- The rulings in section 3 are recorded.
- No product source, test, or migration differs from the audited HEAD except documentation and roadmap status.
- Roadmap and the transitions index say LFC-1 has started, the design gate is written, and implementation has not started.

**LFC-1.1**

- A disabled schedule can be deleted by `schedule.manage`.
- An enabled schedule cannot.
- Assignments and states go away in the same transaction; schedule runs and run forensic ids remain.
- An Integration whose only remaining delete blocker was that schedule can be deleted through the existing Integration API after the schedule is gone.
- Archive predicates and archive messages are unchanged.
- `POST /api/schedules` still requires an Integration.

**LFC-1.2**

- An operator with schedule management can delete a disabled schedule from its detail page after an explicit confirm, and cannot confirm-delete an enabled one.

**LFC-1.3**

- Retired connection copy matches `SlotBoundResolver` and readiness: bindings remain and do not resolve.
- Retire behavior is otherwise unchanged.

**Epic complete** when 1.1–1.3 meet those criteria and no connection hard-delete or un-retire path exists.

**Closure (2026-09-29).** Those criteria are met. See §27.

---

## 23. STOP conditions

Each stop from the LFC-1.0 brief was checked. None fired. The gate is allowed to decide.

| Condition | Result |
|---|---|
| Historical run integrity cannot survive the proposed deletion | **Not hit.** Runs do not FK to schedules or connections. `ScheduleRuns` and idempotency keys keep the schedule id and fire time. Context snapshots stay. The gate refuses a fake per-fire cron. Connection rows are not deleted, so connection provenance is unchanged. |
| Secret ownership is ambiguous | **Not hit.** Connection secrets are identified by `SecretRefs` plus `connection:{id}:{field}` and provider `platform-connection`. The safe response is to keep that pointer, which is what rejecting connection hard-delete does. |
| Bindings cannot be enumerated | **Not hit.** `GetByConnectionIdAsync` lists them. They have no FK, which is why delete-by-cascade would be unsafe, and why this gate does not delete connections. |
| Schema cleanup would be destructive in an undocumented way | **Not hit for the approved operation.** Schedule history has no FK, so the contract has to say what is removed in code. That is specified in section 6. Connection row removal would orphan Active secrets and bindings; that is why it is rejected, not why the epic stops. |
| Connection retire was a deliberate permanent-retention decision that forbids this design | **Not hit as a stop.** D2 deferred delete and separately accepted retired rows on archived Applications. This gate now records Retired as terminal. That is the decision, not an external prohibition on deciding. |
| Schedule identity is immutable provenance that requires the definition row | **Not hit.** The durable identity is the schedule id string on firings and idempotency keys. The definition row is current configuration. |
| Another unfinished epic is required first | **Not hit.** Integration delete, archive, movement, and the connection workspace are already shipped. IAM-8 and navigation grouping are not prerequisites. |

If implementation discovers a current-state `ScheduleId` reference that section 6 does not list, stop and extend the matrix before deleting rows. Do not cascade it silently.

---

## 24. LFC-1.1 implementation notes

Written with the schedule hard-delete implementation. Product rulings in §3 are unchanged.

**Atomicity.** `DeleteScheduleAsync` checks the actor first (no gate, no audit when there is no actor). It then opens a transaction on a relational provider, acquires `IResourceOwnershipMutationGate`, and re-reads the definition with `GetByIdForUpdateAsync` (`UPDLOCK, ROWLOCK` on SQL Server). Enabled refuses. Disabled deletes schedule-state rows, assignments, then the definition in that same transaction. There is no foreign key from assignments, states, or runs onto `ScheduleDefinitions`; the explicit delete order is the integrity rule. `ScheduleDefinitions.ResourceContextId` points at `ResourceContexts` with `ON DELETE RESTRICT` and does not cascade when the schedule row is removed.

**Enable race.** Enable takes the same ownership gate before it can set `Enabled`. A unit test flips the row to enabled inside `AcquireAsync` and asserts the delete then refuses and leaves the assignment in place. That is the deterministic stand-in for the SQL application lock. InMemory does not take `UPDLOCK`.

**Assignment insert.** `AssignToRuntimePlaneAsync` now takes the same definition row lock and fails if the definition is gone, so an insert cannot land underneath a delete that holds the lock, and cannot recreate an assignment after the delete commits.

**Cursor writes.** `DeleteDisabledScheduleGraphAsync` takes `UPDLOCK, ROWLOCK` on the schedule's assignment rows before it reads or removes their schedule-state rows. `UpdateStateAsync` takes the same lock on the assignment for the whole upsert, inside its own transaction when it is not already enlisted. Those locks conflict, so a cursor cannot commit after the delete has locked the assignment, and a cursor that committed earlier is included in the delete. When the assignment row is gone, `UpdateStateAsync` does not insert one. InMemory does not take `UPDLOCK`; the unit test proves the post-delete no-op. A claim that already passed due-evaluation can still insert a `ScheduleRun` for a deleted id; that row is history, and the due evaluator will not select the schedule again because the definition and assignment are gone.

**Audit.** `schedule.deleted` and `schedule.delete_rejected` are appended after the transaction completes, the same relationship as `integration.deleted`. Success-audit failure can surface after the rows are already gone. Rejection-audit failure happens while the schedule is still enabled. Unknown ids and missing actors write neither event. Schedule create, update, enable, and disable remain unaudited.

**Archive.** `BlocksArchive` is unchanged. A disabled schedule on an archived Application can be deleted. Enable on an archived Application still throws `ResourceContextArchivedException`.

**Not in this slice.** Schedule detail UI (LFC-1.2). Retired connection copy (LFC-1.3). No connection lifecycle change. No new permission. `DELETE /api/schedules/{id}` is an ordinary `/api` mutation and is not request-trust exempt.

**SQL Server proof.** `ScheduleDeleteSqlServerTests` runs the graph delete, enabled refusal, history retention, gate-held Enable/Delete interleavings, definition-row assignment conflict, assignment-row state-update conflict, and the FK/cascade inspection against a real throwaway catalog. InMemory tests remain the unit-level stand-in.

---

## 25. LFC-1.2 implementation notes

Written with the schedule detail delete UI. Product rulings in §3 and the §15.1 copy contract are unchanged. No schedule-delete service, repository, audit, permission, or connection-lifecycle change.

**Where.** Schedule detail only. The list is unchanged. There is no new create flow.

**Who sees Delete.** An operator with `schedule.manage` sees **Delete** only when the loaded schedule is disabled. The button opens the shared confirm dialog. An enabled schedule does not offer that button. The toolbar instead says "Disable the schedule before deleting it." Enable and Disable stay the existing actions. An operator who cannot manage schedules sees neither the button nor that prompt.

**Confirm.** Title `Delete schedule?`, confirm label `Delete schedule`, destructive emphasis, shared `ConfirmActionDialog`. The body says the named schedule is permanently removed and the action cannot be undone. Supporting text says current assignments and live scheduling state are removed, past firings and the runs they started stay in history, and opening this schedule from those records will no longer be available. Cancel sends no request.

**Success.** `DELETE /api/schedules/{id}` 204 shows the success toast "Schedule deleted." and navigates to `/schedules`. The page does not reload the deleted detail.

**409.** The client surfaces the API error ("Cannot delete schedule: it is still enabled. Disable it first."). The page reloads the detail, leaves it in place, and lets the dialog stay open with that error. After the reload shows Enabled, Delete is no longer offered. A later confirm while the dialog is still open does not call delete again.

**404.** The dialog closes. The page shows the existing "Schedule not found" empty state and a warning toast, "This schedule no longer exists." There is no success toast and no further confirm.

**403 and other failures.** The exception propagates into the shared dialog, which stays open and shows the client message. A 403 message is the status text and does not add the schedule id or name. The schedule stays on the page. Nothing is removed before the API succeeds.

**Validation.** `ScheduleDetailDeleteRenderTests` (10) render the real page and drive the same handlers the buttons call. `ManagementClientScheduleDeleteTests` (4) cover `DELETE /api/schedules/{id}`, 204, 409, 404, and 403. Retired connection copy is §26.

---

## 26. LFC-1.3 implementation notes

Written with the retired-connection copy correction. Product rulings in §3 are unchanged. No connection state transition, API, permission, persistence, secret, binding, archive, or schedule-delete change.

**What changed.** Operator copy only.

- The retired notice no longer says existing bindings keep working. It says the connection is retired and read-only, no longer available for active use, not eligible for new bindings, and that existing bindings may still be listed, stay in place, and do not resolve until they are changed.
- The retire confirmation says retirement is permanent and cannot be undone, the connection is not deleted, existing bindings stay recorded but will not be able to use it until they are changed, and history for the connection remains. The shared confirm dialog, the Retire label, and caution emphasis are unchanged.
- Readiness copy for a retired connection no longer tells the operator to activate or enable it. It says to bind a different connection or remove the binding, and that a retired connection cannot be brought back.

**Unchanged.** `DELETE /api/connections/{id}` still retires and returns 204. There is no un-retire and no connection hard-delete. The workspace offers neither Restore nor Delete connection. Secrets stay associated with the retained connection. Binding rows are not revoked or retargeted. Resolution and readiness still treat a non-Active connection as unusable. `BlocksArchive` is unchanged: an active connection blocks archive, and a retired connection does not. Schedule delete is untouched.

**Not in this slice.** Epic closure. §2.1 still records the pre-1.3 notice as the audited code fact; the supersession note there points here.

Planned implementation slices 1.1–1.3 are complete. Closing the epic is a separate audit.

**Supersession (closure).** The closure audit is §27. LFC-1 is complete and merge-ready.

---

## 27. Closure audit (2026-09-29)

Read-only reconciliation of HEAD `a1f883585ea5d9a7d2d83d1feb81ed7c2e66569f` against the §3 rulings and the §22 acceptance criteria. No product or test source was changed in this closeout.

**Ruling: complete and merge-ready.** Binding LFC-1 requirements are satisfied. IAM-8 and navigation / grouping were not started.

| Slice | Result |
|---|---|
| LFC-1.0 | Rulings R1–R12 remain the contract. |
| LFC-1.1 | `DELETE /api/schedules/{id}` hard-deletes a disabled schedule; enabled is 409; missing 404; unauthorized 403. Live graph (definition, assignments, states) is removed in one gated transaction. History is retained. Integration delete stays non-cascading and becomes reachable after the schedule row is gone. |
| LFC-1.2 | Disabled manageable schedules offer confirmed delete from detail. Enabled schedules must be disabled first. 204 leaves for `/schedules`. 409 / 404 / 403 match §25. |
| LFC-1.3 | Connection lifecycle is unchanged. Retired copy now matches resolver and readiness. No Restore or Delete Connection control. |

**Verified unchanged.** No connection hard-delete, un-retire, secret deactivation on retire, automatic binding revoke, per-fire snapshots, standalone schedule authoring, archive-predicate redesign, HSK-D state filters, `schedule.delete` permission, or request-trust exemption.

**Known non-blockers, not closure failures.**

- §17 asked a seeded `PlatformEvents` row to survive delete. The delete path does not touch that table; unit tests seed `ScheduleRuns`, idempotency keys, notification instances, and a prior audit row instead. Retention of platform events is by omission.
- `IResourceOwnershipMutationGate` remarks still say “eleven paths” and do not name `DeleteScheduleAsync`. The method does acquire the gate. Comment-only drift; not a behavior defect.
- `ResourceContextServiceTests.Archive_SucceedsWhenOnlyRetiredOrDisabledResourcesRemain` still comments that disabled schedules are “never deletable.” The archive predicate is unchanged.
- Gate-timeout HTTP mapping for delete matches enable: a completed concurrent enable is 409 `delete_rejected`; a lock wait timeout is not given a dedicated 409 mapper. Pre-existing enable pattern, not a new LFC-1 contract hole.
- No new ADR. This gate remains the lifecycle contract.

**CI.** Not required to close the epic. One post-close CI run is worthwhile only as the pre-merge checkpoint, not because a cross-project risk was found without local coverage.
