# IAM-10.2a slice 4: implementation record

| | |
|---|---|
| Package | IAM-10.2a, slice 4 of 4: the audited, catalog-driven repair of the Platform Administrator role ([IAM-10.0 design gate](iam10-design-gate.md) §3.1 P-5, P-6, P-7, P-20, P-30(d); A10.2.4, A10.2.12, A10.2.14, RT-8). |
| Branch | `feat/iam-10.2a-anti-escalation`, on top of the slice 3 correction. Not pushed, not merged. |
| Not in scope | IAM-10.2b (viability, the global `apperture:platform-administrator-set` lock), Recovery Access attribution, the legacy break-glass removal, IAM-10.3, IAM-10.4, context scope, removal of `relationship.manage`. |

This record states what was built and the choices the gate left open. Where it and the gate differ, this record says so under "Interpretations".

## What changed

| Area | Change |
|---|---|
| Repair plan | `PlatformAdminRolePermissionRepair.Plan(current, registry)` replaces the add-only merge. The target is what the role already holds, plus every registered permission whose definition is `GrantedToPlatformAdministratorByDefault` (by its own resource type and scope), plus the fixed grants, minus the obsolete list. It returns the target, the grants added and the grants removed, so the caller can tell a change from a no-op. `ApplyAllRepairs` is kept and returns the target. |
| Repair service | `PlatformAdminRoleRepairService` is now one audited unit of work (`IAuditedUnitOfWork`, system actor `system:administrator-repair`). It reads the role only through `LockAuthoritySnapshotAsync` (the lock set an assignment of that role takes), decides from the row as locked, updates the role and stages the summary row, or declares a no-op. |
| Audit | New action `governance.authorization.administrator_role_repaired` (governance, elevated, basis change, required mutation, target the role). Fields: `roleId`, `permissionCount`, `grantsAdded`, `grantsRemoved`, and the permission identities added and removed, bounded to 100 each. |
| Failure | A repair that cannot complete stops the host start. Stored permissions that cannot be read are never replaced: `PlatformAdminRoleRepairException` (`platform_administrator_role_permissions_unreadable`). |
| Guards | `RoleWriterCoverageGuardTests` keeps the repair in the exempt group and states why; the audit catalog tests count the new action; `AdministratorGrantableCatalogTests` now pins the reconciled result. |

## The repair, exactly

For the Platform Administrator role (`role-platform-administrator`), in one transaction:

1. Take the role row under `UPDLOCK, HOLDLOCK` (the second step of the slice 2 lock set, with no actor and no target user). An assignment of the administrator role takes the same row, so the two serialize in either order. A concurrent host start waits and then finds nothing to do.
2. If the role does not exist: nothing. If its stored permissions cannot be read: stop with the typed startup failure; nothing is written.
3. Plan from the locked row:
   - **Held.** An existing grant is kept unless it is obsolete. A grant counts as held when its action and resource type match ignoring case (as the evaluator reads them) and its scope is exactly equal. A grant at another scope is not a stand-in: the repair makes the stored set explicit.
   - **Added.** Every registered administrator default not held, with its own resource type and scope. Platform permissions are at instance scope; environment permissions (`environment.read`, `runtime.read`, `runtime.control.shutdown.request`, `runtime.enroll.invitation.create`) are at the real environment's scope (`environment:{id}`), never assumed `local` and never widened to instance; module permissions are at `module:{id}`. The fixed grants (the eight-combination floor, plus `workflow.execute` on WorkflowInstance at instance scope, the one stored grant that is deliberately not a catalog permission) are added whether or not the registry is complete.
   - **Removed.** Only the approved obsolete list: `user.read` on Role. It is matched at any scope, as the slice 3 data step matches it for custom roles. Every other held grant stays, including `relationship.manage` if the role holds it (its removal is IAM-10.5b).
4. If nothing is added or removed: declare a no-op. No `UPDATE`, no audit row, no `RowVersion` change. Stored duplicates alone are not a change.
5. Otherwise: write the target (duplicates collapsed) and stage the single summary row. The change and the row commit together; a failed audit insert rolls the repair back and fails the start.

`relationship.manage` is in the catalog and is not an administrator default, so the repair does not add it. It is the one permission the repaired administrator cannot grant under Rule 1.

## Startup order

Hosted services start in registration order. The Management Host registers, in this order: migrations (`AppertureDatabaseMigrator`, which ends with `RoleAuthorizationDataStep`), then in `AddAppertureGovernance`: `ModulePermissionRegistrationHostedService`, `PlatformAdminRoleRepairService`, `BootstrapPlatformAdminAssignmentHostedService`, `DevTestUserBootstrapHostedService`. The platform catalog is seeded when the registry singleton is first resolved, before any hosted service uses it. So the repair reads the complete catalog (platform, environment and every module permission), sees the migrated schema and a current audit table, and runs before the first administrator is assigned. A unit test pins the order. The data step skips system roles, so the two writers never touch the same row.

## Interpretations the gate left open

- **Unreadable administrator permissions stop the start.** Before this slice the repair read an unreadable list as empty and wrote a repaired set over it, discarding the stored data without a trace. The gate (P-1) says an unreadable administrator list must not lock out administration, which the floor still guarantees at run time, but it approves no recovery path for the repair to replace data it cannot interpret. The repair therefore refuses and the host does not start, with a message naming the role and the remedy (restore a readable value, then start again). **This is a behaviour change and a decision for the owner**: if a self-healing replacement is wanted, it needs its own approved, tested path (for example, a distinct audited event that records the discarded value's hash).
- **Obsolete grant scope.** The gate names `user.read` on Role without a scope. The slice 3 repair removed it at instance scope only; this slice removes it at any scope, which matches the data step and cannot remove anything that has a consumer.
- **One-direction convergence.** The repair never removes a held grant because it is not an administrator default or because its catalog entry is gone. Stale environment-scoped grants (an environment id that changed) therefore remain; removing them would need a rule the gate does not contain.
- **The `workflow.execute` exception** stays an ordinary stored grant added by the fixed list, not a catalog permission, exactly as the gate records.
- **No administrator-set lock.** The repair takes the role-row lock of P-5 only. The global lock belongs to IAM-10.2b, which will also decide whether the repair needs it (the repair never reduces administrators).
- **Fixed lists kept as a backstop.** The per-area lists in `PlatformAdminRolePermissionRepair` remain so the floor is stored even if the registry is empty. A unit test pins that each of them is a catalog administrator default or the named exception, so they cannot drift into broader grants.

## Evidence

All SQL cases run on a throwaway catalog with locking READ COMMITTED and on one with READ_COMMITTED_SNAPSHOT, fail (never skip) without SQL Server, and wait on the server's own lock list rather than on time.

| Criterion | Where | Result |
|---|---|---|
| **A10.2.4** stored grants, floor and catalog agree | `PlatformAdminRoleRepairPlanTests` (target from nothing is exactly the defaults plus the one named exception; environment scope is the real environment; module scope; `relationship.manage` absent; non-defaults kept; floor present even with an empty registry; every fixed grant is a default or the exception); `PlatformAdminRoleRepairSqlServerTests.A10_2_4_*` (stored set after a real repair, unrelated roles and assignments untouched, Rule 1 grants everything held and refuses `relationship.manage`) | pass |
| **The 13-permission discrepancy** | `AdministratorGrantableCatalogTests`: twelve of the thirteen are now held; `relationship.manage` is the one deliberate exception | pass |
| **A10.2.14** audited, one event, none for a no-op | `PlatformAdminRoleRepairServiceTests`; SQL `A10_2_14_*` (one row, exact counts and identities, no `UPDATE` and unchanged `RowVersion` on the second and third start); `Repeated_startup_through_the_migrator_and_the_repair_*` (no new rows after the first complete start) | pass |
| **Concurrent starts** | SQL `Two_hosts_starting_together_*`: the first host is parked holding the role row, the second is shown waiting, then released: one `Repaired`, one `NoChange`, one row. `A_change_made_to_the_role_while_the_repair_holds_it_*` | pass |
| **RT-8, both orders** | `RoleAuthoritySqlServerTests.RT8_the_startup_repair_waits_*` (assignment first: the repair waits; the assignment decided on the pre-repair role, `maxSensitivity` standard) and `RT8_an_assignment_..._waits_for_a_repair_in_flight_*` (repair first: the assignment waits and decides on the repaired role, `maxSensitivity` critical) | pass |
| **Audit insert failure** | SQL `An_audit_insert_failure_rolls_the_repair_back_and_fails_the_start`: role JSON and `RowVersion` unchanged, no row, the start throws | pass |
| **A10.2.12** retry and uncertain commit | SQL `M6_the_repair_keeps_the_audited_units_guarantees`: audit failure, lost acknowledgement (verified, not rerun), confirmed rollback (rerun once), unknown outcome committed and rolled back, operation lock not granted | pass |
| **Unreadable stored data** | unit and SQL: the repair throws `PlatformAdminRoleRepairException`, the stored value is byte-for-byte unchanged, no row; after the operator restores a readable value the next start repairs | pass |
| **Startup order and fail-the-start** | `PlatformAdminRoleRepairWiringTests`: registration order, migrations before governance, no swallowed failure, lock before decision before write before stage, no administrator-set lock | pass |
| **Host** | `RoleAuthorityHostHttpTests` (17) and `AuthorizationRemovalHostHttpTests` (7) start the real Management Host, so its hosted services run the audited repair through dependency injection. On the persistent Management Host test catalog (a database that predates the repair) the repair wrote exactly one row: 12 grants added, 0 removed, 57 stored, and the later host starts of the same run added none | pass |

**Mutation check.** The repair was broken four ways and the tests were run: an unlocked read of the role, no no-op branch, every registered permission added (including `relationship.manage`), and unreadable data overwritten. The SQL cases failed 14 of 32 (timeouts where a waiting assignment or host never waited, a second start that repaired again, extra audit rows, `relationship.manage` grantable, no exception on unreadable data) and the unit cases failed 10 of 34, each for the mutation it was written to catch. The production files were then restored byte-for-byte.

**Suites run** (isolated build output, restored production files): full unit suite 5210 passed; architecture suite 38 passed; SQL Server: repair, role authority, data step and Management Host authorization 185 passed; audit recorder, integrity guard, identity governance audit, classification, append bridge and timeline 72 passed.

**Not run.** The full integration suite (about two hours), the browser tests, and the Ui.Client and Ui.Shared suites: this slice changes no capability flag, DTO, route or page, and nothing in those projects references the repair or the audit catalog. `IntegrationTests` outside the classes above were not run.

## Correction before review: the data step uses the same obsolete-grant rule

Owner rulings on this slice: the obsolete `user.read` on Role is removed at any scope (approved); unreadable administrator permissions fail the start and are preserved (approved); historical environment-scoped grants are preserved for now; the administrator-default Critical permissions and the repair summary's static Elevated severity are approved; whether the repair needs a deadlock-rerun test is left to the independent review.

`RoleAuthorizationDataStep` already matched `user.read` on Role by action and resource type at any scope, from readable custom roles, leaving `user.read` on User, null-resource grants, unreadable roles and system roles alone. One difference from the repair remained in its summary: an identical obsolete grant stored twice was counted and listed twice. It is now one removal in `grantsRemoved` and in the `changes` list (every copy is still removed), as in the repair. Tests added: a scope matrix, mixed-scope roles, duplicates, broad grants, unreadable and system roles, idempotency of the plan, and, against SQL Server on both isolation modes, the stored roles, the exact summary counts and entries, untouched `RowVersion` for a role with nothing obsolete, and a second run that changes and writes nothing.

## Independent review remediation

The independent security review returned *safe after bounded remediation* with no Critical or High findings.

**M-1 (Medium), fixed.** `AssignRoleAsync` and `RemoveRoleAsync` loaded and locked the target role but then compared and recorded the caller's spelling of its id. On a case-insensitive catalog an id such as `ROLE-PLATFORM-ADMINISTRATOR` finds the administrator row, passes the entitlement check (which already ignored case), and then skipped the last-administrator guard (an ordinal comparison against the exact id), so the last administrator's role could be removed. Every decision, repository call and audit row after the lock now uses the id of the locked row (`role.Id`) and `RoleAuthorityRules.IsPlatformAdministratorRole`: the Recovery Access removal check, the last-administrator count and refusal, the stored assignment, the removal, and the assignment, removal and refusal audit events. The same mismatch was removed from the adjacent places that decide on an administrator assignment or record a refusal: the two user-level guards that read `AssignedRoleIds` (last local link, last local credential) and the provisioning refusals of the initial role. Rule 2, the authorization-before-no-op order, the typed refusals and the audit contracts are unchanged, and no viability design or administrator-set lock was added. An assignment row written before this fix by a case-variant request (possible on a case-insensitive catalog) is not migrated; the guards that read stored assignment ids now compare ignoring case, so such a row is still recognised as the administrator role.

**L-3 (Low), documented.** `docs/04-api/contracts.md` now separates two effects in the upgrade-window statement: the evaluator is deny-only relative to the old one (pinned by the upgrade-window tests, unchanged), and the approved governance-read switch makes the governance surfaces visible to holders of `user.read` on User from the first request.

**Deferred observations (recorded, not implemented).**

| # | Observation | Owner |
|---|---|---|
| L-1 | Credential-route audit exception mapping | IAM-10.2b |
| L-2 | Semantics of a broad legacy grant (no resource) when an action appears on several resource types | open |
| L-4 | Provisioning actor resolution | IAM-11 |
| - | The Entra provisioning-directory endpoint's relationship to `auth.directory.search`; its permission is not switched without a separate owner decision | owner |
| - | Role replace-permissions, rename and delete still name the role in their audit rows by the caller's spelling (the target is a custom role, never the administrator role, and the decision is not affected) | open |
