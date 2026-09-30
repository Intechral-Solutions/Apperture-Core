# IAM-8 — Custom role lifecycle — design gate

Date: 2026-09-29
Status: **Complete and merge-ready (2026-09-30).** IAM-8.0–IAM-8.4 satisfy the binding rulings. Closure audit is §20. Not merged. This closeout does not select a next epic. Product rulings in §2 are unchanged.
Branch: `iam-8.5-role-lifecycle-closure`
Implementation HEAD at closure: `cb60c7b7282543061a9e87e439db5743672aac5a`
Scope: complete create, metadata edit, and delete for the existing instance-scoped role model. Do not redesign authorization evaluation.

This gate is the lifecycle contract. Product rulings below are binding. §1 records the hole at the 2026-09-29 gate. Later slices closed it. §20 is the closure audit. Those rulings do not reopen Context-scoped IAM, permission evaluation, or cross-context grouping.

Labels:

- **Code fact** — verified in the repository when this gate was written.
- **Ruling** — product decision for IAM-8.
- **Contract** — what implementation slices must build.

---

## 1. Current-state problem

Operators can list roles, open a role, replace the permission set of a non-system role, and assign or remove roles on users. They cannot create a role or delete one. A system role's grants are hidden from editing in the UI, and the permission-replace service does not check `IsSystemRole`.

**Code facts.**

| Fact | Where |
|---|---|
| Role is a named permission set with `IsSystemRole` | `Role` in `src/Apperture.Core/Apperture.Core.Domain/Identity/Role.cs` |
| Rows live in `Roles` (`Id` nvarchar(64), `Name` nvarchar(256), `Description` nvarchar(512), `PermissionsJson`, `IsSystemRole`, `RowVersion`) | `RoleEntity`; migration `AddPlatformIdentityTables`; `RowVersion` added in `AddPlatformIdentityReferentialIntegrity` |
| Unique name | Unique index `IX_Roles_Name` on `Roles.Name` |
| Assignments | `UserRoles` (`UserId`, `RoleId`). FK `FK_UserRoles_Roles_RoleId` is `ON DELETE RESTRICT` |
| Reads and grant replace | `RolesController` GET list/detail/permissions, PUT `{id}/permissions`; `UserManagementService.SetRolePermissionsAsync` |
| Grant replace authorization | `permission.assign` on `Role` at instance scope (`RequirePermissionAssignAsync`) |
| Grant replace audit | `governance.permission.assignment.updated` |
| UI edit affordance | `RoleDetail.razor` shows **Edit permissions** only when the role is not a system role and `CanAssignPermissions` |
| Assignment | `UsersController` assign/remove; `role.assign`; audits `governance.role.assignment.created` / `governance.role.assignment.removed` |
| Seeded system role | `role-platform-administrator`, name `Platform Administrator`, `IsSystemRole = true` (`BootstrapConstants.PlatformAdminRoleId`) |
| Last platform administrator | `RemoveRoleAsync` refuses removal of that role id when `CountUserAssignmentsAsync` is 1 or less, and audits `auth_admin.blocked.last_platform_admin_removal` |
| Evaluation | `RbacAuthorizationPolicyEvaluator` loads whatever roles are assigned and unions their permission descriptors at instance scope. Holding `role-platform-administrator` also unlocks a fixed governance surface if permission JSON is empty. Instance-scoped `role.manage` implies that same governance surface, including `permission.assign` and `role.assign`. It does not imply a new permission. |
| No create or delete role API | `IRoleRepository` has get, list, assign, unassign, count, and `UpdatePermissionsAsync` only |
| No other persisted role pointer | Provisioning and access-request approval pass `InitialRoleId` on the request. They do not store a default role row. Audit details store role id strings. `AttentionRoutingRecipientRoles` is not this role model. |

The product hole is lifecycle of the existing aggregate, not a new authorization model.

---

## 2. Binding rulings

| Id | Ruling |
|---|---|
| R1 | A user-created role may be hard-deleted only when it has zero current user assignments. Delete does not revoke assignments. An administrator removes or reassigns users first. |
| R2 | The UI shows current assignments (count and who holds the role) before a destructive delete, including when delete is blocked. |
| R3 | For a user-created role, the internal id is immutable. Display name, description, and permission grants are editable. `IsSystemRole` is immutable and is never client-supplied. |
| R4 | System roles cannot be deleted. Their name, description, and permission grants cannot be edited. `IsSystemRole` cannot be changed. These refusals are enforced in the service, not only in the UI. |
| R5 | There is no archive, retire, tombstone, or undelete. A deleted role is a missing row. |
| R6 | Authorization evaluation, scope model, and permission catalog rules stay as they are. No ResourceContext-scoped grants. |
| R7 | Create, metadata update, and delete use existing `role.manage`. Grant replacement keeps using `permission.assign`. No new permission constants. |
| R8 | Cross-context grouping, departments, and domains are out of scope. ADR-0040's `/connections` rail rule is unchanged and unrelated. |
| R9 | The seeded platform-administrator role stays a system role. Deleting it is impossible under R4. Last-administrator protection stays on **user assignment removal**, which is a different mechanism. |

---

## 3. Role identity rules

**Id.** Implementation generates the id. The client does not choose it. The id is opaque, stable, and immutable. Shape: `role-` plus 32 lowercase hex characters (a Guid in `N` format). That fits `nvarchar(64)`. It must not equal `BootstrapConstants.PlatformAdminRoleId`. Display name is not part of the id, so rename does not change identity. Existing seeded id `role-platform-administrator` remains valid and is not migrated.

**Name.** Required. Trim whitespace. After trim, length is 1 through 256 characters (the `Roles.Name` column). Uniqueness is the existing unique index `IX_Roles_Name`. A create or rename that collides returns conflict, not a 500. SQL Server uniqueness follows the column's collation; this epic does not add a second normalized name column and does not change collation. Names that differ only by case are duplicates when the database treats them as equal. System role names stay reserved because those rows cannot be renamed and the unique index rejects a second row.

**Description.** Optional. Trim. Empty becomes null. Maximum 512 characters (`Roles.Description`).

**IsSystemRole.** False for every role this epic creates. Never accepted on create or update.

---

## 4. Lifecycle contract

A role is either a live row or absent.

| Kind | Create | Metadata edit | Grant replace | Delete |
|---|---|---|---|---|
| System role (`IsSystemRole`) | Not created by this epic | Refuse | Refuse | Refuse |
| User-created role, zero assignments | — | Allow | Allow | Hard-delete the `Roles` row |
| User-created role, one or more assignments | — | Allow | Allow | Refuse. Assignments stay. |
| Missing id | — | 404 | 404 (same as today when the role is missing) | 404 |

No status column. No tombstone. Historical audit rows that mention a role id are not rewritten and are not foreign keys. After delete, a later assign or provision that cites the id fails because the role is not found. That is the existing not-found behavior, not a new dangling-assignment state: delete is refused while any `UserRoles` row points at the role, and the restrict foreign key is the database backstop.

Do not copy Connection retire or Schedule soft-delete. A role does not own secrets, bindings, or firing history.

---

## 5. Create, update, and delete semantics

### 5.1 Create

One operation creates the row and, when the caller sends grants, writes the initial permission set in the same transaction.

**Why one operation.** An empty role followed by a separate grant call leaves a named role behind when the grant call fails, and it splits one administrator action across two permissions by accident. Atomic create still allows an empty grant list so a caller who may manage roles can create a role before anyone holds `permission.assign` as a distinct grant. The platform administrator already passes both checks (`role.manage` is seeded, and instance `role.manage` implies `permission.assign`).

**Authorization.** `role.manage` on `Role` at instance scope. If `permissions` is non-empty, the same call also requires `permission.assign` on `Role` at instance scope (the existing evaluator implication still applies; this gate does not remove it).

**Body.** `name` (required), `description` (optional), `permissions` (optional; omitted means empty). No id. No `isSystemRole`.

**Validation.** Identity rules in §3. Each permission must already be in the canonical catalog, using the same checks as `SetRolePermissionsAsync`. No new catalog entries. No context-scoped descriptors.

**Duplicate name.** Conflict. Do not mutate the existing row.

**Audit.** On success, `governance.role.created` with the new role id, name, and permission count. No audit when the caller is unauthorized. No audit when validation fails before a row exists.

**Concurrency.** Insert is one transaction. Unique index settles two creates of the same name.

### 5.2 Metadata update

Separate from grant replacement.

**Authorization.** `role.manage` on `Role` at instance scope.

**Body.** `name` and `description` only. Omitted description clears to null when the field is present as empty. This gate uses a dedicated metadata operation so a name edit cannot silently replace grants.

**System role.** Refuse before any write.

**Missing role.** Not found. No audit.

**Name collision** with a different role. Conflict. The row is unchanged.

**Audit.** On success, `governance.role.metadata.updated` with role id and the new name. On a system-role refusal by an authorized caller, `governance.role.mutation_rejected` with reason `system_role`.

### 5.3 Permission replacement

Keep `PUT /api/roles/{id}/permissions` and `SetRolePermissionsAsync`, including catalog validation and `governance.permission.assignment.updated`.

**Add.** If the role is missing, not found (already). If `IsSystemRole` is true, refuse before `UpdatePermissionsAsync`, and when the caller was authorized, audit `governance.role.mutation_rejected` with reason `system_role`. Do not write `governance.permission.assignment.updated` for that refusal.

Grant shape stays instance-scoped catalog entries. This epic does not version grants and does not diff them. Replacement remains whole-set replacement, which the role detail UI already tells the operator.

### 5.4 Delete

**Authorization.** `role.manage` on `Role` at instance scope.

**Order, inside one transaction.**

1. Load the role. If it is missing, stop. No audit.
2. If `IsSystemRole` is true, audit `governance.role.mutation_rejected` with reason `system_role` after the authorization check, and refuse. Delete nothing.
3. Count `UserRoles` for that role id under the same transaction, with locks that block a concurrent insert (see §12). If the count is not zero, audit `governance.role.mutation_rejected` with reason `assignments_remain` and the count, and refuse. Delete nothing. Do not delete `UserRoles` rows.
4. Delete the `Roles` row. Commit.
5. After commit, audit `governance.role.deleted` with role id and name. A lost audit after a committed delete is acceptable. Retrying the delete must not be required to repair the audit. This matches other post-commit governance audits in this codebase.

**Unauthorized caller.** Forbid. No mutation audit.

**Idempotency.** A second delete of a gone id is not found, not success. There is no undelete.

---

## 6. Assignment integrity

Current assignments are `UserRoles` rows. `CountUserAssignmentsAsync` and `ListUserIdsWithRoleAsync` already exist on `IRoleRepository`.

Delete is legal only at count zero. The service must not call `RemoveFromUserAsync` as part of delete.

The restrict foreign key remains. It is not a substitute for the counted refusal: a restrict violation would surface as a failed delete instead of an explained refusal. The transaction in §5.4 is the product rule. The foreign key is the backstop if a lock is wrong.

Showing assignments is a read. It does not change assignment. Removing an assignment stays `role.assign` on the existing user-role endpoints, including the last platform-administrator refusal. IAM-8 does not add bulk unassign.

---

## 7. System-role invariants

`IsSystemRole == true` is the rule. The seeded platform-administrator role is the known row. Any future seeded system role is covered by the same flag without a special case in create/update/delete.

Server-side, all of these refuse for a system role:

- delete
- metadata update
- permission replacement
- any request field that would set `IsSystemRole`

The UI must match: no edit-name, edit-description, edit-permissions, or delete control. Read-only presentation stays. UI hiding is not the control.

---

## 8. Platform-administrator invariants

Three mechanisms stay distinct.

| Mechanism | What it protects | IAM-8 |
|---|---|---|
| System-role immutability | The `role-platform-administrator` row cannot be renamed, re-permissioned, or deleted | New refusals on metadata, grants, and delete |
| Last platform-administrator assignment | `RemoveRoleAsync` refuses to remove that role from the last remaining assignee | Unchanged. Not a delete-role rule |
| Custom-role delete | A user-created role with no assignees can disappear | New. Cannot target the system role, so it cannot remove the administrator role row |

Because system-role delete is categorically refused, role delete cannot strand the instance without a platform-administrator **role**. The instance can still be stranded if someone removes the last **user** assignment. That remains the existing assignment guard, not something delete-role replaces or weakens.

`UserHasPlatformAdministratorRole` compares role id, not display name. Renaming is already impossible for that system role. Custom roles must not be created with that id (§3).

Emergency-access accounts stay system-managed on the user assignment path. IAM-8 does not change that.

---

## 9. Authorization and permission rules

| Operation | Permission checked |
|---|---|
| List and read role | Existing `user.read` path used by role reads (`RequireUserReadAsync`) |
| List assignments for a role | Same read permission as role detail |
| Create role | `role.manage` on `Role` / instance. Non-empty initial grants also require `permission.assign` on `Role` / instance |
| Update name and description | `role.manage` |
| Replace grants | `permission.assign` (existing) |
| Delete role | `role.manage` |
| Assign or remove a role on a user | `role.assign` (existing) |

Do not add `role.create`, `role.delete`, or `role.update`.

`role.manage` continues to imply the governance surface in `RbacAuthorizationPolicyEvaluator`, including `permission.assign` and `role.assign` at instance scope. IAM-8 does not extend or narrow that implication. Direct checks stay as in the table so a future role that holds only `permission.assign` still cannot create or delete roles.

---

## 10. Audit rules

Add constants beside the existing names in `GovernanceAuditActionNames`.

| Event | When |
|---|---|
| `governance.role.created` | Role row committed |
| `governance.role.metadata.updated` | Name and/or description committed |
| `governance.permission.assignment.updated` | Grant replacement committed (existing) |
| `governance.role.deleted` | Role row delete committed |
| `governance.role.mutation_rejected` | Authorized caller attempted to mutate or delete a system role, or to delete a role that still has assignments. Detail includes `reason=system_role` or `reason=assignments_remain` |

Do not audit unauthorized calls or not-found ids.

Assignment create/remove audits stay `governance.role.assignment.created` and `governance.role.assignment.removed`. Last-administrator assignment refusal stays `auth_admin.blocked.last_platform_admin_removal`.

Audit rows are not deleted when a role is deleted. They may cite a role id that no longer exists. That is evidence, not a tombstone.

---

## 11. Persistence and history

**Removed on delete.** The `Roles` row only, and only when the assignment count is zero.

**Not removed.** `UserRoles` (delete does not run when any exist). `Users`. Audit log rows. Provisioning request logs. Permission catalog rows.

**Not introduced.** Lifecycle status, deleted-at, soft-delete flag, or a rename history table. `RowVersion` already exists and stays the concurrency token for the role row. Metadata update and grant replacement should fail cleanly if the row version no longer matches, as conflict, when the implementation passes the version. If the current client does not send a version, last-write-wins on grants remains acceptable for IAM-8.0's contract only for grant replace of a **user-created** role; delete versus assign must still be locked as in §12. Do not ignore `RowVersion` on delete: delete re-reads the row inside the transaction rather than trusting a stale client flag.

No migration is required for a new lifecycle column. A migration is allowed only if implementation needs a supporting index or a check constraint that the gate's rules cannot express with the existing unique name index, restrict foreign key, and `IsSystemRole` bit. Prefer no schema change.

---

## 12. Concurrency requirements

These races can break the rulings if delete is check-then-act without locks.

| Race | Required outcome |
|---|---|
| Assign (insert `UserRoles`) vs delete | Either the assignment commits and delete is refused, or delete commits and the assignment fails because the role is gone. No deleted role with a remaining assignment. No assignment silently removed by delete. |
| Grant replace vs delete | Whichever commits first wins. Delete of an unassigned role still deletes. Grant replace of a deleted role is not found. Neither may delete assignments. |
| Metadata update vs delete | Same. No partial name write after delete. |
| Two deletes | One deletes. The other is not found. |
| Create vs create, same name | One row. The other conflicts on `IX_Roles_Name`. |

**Proof.** A SQL Server test must interleave delete with `AssignToUserAsync` for a user-created role and assert the invariant above. In-memory providers are not sufficient for that proof. Lock the role row and the assignment rows for that `RoleId` in the delete transaction (`UPDLOCK`, `HOLDLOCK` or equivalent) before the count check, then delete. The restrict foreign key must remain so a missed lock cannot orphan a grant of privileges to a removed role; the test's expected result is still the explained refusal or a failed assignment, not an unhandled 500 as the product contract.

System-role refusals do not need a concurrency test beyond a straight service test: the flag is immutable and seeded.

---

## 13. API contract

All routes stay under the existing Management API, cookie session, and SEC-1 request-trust rules. No new exemption.

| Method | Path | Success | Failure |
|---|---|---|---|
| POST | `/api/roles` | **201** with the created role detail (id, name, description, `isSystemRole: false`, permissions) | **400** validation. **403** unauthorized. **409** duplicate name |
| PATCH | `/api/roles/{id}` | **200** with updated detail, or **204** | **400** validation. **403** unauthorized. **404** missing. **409** system role, or name owned by another role |
| PUT | `/api/roles/{id}/permissions` | **204** (existing) | **400** catalog validation (existing). **403** unauthorized. **404** missing. **409** system role |
| DELETE | `/api/roles/{id}` | **204** | **403** unauthorized. **404** missing. **409** system role, or assignments remain. Body includes a stable `error` code: `system_role` or `assignments_remain`, plus `assignmentCount` when assignments remain |
| GET | `/api/roles/{id}/assignments` | **200** list of `{ userId, displayName }` and the count | **403** unauthorized. **404** missing role |

Existing GET list and GET detail stay. GET detail should include `assignmentCount` so the list and detail can explain delete without a second round trip. The assignments route is what the delete confirmation uses to name holders.

Conflict versus not-found is stable: missing id is 404; live row that the lifecycle forbids is 409.

---

## 14. UI contract

Surfaces stay the current governance role list and detail (`RolesList.razor`, `RoleDetail.razor`, routes under `/config/governance/roles`). No new rail item.

| Surface | Behavior |
|---|---|
| Role list | **Create role** when the operator has `role.manage`. Collect name, optional description, and optional initial grants using the existing permission catalog picker. System roles stay labeled system. |
| Role detail, user-created | Edit name and description when the operator has `role.manage`. Edit permissions stays the existing whole-set editor when the operator has `permission.assign`. |
| Role detail, system | Read-only name, description, and grants. No edit and no delete. Short copy: this built-in role cannot be changed or deleted. |
| Assignments | Detail lists current holders (display name, link to the existing user page) and the count, for every role the operator can read. |
| Delete | Shown for a user-created role when the operator has `role.manage`. Confirmation states that the role is removed and cannot be restored, and that people who hold it must be changed first. If the count is not zero, the control does not call delete; it explains that deletion is blocked until those assignments are removed. Server 409 still handled if the count changed. |
| After success | Leave the detail page for the role list. |
| Copy | Operator language only. No epic ids in labels. |

Destructive confirmation uses the existing confirm dialog pattern. Delete is not a list-only gesture; detail is the confirmation surface. A list affordance is not required to close IAM-8.

---

## 15. Explicit non-goals

- Context-scoped IAM and ResourceContext-specific roles
- Changing `RbacAuthorizationPolicyEvaluator`, scope containment, or the platform-administrator implication list, except to call the existing checks from new methods
- Cross-context grouping, departments, domains, responsibility areas
- Entra group-to-role mapping, SCIM, directory sync
- MFA, SEC-1 behavior, deployment, or UAT smoke
- Navigation overhaul and ADR-0040 `/connections`
- Role archive, retire, or undelete
- Bulk assignment changes
- Permission hierarchy, new catalog families, or module permission registration
- Editing the seeded platform-administrator permission JSON as an operator feature (repair services that already patch that seed stay as they are)

---

## 16. Implementation slices

IAM-8.0 is this document. Later slices do not start by rewriting the gate.

| Slice | Delivers |
|---|---|
| IAM-8.1 | Repository and `UserManagementService`: create, metadata update, delete, assignment listing, system-role refusal on grant replace. No UI. |
| IAM-8.2 | `RolesController` routes in §13, audit action names, HTTP status mapping. Client DTOs on `IManagementClient`. |
| IAM-8.3 | Role list create, detail metadata edit, assignment list, delete confirmation and blocked-delete explanation, system-role read-only presentation. |
| IAM-8.4 | SQL Server proof of assign-versus-delete (§12) plus service tests for system-role refusal, assigned delete refusal, unique name, and empty-grant create. |
| IAM-8.5 | Closure audit against this gate. Does not select grouping. |

IAM-8.2 depends on IAM-8.1. IAM-8.3 depends on IAM-8.2. IAM-8.4 can land with IAM-8.1 and IAM-8.2 and must be green before closure.

---

## 17. Test and proof requirements

- Unit: id is server-generated and not the display name; system role rejects metadata, grants, and delete; user role with assignments rejects delete and does not remove `UserRoles`; zero-assignment delete removes the role row; duplicate trimmed name conflicts; description empty becomes null; catalog rejection on create matches grant replace; unauthorized paths do not write lifecycle audits.
- API: status codes in §13, including 409 codes `system_role` and `assignments_remain`.
- SQL: the assign-versus-delete interleaving in §12 against SQL Server.
- UI helpers or component tests where this repository already tests governance pages: system role hides mutation controls; blocked delete copy includes the assignment count; successful delete targets the role list.
- Regression: existing role assign, last platform-administrator removal, and permission-replace tests stay green.

---

## 18. Open questions

None that block implementation. These decisions are closed by this gate:

- Delete does not unassign (R1).
- Name and description are editable; id is not (R3).
- Create may include grants in the same transaction; empty grants are valid (§5.1).
- Metadata and grants are separate operations (§5.2, §5.3).
- No tombstone (§4, §11).

---

## 19. Acceptance and closure criteria

IAM-8 is complete only when all of the following are true:

- A user-created role can be created, renamed, described, granted from the catalog, and hard-deleted when it has no assignments.
- System roles cannot be mutated or deleted through the API.
- Delete never removes assignments.
- The detail surface shows holders and blocks delete while any exist.
- Audits in §10 are written for the success and authorized-refusal cases named there.
- The SQL assign-versus-delete proof passes.
- Evaluation behavior for an assigned role is unchanged aside from the new system-role write refusals.
- The closure audit records that grouping was not started.

Until that closure, roadmap status is: **selected, design gate written, implementation not started.**

**Closure (2026-09-30).** Those criteria are met. See §20. This closeout does not select a next epic.

---

## 20. Closure audit (2026-09-30)

Read-only reconciliation of implementation HEAD `cb60c7b7282543061a9e87e439db5743672aac5a` against the §2 rulings and the §19 acceptance criteria. No product or test source was changed in this closeout.

**Ruling: complete and merge-ready.** Binding IAM-8 requirements are satisfied. Grouping, Context-scoped IAM, Entra/SCIM, navigation, and SEC-1 UAT were not started.

| Slice | Result |
|---|---|
| IAM-8.0 | Rulings R1–R9 remain the contract. |
| IAM-8.1 | `UserManagementService` creates, renames, replaces grants, lists assignments, and hard-deletes. System-role mutation is refused in the service. Delete does not call unassign. |
| IAM-8.2 | `RolesController` matches §13, including 409 `system_role` and `assignments_remain`. `IManagementClient` maps those conflicts. No new permission constant. |
| IAM-8.3 | Role list offers create. Detail edits metadata, lists holders, confirms delete, and leaves for the role list. System roles stay read-only. Blocked delete does not call the API. |
| IAM-8.4 | Committed SQL Server tests assert assign-versus-delete with no product change. They require a key-range lock from `UPDLOCK`/`HOLDLOCK` on `UserRoles`, and they assert `FK_UserRoles_Roles_RoleId` is `NO_ACTION`. |

| Gate area | Result |
|---|---|
| Role identity | Satisfied. Server-generated `role-` plus 32 lowercase hex. Not the display name. Not `role-platform-administrator`. |
| Naming and uniqueness | Satisfied. Trim, length, empty description to null. Conflict on the existing unique name index. |
| Create | Satisfied. One insert carries the initial grants. Empty grants do not require `permission.assign`. Non-empty grants do. |
| Metadata update | Satisfied. Name and description only. Separate from grant replacement. |
| Permission replacement | Satisfied. Existing PUT. System roles refuse before `UpdatePermissionsAsync` and do not write `governance.permission.assignment.updated`. |
| System-role immutability | Satisfied. Service, API, and UI. `IsSystemRole` is not client-supplied. Create stores false. |
| Delete, zero assignments, no implicit unassignment | Satisfied. Count under the lifecycle locks. Assignments are not deleted. A second delete of a missing id is not found. |
| Assignment visibility | Satisfied. Detail includes `assignmentCount`. Holders come from `GET /api/roles/{id}/assignments`. |
| Hard-delete / no tombstone | Satisfied. No lifecycle column and no migration. Audit rows are kept. |
| Platform-administrator invariants | Satisfied. Role delete cannot remove the system role. Last-administrator protection remains on user-assignment removal. Seed repair still calls `UpdatePermissionsAsync` directly. |
| Authorization | Satisfied. `role.manage` for create, metadata, and delete. `permission.assign` for grants. `user.read` for reads and assignment listing. Evaluator implication list unchanged. |
| Audit | Satisfied. Success and authorized-refusal events in §10. Unauthorized and not-found paths do not write lifecycle audits. |
| Persistence and history | Satisfied. Delete re-reads the row inside the transaction. Client version is not required. |
| API, client, and UI | Satisfied. Status codes in §13. Client conflict codes match. UI copy is operator language. No new rail item. |
| Accessibility | Satisfied against this gate. Fields are labeled, errors use alert text, and delete uses the existing confirm dialog. |
| Concurrency | Satisfied. Assign-versus-delete cannot leave a `UserRoles` row for a deleted role. |
| Test and proof | Satisfied for engineering closure. See the evidence note below. |
| Explicit non-goals | Intentionally out of scope, and not present in the IAM-8 diff. |

**Verified unchanged.** `RbacAuthorizationPolicyEvaluator` was not modified. No `role.create`, `role.delete`, or `role.update` permission. No ResourceContext-scoped grants, grouping, department, domain, Entra/SCIM, bulk unassign, archive, or tombstone. No SEC-1 request-trust exemption. `RemoveRoleAsync` last-administrator refusal was not edited.

**Known non-blockers, not closure failures.**

- `TryDeleteUnassignedAsync` does not re-check `IsSystemRole`. The service refuses a system role before that call. A direct repository delete of an unassigned system role is not an API path.
- The in-memory name pre-check compares with ordinal case-insensitivity. The unique index remains the conflict authority. Collation was not changed.
- Metadata update and grant replacement do not send `RowVersion`. §11 allows last-write-wins when the client does not send a version.
- Create is a page at `/config/governance/roles/new`, linked from the list. It is not a rail item.
- The blocked-delete dialog keeps a disabled **Delete role** label. It does not call delete, and the explanation includes the assignment count.
- `duplicate_name` is an additional stable 409 code. The gate required conflict for a duplicate name, and named `system_role` and `assignments_remain`.
- No new ADR. This gate remains the lifecycle contract.

**Evidence.** This closeout inspected the committed service, repository, API, client, UI, and tests. It did not re-run those suites. IAM-8.1 service and repository tests cover id generation, system-role refusal, assigned-delete refusal, zero-assignment delete, trimmed duplicate names, empty description, catalog rejection, and unauthorized create with no lifecycle audit. IAM-8.2 covers the §13 status codes. IAM-8.3 covers hidden system-role controls, blocked-delete copy, and navigation to the role list after delete. IAM-8.4 covers the SQL Server assign-versus-delete invariant and the restrict foreign key. That set is sufficient for engineering closure.

**CI.** Not required to record this closure. One deliberate remote CI / PR run is warranted as the pre-merge checkpoint: the body is merge-ready authorization work, and that run would execute the full suite, including the SQL Server concurrency proof, on a clean agent. It is not warranted merely because another slice was committed. Do not push as part of this closeout.
