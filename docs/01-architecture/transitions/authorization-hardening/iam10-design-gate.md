# IAM-10: Authorization hardening, permission definitions and scope composition (IAM-10.0 design gate)

| | |
|---|---|
| Date | 2026-10-15. **Revision 1** after architectural review, 2026-10-15 (§10 is the revision record). |
| Status | **Revised proposal. For final owner review. Not approved. Nothing is implemented.** Every "P-n" is a recommendation until the owner records a ruling. The questions still needing a ruling are in §5. |
| Baseline | `master` at `aec0c50` (PR #33, the SEC-2 closure). Branch `iam-10.0-authorization-hardening-design-gate`. |
| Nature | Design only. No product code, migration, test, contract or persistent data changed. The only other change in this branch is one index row in [transitions/README.md](../README.md). The RC1 program gate is **not** edited; the amendment it needs is proposed as text in §4.2. |
| Inputs | [RC1 program gate](../../rc1-program-gate.md) §§2, 3.2, 5, 6.3, 8; the [RC1 audit and program plan](../../../audits/rc1-architecture-audit-and-program-plan.md) IAM-10 sections (H, I, J) and its [supplemental addendum](../../../audits/rc1-supplemental-reconciliation-addendum.md) B.6 and C.3; the [AUD-1 design gate](../audit-hardening/aud1-design-gate.md) and [AUD-1.2 record](../audit-hardening/aud1-2-identity-governance-record.md) §8; the [IAM-9 gate](../identity-foundation/iam9-design-gate.md) §19 and §29; the [IAM-8 gate](../custom-role-lifecycle/iam8-design-gate.md); [ADR-0012](../../../02-decisions/ADR-0012-authorization-evaluation-model.md) and [ADR-0013](../../../02-decisions/ADR-0013-action-and-scope-naming-conventions.md); the [SEC-2 gate](../security/sec2-configuration-secret-and-operator-boundary-design-gate.md) §4.5; the CTX gates on ownership; and the source cited below. Where a document and the code disagree, the code wins and the disagreement is listed in §4. |

**Labels.** **Code fact** is traced in source at the baseline. A fact marked † was traced by a read-only code survey and spot-checked, not re-derived line by line. **P-n** is a recommended decision. **OQ-n** is an owner question. **C-n** is a conflict or ambiguity between existing contracts. **G-n** is a contract gap. **A10.x** is an acceptance criterion. Paths are abbreviated: `SEC/` is `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/`, `API/` is `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/`, `PERS/` is `src/Apperture.Persistence/Apperture.Persistence.SqlServer/`.

**Approved decisions treated as binding** (not reopened here): RC1 requirement 4 (no known escalation path; every permission has a name, description and sensitivity); D-06 (a Critical credential-recovery permission with declarative step-up); D-07; D-08 (`operational_team.manage`); D-09 (no second policy engine); D-10, D-11, D-12 (context-scoped authorization is not implemented in RC1, no context scope constant ships, guests are not confined to one Application); the AUD-1 dependency ruling; the Recovery Access boundary of IAM-9 §19 and AUD-1.2; the closure of SEC-2. **Owner direction of this review, also binding on this revision:** the stricter administrator-viability policy is retained; Recovery Access never satisfies the ordinary-administrator count; the audit attribution correction and its verification precede removal of the legacy grant-all branch; the symmetric removal rule and the separate directory-search and connection-verify permissions are retained; there is no generic exemption from declarative step-up; refusals are typed.

---

## 0. Summary

1. **One definition of "holds".** An immutable `EffectivePermissionSet` answers `Allows(permission, targetScope)` with exactly the evaluator's rule (§3.1, P-1). It is introduced first as a behaviour-neutral refactor, proven equal to the current evaluator by a differential test, before anything else changes.
2. **Two subset rules, decided inside the audited unit on locked rows** (P-2 to P-5). Rule 1: permissions added to a role must be held by the actor. Rule 2: a role may be assigned only if the actor holds all its permissions, and the Platform Administrator role only by a Platform Administrator. Removal is symmetric (P-4). The decision uses row locks that the existing role-delete path already uses; no authorization-locking framework is added.
3. **The `role.manage` implication is deleted together with a preserving data step** (P-6, P-32). The implied set is **ten** combinations, of which eight are catalog-valid grants that are added, one is replaced by switching its three consumers, and one has no consumer. The consumers switch in the same release (P-18), so no check changes before the migration.
4. **One invariant protects administrator viability** (P-8 to P-11): a mutation is refused iff it would take the number of viable ordinary administrators from at least one to zero. Viability is real sign-in viability, not a role-assignment count. Recovery Access is never counted. One gate serializes every mutation that can reduce it, and the same rule closes the sequential hole as well as the race.
5. **`PermissionDefinition`** (P-12 to P-16) and the catalog changes (P-17 to P-20) are as in the first revision, with the migration and compatibility policy for the two new credential-using permissions now defined (P-19).
6. **Scope composition** (P-21 to P-27) is unchanged: two independent axes, context never a scope-key string, `integration:{id}` an identity, one list-filtering contract. RC1 implements none of it.
7. **Step-up is declarative at the evaluator seam with no exemption** (P-29). The RC1 decision (OQ-1) is recorded with its security trade-off: no step-up flag on role and permission operations.
8. **Typed refusals replace `InvalidOperationException` mapping** (P-37): authorization refusal, business conflict, not found, validation and audit-infrastructure failure are separate types with separate statuses.
9. **Recovery Access attribution comes from the authenticated user's server-side designation**, not from the legacy marker (P-38). The correction and its verification are prerequisites to removing the legacy grant-all branch (P-36).

**Packages (§6).**

| Package | Content | Depends on |
|---|---|---|
| IAM-10.1 | `PermissionDefinition`, registry, seeded names and descriptions, module declarations, catalog DTO, new catalog entries | 10.0 |
| IAM-10.2a | Four slices: (1) held-set refactor with differential proof; (2) subset rules, row locks, typed refusals, writer guard; (3) **one release unit**: implication removal, data steps, consumer switch, enforcement switches; (4) audited repair | 10.1; AUD-1.2 (done) |
| IAM-10.2b | Administrator-viability gate, query and invariant | 10.2a |
| IAM-10.3 | Result type, step-up at the seam, enforcer, denial auditing, capability flags | 10.1 |
| IAM-10.4 | Catalog and role editor UI, refusal messages, honest scope documentation | 10.1, 10.2a |
| IAM-10.5a | Recovery Access attribution correction and verification | 10.0 (may precede 10.2) |
| IAM-10.5b | Legacy break-glass removal, dormant surface, `relationship.manage` removal, ADR amendments | 10.2a, **10.5a verified** |

## 1. Scope and non-goals

**In scope.** The escalation paths and the subset, implication and migration rules. The administrator-viability invariant. `PermissionDefinition`, sensitivity and step-up metadata. The catalog questions carried in from IAM-9 and SEC-2. The six scope-composition questions of the program gate §6.3. The result type, declarative step-up, typed refusals, refusal auditing, migration behaviour and the tests that prove them.

**Not in scope.** Any context-scoped implementation, scope constant, role, grant, capability or UI (D-10, D-11). Permission sets, deny rules, service accounts, agents or a second policy engine. IAM-11 content (credential recovery flows, decommission, session revalidation, throttling): this gate defines only the shapes IAM-11 consumes. Reopening SEC-2 or IAM-9. A general security audit.

## 2. Current code facts and contract gaps

### 2.1 How authorization works today

| Fact | Evidence |
|---|---|
| The evaluator gives RBAC only to `ActorKind.Human` that resolves to a user. Everything else is denied | `SEC/RbacAuthorizationPolicyEvaluator.cs:43-49, :95-96` |
| A resolved session carrying the break-glass marker is allowed every check | `:51-53` |
| Holders of `role-platform-administrator` are allowed a fixed set of **ten** resource and action combinations at instance scope **even when the role's stored permissions are empty** | `:58-62`; `SEC/PlatformAdministratorGovernancePermissions.cs:20-51` |
| Effective permissions are the union of the user's roles' stored grants, de-duplicated. A grant matches on action (case-insensitive), resource type (only when both sides have one) and scope containment. The **target scope is taken from the `resourceIdentity` or `resource` argument, never from `permission.Resource.ScopeKey`**; with neither it is `instance` | `:56, :64-92` |
| An instance-contained `role.manage` on Role implies the same ten combinations, before any action comparison | `:75-79, :99-118` |
| Scope containment is: exact match, `instance` contains everything, or `outer + ":"` is a prefix of `inner` (ordinal, case-sensitive) | `SEC/ScopeContainmentEvaluator.cs:11-29` |
| The evaluator returns `bool`. More than 40 production files call it. A second result-like scaffold (`AuthorizationEvaluationResult { Allow, Deny }`, `AuthorizationEvaluationPath`, a second `AuthorizationEvaluationContext`) in `Core.Application/Authorization/` has no other reference | `IAuthorizationService.cs`; `SEC/DelegatingAuthorizationService.cs`; `git grep` |
| `UserRoles(UserId, RoleId)` rows have no scope. `IRoleProvider.GetRolesForUserAsync` returns `Role`s. Grants are JSON in `Roles.PermissionsJson` | `PERS/AppertureDbContext.cs:844-851`†; `IRoleProvider.cs` |
| Only `HumanActor` is created in production. `Machine`, `System` and `RemoteInstance` have no creator. The runtime plane makes no authorization call. `ServiceAccount` is an unreferenced domain record† | `SEC/HumanActor.cs:13`; survey |
| Request serving is **not** gated on migration completion. `MigrationCompletionState.IsComplete` feeds a readiness health check only. A failed migration or startup data step fails the host start | `PERS/PersistenceMigrationHostedService.cs:33-60`; `MigrationsCompleteHealthCheck.cs` |

### 2.2 The ten combinations implied by `role.manage`, and their consumers

Verified by reading `IsGovernanceOrAuthConfigurationSurface` and every production check of each action. All consumers call with scope `instance`.

| # | Combination | In the catalog? | On the Platform Administrator role explicitly (repair arrays)? | Production consumers (non-test) |
|---|---|---|---|---|
| 1 | `user.read` on **Role** | **No** (the stopgap) | Yes (`RequiredGovernanceSurfacePermissions`) | `OperatorCapabilitiesQueryService.cs:52`; `API/GovernanceController.cs:46-52`; **`SEC/Dashboard/GovernanceDashboardCardProvider.cs:28-33`** |
| 2 | `permission.assign` on Role | Yes | Yes | `UserManagementService.cs:1049`; `OperatorCapabilitiesQueryService.cs:53` |
| 3 | `role.assign` on Role | Yes | Yes | `UserManagementService.cs:1007`; `UserProvisioningService.cs:433`; `OperatorCapabilitiesQueryService.cs:54` |
| 4 | `auth.configuration.read` on InstanceConfiguration | Yes | Yes | `ConfigAuthOverviewController.cs:78`, `ConfigAuthUiPolicyController.cs:149`, `ConfigEntraTenantsController.cs:804`, `AuthWarningsDashboardCardProvider.cs:31`, capabilities `:62` |
| 5 | `auth.ui_policy.manage` on InstanceConfiguration | Yes | Yes | `ConfigAuthOverviewController.cs:87`, `ConfigAuthUiPolicyController.cs:167`, `AuthWarningsDashboardCardProvider.cs:37`, capabilities `:57` |
| 6 | `auth.entra.manage` on InstanceConfiguration | Yes | Yes | `ConfigEntraTenantsController.cs:813, :828`, `AuthWarningsDashboardCardProvider.cs:43`, capabilities `:67` |
| 7 | `user.read` on User | Yes | Yes | `UserManagementService.cs:980`; `ManagedContactManagementService.cs:706`; `GovernanceController.cs:112` |
| 8 | `user.manage` on User | Yes | Yes | `UserManagementService.cs:993`; `UserProvisioningService.cs:449`; `EntraAccessRequestService.cs:380`; `EntraProvisioningDirectoryService.cs:241`; capabilities `:153` |
| 9 | `role.assign` on **User** | **No** | **No** | **None.** Every `role.assign` check is on Role (`git grep`). Vestigial |
| 10 | `credential.manage` on User | Yes | Yes | `UserManagementService.cs:1021`; capabilities `:158` |

So: eight are catalog-valid grants (rows 2 to 8 and 10), one is a stopgap with three consumers that this design re-points to row 7 (row 1), and one has no consumer (row 9). **The Platform Administrator role already holds nine of the ten explicitly; only row 9 is absent, and nothing asks for it.** A custom role may already hold any of the eight explicitly, since the API permits granting them.

### 2.3 Contract gaps

| # | Gap | Evidence |
|---|---|---|
| **G-1** | **No subset rule exists.** `AssignRoleAsync` checks `role.assign`, that the user and role exist, and that the user is not system-managed. It does not look at the role's contents and does not refuse the Platform Administrator role. `SetRolePermissionsAsync` and `CreateRoleAsync` check the catalog and the relevant permission, never the actor's own holdings | `SEC/UserManagementService.cs:774-807, :151-180, :183-225` |
| **G-2** | **The `role.manage` implication** makes a `role.manage`-only custom role root-equivalent. A test pins it as intended | IAM-9 §29.2; `tests/Apperture.UnitTests/Governance/RbacAuthorizationPolicyEvaluatorTests.cs:85`† |
| **G-3** | **"Holds" has no single definition.** The evaluator, the effective-access endpoint, the capability flags and the repair arrays each compute it separately | survey† |
| **G-4** | **The Platform Administrator role carries two stored grants the catalog does not contain**: `user.read` on Role (row 1) and `workflow.execute` on WorkflowInstance at instance scope. The repair forces both, only adds and never removes, runs unaudited, and uses no transaction or lock. `workflow.execute` is **not** part of the evaluator's special authorization (§2.1): it is an ordinary stored grant | `SEC/PlatformAdminRolePermissionRepair.cs:32, :111`; `SEC/PlatformAdminRoleRepairService.cs`† |
| **G-5** | **Governance-read mismatch** (IAM-9 §29.1), now with a third consumer. `CanViewGovernance`, `GET api/governance/descriptors` **and the governance dashboard card** check `user.read` on **Role**; `GET effective-permissions` and every user and role read API check it on **User** | rows 1 and 7 above |
| **G-6** | **Permissions have no metadata**; the role editor shows raw action strings; module declarations have no name, description, sensitivity or step-up field; `GovernedActionDescriptor`, `ActionCategory` and `IsPrivileged` are never read | `Shared.Kernel/PermissionDescriptor.cs`; `RoleDetail.razor:113, :245-257`†; `ModuleSdk.Abstractions/ModuleManifest.cs`†; `git grep` |
| **G-7** | **Team management rides on `schedule.manage`**, as does the attention contact group service | `SEC/OperationalTeamManagementService.cs:171-180`; `SEC/AttentionContactGroupManagementService.cs:176-185` |
| **G-8** | **`relationship.manage`** is seeded on `TrustRelationship`, with no consumer and no test† | `SEC/PlatformPermissionSeeder.cs:112` |
| **G-9** | **The last-administrator guards are unlocked and count role assignments, not sign-in viability.** They act only when the assignment count is exactly one (`adminCount == 1` at `UserManagementService.cs:733, :948`; `count <= 1` at `:838`). So (a) two concurrent operations can both pass (F-10A), and (b) **with no concurrency**, two administrators who sign in only locally can each have their credential disabled in turn: each sees a count of two and passes. Removing an **Entra** link has no guard at all (the guard is inside the `source == Local` branch, `:727`). Entra registration and sign-in policy changes have only an instance-level "some sign-in path remains" check, not an administrator-level one (`SEC/AuthenticationConfigurationGuard.cs:30-38`). The Entra-primary policy counts differently again (it skips break-glass-eligible users and requires an enabled local credential)†. The `User` record has no status or disabled field, so no user lifecycle mutation exists to guard yet | `UserManagementService.cs:704-742, :810-845, :898-955`; `SEC/EntraPrimaryAuthenticationPolicy.cs:86, :178-201`†; `Core.Domain/Identity/User.cs` |
| **G-10** | **There is no refusal contract for authorization.** `POST api/users/{id}/roles` and `DELETE .../roles/{roleId}` map **every** `InvalidOperationException` to **404**: the last-administrator refusal, a genuine not-found, and **`AuditInvariantException`**, which derives from `InvalidOperationException` and whose own documentation says an endpoint mapping that type to a refusal must exclude it, so an audit programming error reads as "not found". `RolesController` maps `RoleLifecycleException` to 409 and any other `InvalidOperationException` to 400 or 404 by message text. `RoleLifecycleErrorCodes` has three values. `IdentityLinkException(LastViableSignInMethod)` is not caught on `DELETE identity-links` and probably surfaces as a 500 (not run). `AuditOutcomeUnknownException` and `AuditCommittedResultUnavailableException` correctly do **not** derive from `InvalidOperationException` | `API/UsersController.cs:436-468`; `API/RolesController.cs:229-245`†; `Core.Application/Audit/IAuditedUnitOfWork.cs:127-170`; `UsersController.cs:396-431`† |
| **G-11** | **The checks are not serialized.** The permission checks run outside the audited unit. `AssignToUserAsync` is check-then-insert with no lock; `ReplacePermissionsAsync` uses the `RowVersion` token (edit-versus-edit is safe) but assign-versus-edit and actor-versus-anything are unserialized. The unit runs at the connection default isolation, with a per-operation application lock keyed by a GUID, which serializes nothing across operations, and a retrying execution strategy. The only cross-operation lock in the code is the ownership mutation gate. The only existing row-lock pattern is `TryDeleteUnassignedAsync` (`UPDLOCK, HOLDLOCK` on the role row and its assignments) | `PERS/SqlRoleRepository.cs:69-81, :104-114, :157-213`†; `PERS/Audit/SqlAuditedUnitOfWork.cs:94-135, :147-152`; `IResourceOwnershipMutationGate.cs` |
| **G-12** | **Step-up is per call site.** `IStrongAuthenticationGate` has two callers (local credential re-enable inside the unit, with a refusal row; a verified contact-email change). It fails closed when nothing usable is configured; a satisfied native TOTP or recovery code counts even when Entra is not configured | `SEC/StrongAuthenticationGate.cs:32-58`†; `UserManagementService.cs:900-931`; `AccountContactService.cs:263-291`† |
| **G-13** | **A stored grant with no resource matches any resource type** for its action (`:85-89`). The API cannot create one (catalog check). **Malformed `PermissionsJson` is swallowed as an empty list** and indistinguishable from "no grants", so the next change records "all added"; an element with a null `Action` is not caught | `PERS/SqlRoleRepository.cs:225-262`; `Core.Domain/Identity/Role.cs` |
| **G-14** | **Break-glass and Recovery Access are different mechanisms.** The grant-all marker (`IsBreakGlassSession`) has exactly one producer: the legacy `POST api/auth/break-glass` (`AuthController.cs:693`), off by default, password only, 12 hours. **Recovery Access** (the designated native-MFA user) signs in through ordinary local login with MFA, carries **no** marker, and is authorized by RBAC through its Platform Administrator role; a test states this ("Recovery Access uses the ordinary local session path"). The audit flag `viaRecoveryAccess` is read from `IRecoveryAccessSession`, which `HumanActor` defines as `IsBreakGlassSession`. **So rows written by a real Recovery Access session are not flagged; only legacy break-glass sessions are.** The only test of the flag builds an actor with the marker and calls it Recovery Access | `AuthController.cs:636-705`; `SEC/HumanActor.cs:37-40`; `PERS/Audit/AuditRecorderRows.cs:20-37`; `tests/.../AuthControllerSessionIdentityTests.cs:48-66`; `tests/.../IdentityGovernanceAuditSqlServerTests.cs:564`† |
| **G-15** | **ADR-0012 describes an evaluation order the code does not implement**: a break-glass step with "explicit override flag and audit", and ownership checks on `OwnerId`, which is never consulted | ADR-0012; `RbacAuthorizationPolicyEvaluator.cs` |
| **G-16** | **Authorization scope on a resource is caller-supplied text** (`Integration.ScopeKey`, `Connection.ScopeKey`), unvalidated; schedules have no governed identity† | `IntegrationManagementService.cs:133, :137`†; `ConnectionManagementService.cs:154`† |
| **G-17** | **Lists authorize after the row cap** (Runs); a guest with only module-scoped grants is refused the integration and connection lists entirely† | `RunQueryService.cs:79-98`†; addendum B.6 |
| **G-18** | **Tests and rulings pin "no context in authorization"**: `ResourceContextAuthorizationNonChangeTests.NoContextScopeKeyFormExists` and its sibling on governed identities, CTX-3 gate §9.3, IAM-8 R6, ADR-0044 | `tests/Apperture.UnitTests/ResourceContexts/ResourceContextAuthorizationNonChangeTests.cs` |
| **G-19** | **Role grants and assignments are written from more places than the API.** `UserRoles` rows are written by `SqlRoleRepository.AssignToUserAsync` and `RemoveFromUserAsync`, and by `SqlUserRepository.CreateAsync` when a new `User` carries `AssignedRoleIds` (`PERS/SqlUserRepository.cs:125`). Role JSON is written by `ReplacePermissionsAsync`, `UpdatePermissionsAsync` and `CreateAsync` | `git grep` |

## 3. Recommended decisions

### 3.1 A. Escalation: the held-set and the subset rules

#### P-1: `EffectivePermissionSet`, exactly the evaluator's rule

`EffectivePermissionSet` is built from a user's **unconstrained** role assignments. `Allows(permission, targetScope)` implements today's rule. A grantor **holds** a permission `G` iff `Allows(G, G.Resource.ScopeKey)`, which is the evaluator called as `Evaluate(actor, permission: G, resource: G.Resource)`. This avoids a second algebra (D-09). How each case is handled:

| Case | Rule | Check |
|---|---|---|
| **Platform Administrator special authorization** | If **any** assigned role has id `role-platform-administrator` (ordinal, case-insensitive, as `UserHasPlatformAdministratorRole`), the set adds the **floor**: the ten combinations of §2.2, **only for a target scope of `instance`**, whatever the stored JSON says. After the consumer switch of P-18 the floor is the eight catalog-valid combinations (rows 1 and 9 leave it). `workflow.execute` on WorkflowInstance is **not** in the floor; it is an ordinary stored grant (G-4) | Differential test; floor ⊆ repair arrays ⊆ stored grants after startup |
| **Malformed or missing permissions JSON** | Blank, null or `"[]"` is a readable empty list. **Unreadable JSON, or an unreadable element, contributes zero grants** (as today: fail-closed for that role) and is flagged on the loaded `Role` (a `PermissionsReadable` indicator the repository sets; the record gains it additively). A flagged role: (a) **cannot be assigned** (409 `role_permissions_unreadable`), because "assign a role that looks empty" is a trap if the JSON is later repaired; (b) **can be replaced** by an explicit permission set (Rule 1 applies to every permission in it, since the diff against unreadable content is "all added"); (c) is listed by the data step. The platform administrator's floor still applies, so an unreadable administrator JSON cannot lock out administration | Unit and SQL tests |
| **Instance and module scope containment** | Grant scope is `g.Resource?.ScopeKey ?? "instance"`; the grant matches iff `Contained(targetScope, grantScope)`: exact, `instance` contains everything, or `grantScope + ":"` prefixes `targetScope`. Held `workflow.execute@WorkflowInstance:instance` **holds** wanted `...:module:X`; held `module:X` holds `module:X` and `module:X:slot:Y`, **not** `module:Y` and not `instance`. Scope keys are case-sensitive | Table-driven unit test |
| **Null-resource (broad) permissions** | A stored grant with no `Resource` keeps today's meaning: any resource type, instance scope, so it holds every permission with that action. It is kept for compatibility, **cannot be created** through the API, and is listed (not changed) by the data step. A required permission with no resource skips the type check, as today | Unit test; data-step report |
| **Effective grants from multiple roles** | The union over all of the user's roles, de-duplicated. No deny, no precedence, order-independent. The floor applies if any role is the administrator role; the transitional implication (below) applies if any role holds `role.manage`. A role with unreadable JSON contributes nothing but does not hide the others | Unit test over generated role combinations |
| **Target scope source** | `Allows` takes an explicit `targetScope`. Callers keep the evaluator's convention (identity, then `resource`, then `instance`). `Holds(G)` passes `G.Resource.ScopeKey`. `permission.Resource.ScopeKey` is never used as the target, as today | Unit test |
| **Transitional implication** | Until slice 3 of 10.2a, `EffectivePermissionSet` includes the current `role.manage` implication so that the refactor is neutral. Slice 3 deletes it | Differential test |
| **Session marker** | The marker is a property of the session, not of the user's roles, so it stays outside the set: `AuthorityOf(actor)` returns "everything" for a marker session (as the evaluator does today) and the set otherwise. It disappears with the branch in 10.5b | Differential test |

**No unintended change before the migration.** Slice 1 of 10.2a changes no behaviour. Its proof is a **differential test** that runs the current evaluator and the new `Allows` over generated role shapes (empty and malformed JSON, `role.manage`-only, administrator with empty JSON, null-resource grants, module and slot scopes, several roles, marker sessions) against a corpus of every production check of the §2.2 combinations, built from source and guarded for completeness (a new check fails the guard until added). Slice 2 changes only what the new write restrictions intend. Slice 3 (P-6, P-18, P-32) is one release unit.

#### P-2: Rule 1, grant subset

Creating a role with grants, or replacing a role's permission set, requires the actor to **hold** every permission **added** by the change. Removal needs no further holding. System roles stay immutable (IAM-8). Module scope follows P-1. The decision is made at the decision point of P-5.

#### P-3: Rule 2, assignment subset

Assigning role `R` to user `U` requires (a) the actor holds every permission in `R` as locked, and (b) if `R` is the Platform Administrator role, the actor holds the Platform Administrator role. The target may be the actor: (a) and (b) do not depend on the assignee, so self-assignment adds nothing the actor does not already hold. The Recovery Access target refusal is unchanged. A role flagged unreadable (P-1) is refused.

#### P-4: Symmetric removal, with explicit Platform Administrator behaviour and no-op semantics (OQ-2)

Removing `R` from `U` requires the same (a) and (b) as assigning it. **No self-removal exception is needed or granted:** a user removing a role from themselves holds that role, so (a) and (b) hold trivially; they still need `role.assign`, as today. The order inside the unit, for assign and remove alike:

1. Lock set and decision snapshot (P-5).
2. **Authorization refusals first**, including for a request that would be a no-op: the actor must be entitled to manage `R` (the actor lacks `role.assign`, or Rule 2 (a) or (b) fails). Idempotency does not bypass authorization, and a "204 whether or not `U` holds `R`" answer is not given to an actor who may not manage `R`. Platform Administrator behaviour: assigning or removing the administrator role by a non-administrator is `platform_administrator_restricted`, even when `U` already has or lacks it.
3. System-managed target (Recovery Access): existing refusals, unchanged.
4. **No-op detection**: assigning a role already held, or removing one not held, changes nothing and writes no audit row (existing behaviour), and is answered 204.
5. **State and safety refusals** (the viability invariant, P-9) run only for a real change. A no-op for the last administrator is a silent no-op, not a recorded refusal (the AUD-1.2 finding).

#### P-5: The decision point and the transactional guarantee

**Decision point.** The authorization decision for create-with-grants, replace-permissions, assign and remove is made **inside the audited unit's delegate, after the lock set below is held, from the rows that the lock set returned**. The check made before the unit (the current `Require*Async`) is kept as an advisory fast refusal; it is never the decision. A deadlock rerun re-executes the delegate, so the decision is recomputed every attempt.

**Lock set**, taken in this order by one repository method (`LockAuthoritySnapshotAsync`), all with `UPDLOCK, HOLDLOCK`, joining the ambient transaction and refusing to run without one (the shape `TryDeleteUnassignedAsync` and the ownership gate already use):

| Order | Rows | Why |
|---|---|---|
| L1 | `UserRoles` rows `WHERE UserId = @actor` (a key-range lock on the leading key of the primary key, which also blocks inserts of new rows into the range) | The actor's own role set cannot change |
| L2 | `Roles` rows for the union of the actor's role ids and the target role id, **in ascending `Id` order** | The actor's roles' permissions and the target role's permissions cannot change. A self-edit locks one row once |
| L3 | For assign and remove: the `UserRoles` key `(U, R)`, present or absent | Replaces check-then-insert; concurrent identical assignments serialize; the primary key stays as backstop |

**The guarantee, exactly.** From the first lock until the unit commits or rolls back, no other transaction can insert, update or delete any row, or any key in any range, that fed the decision. The decision and the write therefore see one consistent state, and the committed outcome equals the rules evaluated serially at the commit point. Because the lock hints take locks whatever the row-versioning setting, this holds under **both** locking READ COMMITTED and READ_COMMITTED_SNAPSHOT. It holds against **every** writer of those rows, including system writers (repair, bootstrap, emergency tooling) and raw SQL, without their cooperation.

**What it covers, against each concurrent change asked about.**

| Concurrent change | Blocked by | Outcome |
|---|---|---|
| The actor's role assignment removed or added | L1 | Serial: refusal if removed first; the removal waits if the operation locks first |
| A role the actor holds has a permission removed (or is edited, or deleted) | L2 | Same |
| The target role edited by another administrator | L2 | Never "R plus P assigned by an actor who lacks P" |
| The target role deleted | L2 and the existing delete lock | Existing IAM-8 behaviour holds |
| Two identical assignments | L3 | One row, one audit row, the other a silent no-op |
| The actor edits a role they hold | L2 (one row) | Decided on the pre-edit set |

**What it does not cover (stated, not hidden).** The static catalog (immutable at runtime). Sign-in configuration. The validity of the actor's session or identity link, which is session revalidation owned by IAM-11 (F-11A) and is not an authority change. The target's Recovery Access designation, which only emergency provisioning changes and which the viability guard evaluates for the mutations that matter.

**Deadlocks.** A unit that holds L1 for one actor while inserting `(U, R)` where `U` is another unit's actor can deadlock with that unit. A deadlock victim is a transient failure, not a refusal: the unit's retrying execution strategy reruns the delegate. That EF's default transient set includes the deadlock error is framework behaviour not verified here, so acceptance A10.2.10 proves it with a recording execution strategy (the repository's own deadlock-test technique). No refusal and no audit row is written for a rerun.

**Alternatives considered, and why not.**

| Alternative | Why not |
|---|---|
| **One named gate for all authority mutations** (the ownership-gate pattern) | It protects only writers that remember to take it. The role writers include system writers and a user-creation path (G-19). A row lock protects them by construction. It is also a new mechanism where an existing one suffices |
| **`RowVersion` re-validation at commit** | A second read of every row and still a window; the row locks give the same result with the pattern already in the code |
| **`SERIALIZABLE` for the whole unit** | Changes isolation for every audited operation |
| **A per-actor or per-role application lock** | A new mechanism, again cooperating writers only |

The administrator-viability invariant (P-8) does use a gate, for reasons given there; the two are different: that lock set spans four tables and configuration and has no fixed row set.

#### P-6: Delete `ImpliedByInstanceRoleManage`; keep the named floor

The implication is deleted in slice 3 of 10.2a. The floor of P-1 stays, as the narrowed, named, tested set the evaluator already applies to the administrator role. It exists so a malformed administrator role cannot lock out administration (the IAM-10.2 invariant). A test asserts floor ⊆ repair arrays ⊆ the administrator's stored grants after startup; the catalog test's named exceptions are exactly the two non-catalog stored grants (G-4): `user.read` on Role (pruned, P-33) and `workflow.execute` at instance (kept: the instance-level grant is what lets the administrator satisfy every module-scoped check by containment).

#### P-7: Every ordinary writer is under the rules; every exempt writer is named

| Writer | Mechanism | Status |
|---|---|---|
| `UserManagementService` (role create, replace permissions, assign, remove) | `CreateAsync`, `ReplacePermissionsAsync`, `AssignToUserAsync`, `RemoveFromUserAsync` | **Ordinary**: rules apply |
| `UserProvisioningService.TryAssignRoleAsync` (`:420`) | `AssignToUserAsync` (initial role when provisioning or approving; today it needs only `role.assign`, `:424-440`) | **Ordinary**: Rule 2 with the provisioning administrator as the actor |
| `BootstrapPlatformAdminAssignment`, `DevTestUserBootstrapHostedService`, `RealBootstrapRecoveryService` | `AssignToUserAsync` | **Exempt**: system actor |
| `FirstAdminBootstrapService`, `EmergencyAccessService`, and the bootstrap and dev paths that create users | `new User(..., AssignedRoleIds: [admin])` through `SqlUserRepository.CreateAsync` (`:125`), **not** `AssignToUserAsync` | **Exempt**: system actor |
| `PlatformAdminRoleRepairService` | `UpdatePermissionsAsync` | **Exempt**: system actor; audited by P-30(d) |

A guard test enumerates both mechanisms (`AssignToUserAsync` callers and `User` creation carrying role ids, plus the three role-JSON writers) and fails on a writer in neither group.

### 3.2 A. Administrator viability (F-10A, F-10B, and the sequential hole)

#### The invariant

**I-VIABLE.** For a mutation `M`, let `pre` be the set of *viable ordinary administrators* before `M` and `post` the set after `M` is applied. **`M` is refused iff `|pre| ≥ 1` and `|post| = 0`.** One predicate covers sequential and concurrent loss: sequentially because it is evaluated on the post-state, concurrently because P-8 serializes every mutation that can reduce the set. A mutation that cannot reduce the set (assign a role, set a password, enable a credential, add a link) is never refused by it. When `pre` is already empty (a fresh instance, or Recovery Access only) nothing is refused by it, so the instance can still be repaired.

#### Role assignment count is not sign-in viability

| | Counts today | Viable ordinary administrator (proposed) |
|---|---|---|
| Holds `role-platform-administrator` by an assignment row | Yes | Yes |
| Is Recovery Access (designated, system-managed) | Counted by the role guards; **skipped** by the Entra-primary count | **Never counted**, even though it holds the role and can sign in |
| User lifecycle status | None exists | Must permit sign-in, once IAM-11 adds a status (the condition is vacuous until then) |
| Can actually sign in | Not checked (only at count one, and only via Entra) | At least one **usable sign-in method** (below) |

A **usable sign-in method** is an enabled Local credential with local sign-in enabled, or a **proven** Entra link while Entra sign-in is usable, both as the merged sign-in snapshot (`AuthSignInOptionsMerger`, the one `ValidateNoLockout` already uses) reports. The existing `HasViableAlternateSignIn` uses `EntraConfigurationState.IsUsable`; 10.2b reconciles the two with a differential test and uses the merged snapshot for both. A second flavour of the same query additionally requires an enabled local credential (the "Entra-independent" count of F-10B).

#### Mutations the invariant covers

| Mutation | How it can reduce the set | Today | In 10.2b |
|---|---|---|---|
| Remove the Platform Administrator role from a user | Drops a holder | Guarded at count ≤ 1 | Invariant |
| Disable a Local credential | Removes a sign-in method | Guarded at count 1, Entra alternates only | Invariant |
| Remove a **Local** identity link | Same | Guarded at count 1 | Invariant |
| Remove an **Entra** identity link | Same | **Unguarded** (IAM-11 N-07) | Invariant (so the existing endpoint is safe now; IAM-11's replacement operation calls the same guard) |
| Entra-primary policy disabling a local credential | Makes the user Entra-only | Own count, own unit, no lock | Same query, same gate; its deferral rule ("keep one Entra-independent administrator while Recovery Access is unhealthy") stays a **policy** that defers rather than refuses, computed from the same data |
| Entra registration activate, switch, disable, delete; sign-in policy changes that hide or disable a method | Makes Entra or Local unusable for administrators | Instance-level "some path remains" only (`ValidateNoLockout`) | Invariant added **beside** `ValidateNoLockout`, evaluated against the *proposed* configuration (OQ-10) |
| Recovery Access designation changes | Moves a user in or out of "ordinary" | Emergency tooling | Invariant on designation of an existing administrator |
| User disable, decommission, unlink (IAM-11) | Removes a holder or a method | Do not exist | Must call the guard; IAM-11's gate carries the criterion. 10.2b exposes the guard as one call |

A guard test lists the writers of `UserRoles` for the administrator role, credential disablement, identity links, Entra registrations and the designation flag, and fails on a writer that is not in this table or does not call the guard.

#### P-8 to P-11

| # | Decision |
|---|---|
| **P-8** | **One gate**, `IPlatformAdministratorSetGate`, modelled on the ownership mutation gate: one resource name, an exclusive transaction-owned application lock, a bounded wait, a typed busy exception, no direct `sp_getapplock` elsewhere. It is taken as the **first lock after the operation lock** by every covered mutation, **before** the authority lock set of P-5 (so the order is operation lock, gate, authority rows). A row-lock-only operation never waits for the gate, so there is no cycle. **Why a gate here and row locks in P-5:** the rows that decide viability span users, role assignments, credentials, identity links and configuration, with no fixed row set, and the covered writers are an enumerated, test-guarded list; the code base already has this pattern |
| **P-9** | **Apply, then verify, then refuse.** Inside the unit, under the gate: compute `pre`, apply `M`, compute `post`; if the invariant fails, refuse through the existing refusable path (rollback, then the operation's existing refusal audit row). This avoids duplicating each mutation's effect in a hypothetical predicate. For configuration mutations, whose effect is not in the database until applied, the query takes the proposed configuration. Codes and audit rows are unchanged |
| **P-10** | A wait past the bound throws a typed busy exception. It maps to 409 with a stable code and retry semantics, as the ownership gate's `ownership_operation_busy` does, and is a *transient contention* result, distinct from a business refusal and from audit-infrastructure failure (P-37) |
| **P-11** | The three refusals get typed exceptions and stable codes (`last_viable_administrator`, `last_viable_sign_in_method`, `last_local_credential`) at **409**, replacing the 404 and the unhandled link exception (G-10). The read-only display (`DescribeAsync`) uses the query without the gate |

### 3.3 B. Permission definitions and sensitivity

| # | Decision |
|---|---|
| **P-12** | **`PermissionDefinition`** (in `Core.Domain.Governance`). Fields: `Identity` (the unchanged `PermissionDescriptor`), `DisplayName` (required, ≤ 80), `Description` (required, one sentence, ≤ 300), `Category` (a small closed enum; the exact set is settled in 10.1), `Sensitivity` (`Standard`, `Elevated`, `Critical`), `RequiresStepUp`, `GrantedToPlatformAdministratorByDefault`, `Origin` (`Platform` or `Module`), `ModuleId?`, and the existing `Family`. The canonical permission key stays the audit `Identity()` form (`action@resourceType:scopeKey`). **Any later field is additive with a default** |
| **P-13** | **Registry.** `RegisterPermission(PermissionDefinition)` replaces the descriptor-only overload in production code. A definition without a name or description fails registration. A second registration of the same identity with different metadata conflicts, as today. `Source` and `ModuleId` shown to clients come from `Origin` and `ModuleId`, not from parsing the scope key |
| **P-14** | **Module declarations** (`ModulePermissionDeclaration`, and `ModuleGovernedActionDeclaration`, which also registers a permission) take required `DisplayName` and `Description`, `Sensitivity` (default `Standard`) and `RequiresStepUp` (default false). An incomplete declaration fails registration naming the module. The SDK is preview, so no shim; the five in-tree modules are updated. A module cannot decline the administrator default. `IsPrivileged`, `GovernedActionDescriptor` and `ActionCategory` are deleted. Modules are trusted in-process (ADR-0001): sensitivity is a declared classification for operators, not a boundary against the module |
| **P-15** | **Classification rule and proposed values.** **Critical:** can change who holds authority, replace credentials or identity trust, read or write secrets, or introduce executable code. **Elevated:** changes configuration or runtime and alerting behaviour, deletes data, makes an outbound call with a stored credential, or reads audit. **Standard:** reads of operational data and routine authoring. Proposed **Critical:** `role.manage`, `permission.assign`, `role.assign`, `credential.manage`, `auth.entra.manage`, `secret.manage`, `module.install`, and the IAM-11 credential-recovery permission. **Elevated:** `user.manage`, `auth.ui_policy.manage`, `platform.settings.manage`, `connection.create`, `connection.update`, `connection.delete`, `connection.browse`, `connector.manage`, `integration.create`, `integration.update`, `integration.delete`, `binding.create`, `binding.delete`, `binding.assign`, `binding.revoke`, `capability.register`, `mapping.manage`, `module.configure`, `schedule.manage`, `recovery.manage`, `runtime.control.shutdown.request`, `runtime.enroll.invitation.create`, `resource_context.manage`, `operational_team.manage`, `notification.definition.manage`, `managed_contact.manage`, `managed_contact.import`, `audit.read`, `operation.execute`, `workflow.start`, the module-declared `workflow.execute`, and the P-19 permissions. **Standard:** every other `*.read`, `capability.consume`, `environment.read`, `runtime.read`, `runtime.health.read`, `recovery.read`, `operation.read` |
| **P-16** | Names and descriptions are product text. A guard test rejects planning labels, epic or track identifiers and migration names in them (non-negotiable 6). Plain English in code for RC1 |

### 3.4 B. Catalog changes

| # | Decision |
|---|---|
| **P-17** | **`operational_team.manage`** (D-08) on a new `OperationalTeam` resource type at instance scope, Elevated. Registered in 10.1. In 10.2a slice 3 (a) it joins the repair arrays, (b) it is granted to every non-system role holding `schedule.manage` on Schedule at instance scope, and (c) `OperationalTeamManagementService` switches to it, in one release unit. The attention contact group service stays on `schedule.manage` (D-08 does not name it) |
| **P-18** | **Governance read** (G-5, IAM-9 §29.1). All **three** consumers of `user.read` on Role (the capability flag, `GET api/governance/descriptors`, and the governance dashboard card) move to `user.read` on **User**, which every governance read API already enforces and the catalog contains. **This switch ships in 10.2a slice 3, with the implication removal, not in 10.3**: otherwise a `role.manage`-only role would lose the descriptors endpoint and the governance capability between the two releases. The stopgap leaves the floor and the repair array and is pruned (P-33). No new permission. **Recorded ruling:** the governance pages become visible to holders of `user.read` on User, who could already call every governance read API; no API access is widened. A distinct `role.read` is deferred |
| **P-19** | **Credential-using routes** (the SEC-2 input; OQ-4). Principle: a route that calls out with a stored credential on the caller's behalf needs a separately grantable permission of at least Elevated sensitivity. Browse already complies. Add **`auth.directory.search`** (InstanceConfiguration) for the two directory searches and **`connection.verify`** (Connection) for test-connect. **Migration and compatibility policy:** (1) *Grants:* one idempotent data step grants `auth.directory.search` to every non-system role holding `auth.configuration.read` **or** `auth.entra.manage`, and `connection.verify` to every non-system role holding `connection.read`; the administrator gets both through the repair arrays. These are exactly the holders who can act today, so nothing is lost. (2) *Enforcement:* the routes switch to the new permissions in the same release unit as the grants; **there is no dual-accept period**, because a route that still accepted the old permission would make the new one decorative. (3) *Window:* before the step completes, holders of only the old permission are denied (safe direction; OQ-11). (4) *Operators:* the summary audit event lists each affected role so the broad preserving grant can be trimmed; the role editor shows the new permissions with their sensitivity (10.4). (5) *Capabilities:* additive flags `CanSearchDirectory` and `CanVerifyConnections` drive the UI; the client and server ship together. (6) *Rollback:* grants are additive, so an older binary keeps working on the old permissions it still sees. (7) *New roles:* created after the release must be granted the new permissions explicitly. (8) The runtime's own use of a connection during a run is not a route and is not gated by `connection.verify` |
| **P-20** | `relationship.manage` leaves the catalog in 10.5b (federation withdrawn, D-19). The repair gains an **obsolete list** that removes listed permissions from roles it manages; a data step prunes it from custom roles; both are audited (P-30(d)). No consumer or test references it† |

### 3.5 C. Scope composition (RC1 §6.3, questions 1 to 6)

All six are design rulings. Nothing is built in RC1 (D-10), no scope constant is added (D-11), and nothing is "reserved" in code. The only RC1 consequences are the constraints of P-27.

| # | Question | Recommendation | Alternatives considered |
|---|---|---|---|
| **P-21** | **1. Scope algebra** | **Two independent axes, evaluated together by the existing containment function.** Axis 1 is the current scope key (`instance`, `environment:…`, `module:…`, slots), untouched. Axis 2 is **context**, which is **not a scope-key string**: a grant carries, through its assignment (P-23), either "any context" or one context; a target carries its owning context (P-22). A check passes only if axis 1 contains (today's rule) **and** axis 2 matches. Instance grants stay universal on both axes. A context-constrained grant **never satisfies a check whose target carries no context** (fail closed), so a confined principal is refused every endpoint that has not been adopted, which lets adoption go one endpoint at a time | **A composite key** (`context:C/module:M` or the reverse): a prefix-only evaluator supports one hierarchy, and either order breaks one of "a grant at module M covers M in context C" and "a grant at context C covers every module in it" (addendum B.6). **Context as a prefix level of the one key:** fails every module-targeted check (ten services target `module:{id}`). Two axes also keep `NoContextScopeKeyFormExists` true as written |
| **P-22** | **2. Target-scope derivation** | `IGovernedResourceIdentity` gains an optional **`ResourceContextId`**, separate from `OwnerId` (which ADR-0012 reserves for principal ownership and the code never reads). Integration, Connection and Schedule fill it from their direct context column (Schedules have no governed identity today and gain one); a Binding derives it from its Integration and Connection, since it has no column of its own (CTX gate R4). **Live checks use the current owner, read in the same unit as the action and re-authorized under lock** (the ownership-move race SEC-2.1 already handles). **History reads** use the snapshot stored on the row where one exists, and the live owner otherwise (OQ-8). `ResourceContextId` is distinct from `ScopeKey`; `Integration.ScopeKey` and `Connection.ScopeKey` stay authorization text and gain grammar validation at write time (G-16) when this is built. **Deliberately superseded when built (not now):** the sibling assertion in `ResourceContextAuthorizationNonChangeTests` that governed identities ignore ownership, CTX-3 gate §9.3, IAM-8 R6 and the matching sentence of ADR-0044. `NoContextScopeKeyFormExists` stays green | Derive context from the Integration for every check (breaks standalone schedules and history, CTX gate R5). Authorize history by current owner (a moved integration's old runs would follow it) |
| **P-23** | **3. Assignment shape** | **Scope on the assignment, not the grant.** `RoleAssignment(PrincipalKind, PrincipalId, RoleId, ContextConstraint)`, where the constraint is "any" or one context. Roles stay reusable. The `UserRoles` key becomes `(UserId, RoleId, ContextKey)` with a sentinel for "any", a nullable column plus a rebuilt key, built post-RC1. `IRoleProvider` gains `GetAssignmentsForUserAsync`. **`GetRolesForUserAsync` keeps its signature and is defined to return only unconstrained assignments**, so a caller that is not migrated can never treat a constrained assignment as a wide one. System roles are unconstrained only. The subset rules extend: an actor may assign a role at constraint `K` only if they hold its permissions at a constraint that contains `K` | Scope on each grant inside a role (role explosion; scope in a second place). A parallel `ScopedAssignments` table (two sources of truth) |
| **P-24** | **4. Principal kinds** | `PrincipalKind` is `User` today. Others are added **only when their first consumer ships**, each with its own gate: **ServiceAccount** (persisted, issued credentials), **Integration** (an identity, P-25), **Agent** (acts on behalf of a user; authority is the intersection of its own and its delegator's, never more). `System` is **not a principal**: it is the absence of one; the scheduler and worker run unevaluated and are never assigned roles; audit keeps its own `AuditActor`. `ActorKind.Machine` and `RemoteInstance`, the `ServiceAccount` records, `TrustRelationship` and `GrantedCapability` are annotated dormant in 10.5b (D-19). `IRuntimeIdentityService` is **not** dormant (C-4) and is not an actor source | Define all four kinds now |
| **P-25** | **5. `integration:{id}` semantics** | **An identity, never a containment prefix.** It names the Integration as an acting principal and as the publisher or owner recorded on records (the POL-1 need). It is never a `ScopeKey`, never enters the prefix evaluator, and `ScopeKeyConstants` gets no member for it. Operator permissions about integrations (`integration.update`, the SEC-2.1 callback) stay on the existing evaluator, so SEC-2's `P-1` is unchanged. Per-integration operator confinement is not recommended: no trigger, and a third axis needs its own gate | A containment scope: the things that "belong to" an integration are shared connections, schedules, settings and definitions, which is ownership and context, not a scope tree |
| **P-26** | **6. One list-filtering contract** | `IAuthorizedContextResolver.ResolveAsync(principal, permission) → ContextAccess` (`All`, `Only(ids)` or `None`), computed from the principal's assignments whose roles grant the permission. The caller turns it into a **SQL predicate applied before ordering and the row cap** (the LST-1.2 requirement). The same call serves lists, search, counts, directory endpoints and dashboards. **Fail closed:** an aggregate that cannot apply a context filter (the audit log, instance-wide counts) requires `All` and refuses a confined principal. **Shared** appears in a set only when granted explicitly | Return entities and let callers filter (the Runs defect). A per-endpoint boolean |
| **P-27** | **RC1 do-not-freeze constraints** | RC1 code must keep these true: (1) `PermissionDefinition` is additive; (2) `EffectivePermissionSet` is the only holder of grant semantics; (3) `GetRolesForUserAsync` means "unconstrained assignments"; (4) role-assignment APIs, DTOs and audit events can gain an optional `contextId`; (5) `ScopeKeyConstants` contains no context or integration member; (6) capability flags are documented as instance-wide and never presented as per-Application; (7) the module authoring guide and administrator documentation state D-12 honestly: **guests cannot be confined to one Application in RC1; confinement is by capability and by module** | |

### 3.6 D. Result type, step-up, denial auditing and typed refusals

| # | Decision |
|---|---|
| **P-28** | **`AuthorizationDecision`** (not `AuthorizationEvaluationResult`, which collides with the unreferenced enum that 10.3 deletes). `Outcome` (`Allowed`, `Denied`, `StepUpRequired`), `Reason` (`None`, `NotAuthenticated`, `UnresolvedPrincipal`, `UnsupportedActorKind`, `NotGranted`, `StepUpRequired`, `StepUpNotConfigured`), the permission identity and its sensitivity. `IAuthorizationEvaluationService` gains `DecideAsync`; the `bool` overload and `IAuthorizationService.EvaluateAsync` stay and equal `Outcome == Allowed`. A denied API caller still receives the generic 403, except a step-up requirement, which keeps the existing strong-authentication codes |
| **P-29** | **Declarative step-up, enforced at the seam, with no exemption.** **Rule:** a permission whose definition has `RequiresStepUp` is enforced fail-closed on **every allow path** (stored grant, system-role floor, session marker) for the current request principal. There is no exemption attribute, no configuration switch, no actor kind and no session kind that bypasses it. The `bool` path returns false when assurance is insufficient, so no call site has to opt in and a flag cannot be silently unenforced; the decision path returns `StepUpRequired` or `StepUpNotConfigured` so the UI can prompt. A legacy marker session therefore cannot exercise a flagged permission (it cannot satisfy strong authentication); a native Recovery Access session can, through native TOTP recency. Evaluating a **different** actor (a background job) is not a bypass: it never targets a flagged permission on behalf of a session, and the guard test fails on a flagged permission evaluated for a non-current actor. **Step-up is per permission.** The existing per-operation call (re-enabling a local credential) is not an exemption: `credential.manage` is not flagged; IAM-11 gives the recovery operation its own flagged permission |
| **P-30** | **Denial and refusal auditing.** (a) Only the enforcing helper writes a denial row: one W3 row per denied enforced request, for Elevated and Critical permissions (fields: permission identity, sensitivity, reason). `bool` and decision evaluations never write, because the capability query evaluates about twenty permissions per page load. No throttle (the SEC-2 K-4 stance). (b) Escalation refusals: `governance.role.mutation_rejected` gains reason `grant_exceeds_authority`; a new W3 action `governance.role.assignment_rejected` (target the user; user, role, reason `exceeds_authority`, `platform_administrator_restricted` or `removal_exceeds_authority`, missing count). Both carry up to 20 missing permission identities and the count. (c) Grant and assignment success events gain an optional `maxSensitivity` field (severity stays catalog-static). (d) The startup repair becomes an audited unit and writes **one** summary event only when it changes something |
| **P-31** | **Enforcer adoption.** `RequireAsync` replaces the six private `Require*Async` copies in `UserManagementService` and is used by every service 10.2 and 10.3 touch. Other services keep the `bool` call until next changed. Safe because the step-up flag is enforced at the seam (P-29), not by adoption |
| **P-37** | **Typed domain results; no more business meaning from `InvalidOperationException`.** Applies to the methods and routes 10.2 touches (role create, update, delete, replace permissions; assign and remove role; identity-link removal; credential disable and re-enable; provisioning's initial role); not a repository-wide sweep |

| Category | Type | Examples (stable `code`) | HTTP | Audit |
|---|---|---|---|---|
| **Authorization refusal** | New `AuthorizationRefusalException` (code, message, up to 20 missing identities; not an `InvalidOperationException`) | `grant_exceeds_authority`, `assignment_exceeds_authority`, `removal_exceeds_authority`, `platform_administrator_restricted` | **403** | W3 refusal row (P-30(b)) |
| **Business conflict** | New `GovernanceConflictException`; the existing `RoleLifecycleException`, `EmergencyAccessException` and `IdentityLinkException` align to it | `system_role`, `assignments_remain`, `duplicate_name`, `role_permissions_unreadable`, `last_viable_administrator`, `last_viable_sign_in_method`, `last_local_credential`, `emergency.access.unsafe_operation` | **409** | The operation's existing refusal row |
| **Transient contention** | New `GovernanceBusyException` (the gate's wait expired) | `governance_operation_busy` | **409**, retry | None |
| **Not found** | New `GovernanceNotFoundException(kind)` | user, role | **404** | None |
| **Validation** | New `GovernanceValidationException` | invalid catalog permission, missing role id, empty name | **400** | None |
| **Step-up** | Existing `StrongAuthenticationException` | `auth.strong_authentication.required`, `.expired`, `.not_configured` | existing (403, 409) | Existing |
| **Audit infrastructure** | Existing `AuditOutcomeUnknownException`, `AuditCommittedResultUnavailableException`, `AuditOperationNotCompletedException`, `AuditInvariantException` | `outcome_unknown`, `committed_result_unavailable` | **500** from the host handler | The recorder; never mapped to 404, 409 or 400 |

Rules: controllers catch only these types. `catch (InvalidOperationException)` for a business outcome is removed from the touched actions (`UsersController` and `RolesController.MapRoleFailure`), and "not found" is no longer detected by message text. `AuditInvariantException` is never treated as a business outcome. An unrecognized `InvalidOperationException` is a 500. Bodies carry `code` and `message`; the roles routes also keep the existing `error` property with the same value until the client changes in 10.4. **Compatibility:** a business refusal that was reported as 404 or 400 becomes 403 or 409; a genuine not-found stays 404.

### 3.7 D. Migration and compatibility

| # | Decision |
|---|---|
| **P-32** | **The exact preservation rule for the implication.** For every **non-system** role holding a grant `g` where `g.Action` equals `role.manage` (ignoring case), `g.Resource` is non-null with type `Role` (ignoring case), and `g` is instance-contained (its scope is `instance`, or absent): add each of the **eight catalog-valid** combinations of §2.2 that the role does not already hold explicitly (rows 2 to 8 and 10). Add nothing for row 1 (`user.read` on Role): its three consumers move to row 7 in the same release (P-18), so every check gives the same answer. Add nothing for row 9: no production check asks for it. The step skips present grants, so it is idempotent, and it never touches a system role (the repair handles the administrator). It runs as a code step between EF migrations in `AppertureDatabaseMigrator` (the username-ledger precedent), in one transaction, as part of the startup that fails the host if it fails. It reports **one** audit summary event with each changed role and the added identities, and the role editor shows them as ordinary explicit grants. **Invariant, tested:** over the corpus of production checks of §2.2, the old evaluator's answer for the pre-migration roles equals the new evaluator's answer for the post-migration roles. Roles with grants carrying no resource, unreadable JSON, or unreadable elements are listed and left unchanged |
| **P-33** | **Other data steps** (same mechanism, same release unit): P-17, P-19; pruning of `user.read` on Role from every role including the administrator, through the repair's obsolete list (P-18); and, in 10.5b, `relationship.manage`. Prune before the catalog drops an entry, so an edit of a role is never refused for carrying an obsolete grant |
| **P-34** | **API and client compatibility.** `GET api/governance/descriptors` gains display name, description, category, sensitivity, step-up and origin (additive). Role and user mutation refusals gain the bodies and statuses of P-37 (a **breaking** change from 404 and from an unhandled error, noted in `docs/04-api/contracts.md` in 10.2 and handled by the pages in 10.4). There is no `Down` for the data steps; additive grants are harmless to an older binary |
| **P-35** | **Recovery Access boundary, carried forward unchanged.** The designated user stays system-managed (role, link, credential and password changes refused with `emergency.access.unsafe_operation`; the five audited refusal names), hidden from search, signed in through MFA, authorized through its Platform Administrator role, and **never counted** toward the viability invariant |
| **P-39** | **The upgrade window.** Because serving is not gated on migration completion (§2.1), a new binary may serve requests briefly before the data step has run. The design makes that window **deny-only**: the new evaluator never allows anything the old one would not (a `role.manage`-only role loses its implied access until the step runs; a `schedule.manage` holder cannot manage teams until it is granted; and so on). It can never allow more. A failed step fails the host start. Proof: A10.2.5 tests that, for the new code over pre-step data, the allow-set is a subset of the old evaluator's. Alternative: gate request serving until migrations complete (OQ-11) |

### 3.8 Recovery Access and the legacy break-glass branch

| # | Decision |
|---|---|
| **P-36** | **Legacy grant-all removal, ordered behind the attribution correction.** In 10.5b remove the evaluator branch, `POST api/auth/break-glass`, `BreakGlassAuthOptions` and the marker claim, because Recovery Access is the supported emergency path and the legacy path (password only, 12 hours, all permissions) is the only grant-all route left. **10.5b may not start until 10.5a is merged and its verification (A10.5a.1 to .6) has passed.** After removal those tests run again with the marker code gone. If the owner prefers to keep the legacy path, the minimum is: off by default, counted in `AuthorityOf` as "everything", never exempt from step-up (P-29), and a per-use audit row (OQ-6) |
| **P-38** | **Recovery session attribution: an authenticated, trustworthy source.** The audit recorder decides `viaRecoveryAccess` from the **server-side designation** of the audited unit's actor, not from a session claim. *Source:* the actor's id is the stable user id the application session carries (IAM-9: a cookie issued after a verified credential, and after MFA for the designated user, re-resolved to the user through the identity link on every request). The recorder looks up whether that user is the designated emergency user (`Users.IsBreakGlassEligible`, the single designation that `EmergencyAccessIdentity.IsSystemManaged` and `FindDesignatedEmergencyUserAsync` use) **inside the same transaction as the write**. It is not taken from a claim, from a request value, from role membership, or from a caller. *Why it is trustworthy:* the user id comes from the signed session; the designation is database state changed only by emergency provisioning; the lookup shares the audited transaction, so the flag and the row cannot disagree. *Failure behaviour:* a failed lookup fails the unit as an audit invariant failure; it is never silently unflagged. *Transition:* during the overlap the legacy marker is OR-ed in; it adds nothing, because the legacy login already requires the same designated user, so the new rule is a strict superset. *History:* no backfill (the audit table is append-only); rows written by the real Recovery Access before the correction stay unflagged, and the AUD-1.2 record gets a dated note. *Scope:* `IRecoveryAccessSession` and `HumanActor.IsRecoveryAccessSession` are removed with the marker. **ADR-0012** is reconciled **after** 10.5b is implemented: it will state the evaluation order the evaluator actually has (principal resolution, then the held-set with the floor, then step-up), and will say explicitly that `IGovernedResourceIdentity.OwnerId` is carried but **not evaluated** and that its step 3 is not implemented, with ownership belonging to ResourceContext. It will not claim otherwise |

## 4. Conflicts, ambiguities and the proposed RC1 amendment

### 4.1 Conflicts found

| # | Between | Finding | Proposed resolution |
|---|---|---|---|
| **C-1** | AUD-1.2 record F-10A acceptance (1) vs (3) | (1) asks that two operations which together leave no administrator **with a viable sign-in path** commit exactly once. (3) says non-concurrent behaviour is unchanged. Today's predicates act only at an assignment count of one (G-9), so the second of two sequential credential disables is **allowed**; (1) cannot pass without changing sequential behaviour | The viability invariant (I-VIABLE). Sequential behaviour becomes stricter in exactly the case that was already unsafe; codes and rows are unchanged. Owner direction of this review retains it |
| **C-2** | Audit plan IAM-10.2 "last-administrator guards unchanged" vs F-10A and F-10B | They gain a gate and a shared definition | Read "unchanged" as "same refusals and codes" |
| **C-3** | ADR-0012 and the audit plan's "Recovery Access already replaces break-glass" vs the code (G-14, G-15) | The grant-all branch is the legacy login, Recovery Access is ordinary RBAC, ADR-0012's break-glass audit step and ownership step do not exist | Reconcile ADR-0012 after 10.5b (P-38), without claiming the evaluator implements ownership |
| **C-4** | Audit plan section J ("runtime identity validation ... no callers; document as dormant") | `SqlRuntimeIdentityService` is registered and used for runtime enrollment and credentials†; whether `ValidateAsync` has callers was not verified | 10.5b re-checks and annotates precisely |
| **C-5** | `platform-settings-option-providers-and-scopes.md:362` (`integration.read` at `integration:{id}`) vs P-25 | That table uses `integration:{id}` as a scope | Amend the table to name the Integration resource and its derived scope |
| **C-6** | Audit plan IAM-10.2 "the administrator role holds every permission explicitly through repair" vs G-4 | The repair forces two grants the catalog does not contain | The test is "every catalog permission explicitly, plus the named exceptions" (P-6) |
| **C-7** | Program gate D-08 "roles intended to manage teams receive it" | "Intended" is undefined | P-17 preserves access by granting to every `schedule.manage` holder (OQ-5) |
| **C-8** | Audit plan names the result type `AuthorizationEvaluationResult` | The name is taken by an unreferenced enum | `AuthorizationDecision` (P-28) |
| **C-9** | ADR-0013 scope grammar | Omits `environment:{id}` and `environment:{id}:runtime:{id}`, which the code uses | Add them in the ADR amendment |
| **C-10** | New finding, not a conflict | G-14: `viaRecoveryAccess` is keyed to the legacy marker | P-38; prerequisite to P-36 |
| **C-11** | Audit plan "ensure the platform administrator holds every permission explicitly" vs the evaluator's floor | Two mechanisms give the administrator the same ten combinations | Keep both; test that they agree (P-6) |
| **C-12** | AUD-1.2 "refusal check before the already-in-that-state check" | The ordering finding assigned to IAM-10 | P-4 steps 2 to 5 fix an order that separates authorization refusals (before no-op) from state refusals (after) |

Checked and **not** in conflict: IAM-8 R7 ("no new permission constants") binds IAM-8 only; the SEC-2 `P-1` callback follows the existing evaluator unchanged (P-25); SEC-2's `P-9` is the baseline OQ-4 puts to the owner; D-11 and the pinned test hold under P-21.

### 4.2 Proposed targeted amendment to the RC1 program gate (not applied)

Applied only on owner approval, in the change that records the rulings:

1. **§6.3, append after the six questions:** *"IAM-10.0 rulings (2026-10-15, <owner approval>): 1. Two independent axes; context is never a scope-key string. 2. Governed identities gain `ResourceContextId`, distinct from `OwnerId`; live checks use current ownership, history its snapshot. 3. Scope on the assignment; `GetRolesForUserAsync` returns unconstrained assignments only. 4. `PrincipalKind` is `User` until a consumer ships; `System` is not a principal. 5. `integration:{id}` is an identity, never a containment prefix. 6. One `IAuthorizedContextResolver` returning a predicate applied before paging. None is implemented in RC1 and no scope constant is added (D-10, D-11)."*
2. **§3.1, D-08, append:** *"`operational_team.manage` is granted by the preserving data step to every non-system role that holds `schedule.manage`, and to the Platform Administrator through repair."* (if OQ-5 is accepted as recommended)
3. **§8 drift table, the `role.manage` row:** append *"closes when IAM-10.2a passes A10.2.1 to A10.2.18 and the owners record the disposition"*. **Add two rows:** (a) *"The last-administrator guards count role assignments and act only at a count of one, so two local-only administrators can lose their credentials in turn, and an Entra link can be removed from the last administrator unguarded (IAM-10 gate G-9). Owner: IAM-10.2b."* (b) *"The audit `viaRecoveryAccess` flag is set from the legacy break-glass marker, not from a real Recovery Access session (IAM-10 gate G-14). Owner: IAM-10.5a."*
4. **ADR-0012 and ADR-0013 amendments** are IAM-10.5b deliverables, not RC1-contract edits.

## 5. Owner questions

### 5.1 The nine original questions, with revised recommendations

| # | Question | Revised recommendation | Status |
|---|---|---|---|
| **OQ-1** | Should `permission.assign`, `role.assign` and `role.manage` require step-up in RC1? | **No flag in RC1.** Recorded decision: *Security cost:* a hijacked **Platform Administrator** session (stolen cookie, unattended terminal) can create roles and assign Critical permissions with no fresh strong authentication, for the session's life. Rules 1 and 2 already bound any non-administrator to their own holdings, so step-up would be the control for exactly the administrator case. *Compensating controls:* the subset rules, Critical-sensitivity audit, the viability invariant, Recovery Access. *Operability cost of the alternative:* a flagged permission is fail-closed, so role administration would be unusable on an instance with no strong-authentication mechanism configured. *Residual risk accepted for RC1:* the hijacked administrator session. *Revisit trigger:* OPS-1 making a mechanism part of every supported install, or the RCV-1 security review. *No exemption mechanism exists either way (P-29):* flipping the flag later is a metadata change, not new enforcement code | Recommend; owner records the acceptance |
| **OQ-2** | Symmetric removal (P-4) | **Yes**, with the explicit order, Platform Administrator behaviour and no-op semantics of P-4 | Retained per direction |
| **OQ-3** | (a) Viability-aware predicate; (b) Recovery Access counted? | **(a) Yes: I-VIABLE** (a single invariant, sequential and concurrent). **(b) Never counted** | **Settled by owner direction** |
| **OQ-4** | Separate directory-search and connection-verify permissions | **Yes**, with the migration and compatibility policy of P-19 | Retained per direction; owner confirms the policy |
| **OQ-5** | Breadth of the `operational_team.manage` migration | **Every non-system `schedule.manage` holder**, plus the administrator through repair; the attention contact group service unchanged | Needs ruling |
| **OQ-6** | Remove the legacy break-glass login and grant-all branch | **Yes, but only after 10.5a is verified** (P-36). The ordering is settled by direction; the removal itself needs the owner's approval | Needs ruling on removal |
| **OQ-7** | Attribution of `viaRecoveryAccess` | **Settled by direction**: server-side designation of the authenticated user, read in the audited transaction (P-38), in 10.5a, with a dated AUD-1.2 note and no backfill | **Settled by owner direction** |
| **OQ-8** | Authorization basis for reads of history | **The snapshot on the row**, falling back to the live owner. Nothing is built; the ruling only prevents RC1 storing the wrong thing | Needs ruling |
| **OQ-9** | The starting sensitivity classification (P-15) | **Accept** as the starting point, re-reviewed in 10.1 against the real list | Needs ruling |

### 5.2 Remaining decisions requiring owner approval

| # | Decision | Recommendation |
|---|---|---|
| **OQ-5, OQ-6, OQ-8, OQ-9** | As above | As above |
| **OQ-10** | **Scope of the viability invariant over configuration.** Include Entra registration changes and sign-in policy changes (which can make the only usable method unusable for every administrator), beside the existing instance-level `ValidateNoLockout`? | **Yes.** It adds the two controllers' units to 10.2b. Without it, an administrator-level lockout is still reachable through configuration |
| **OQ-11** | **The upgrade window** (P-39): accept a brief deny-only window before the startup data step completes, or gate request serving until migrations complete? | **Accept the window.** It can only deny, a failed step fails the start, and a serving gate is a new host-wide mechanism. If the owner prefers, the gate is a small separate change |
| **OQ-4 (confirm)** | The no-dual-accept rule and the preserving grants of P-19 | Accept |

No other architectural decision is open. The P-n decisions not named here are recommended for ruling as a set.

## 6. Implementation packages

Every package starts only on an explicit owner instruction. A review and a CI checkpoint follow 10.2a and 10.2b.

| Package | Goal | In | Out / do not change | Depends on | Handback |
|---|---|---|---|---|---|
| **IAM-10.1** Permission definitions | P-12 to P-16; catalog entries of P-17 and P-19 | `PermissionDefinition`, registry, seeded names and descriptions, the P-15 table, module declaration fields and the five in-tree modules, catalog DTO fields, deletion of the dead scaffolding, `operational_team.manage`, `auth.directory.search`, `connection.verify` registered. Evaluation unchanged | No grants, no enforcement switch, no UI | 10.0 | Catalog table with name, sensitivity and origin per entry |
| **IAM-10.2a** Anti-escalation | P-1 to P-7, P-17, P-18, P-19 enforcement, P-30(b)(c)(d), P-31, P-32 to P-34, P-37, P-39 | **Slice 1:** `EffectivePermissionSet`, the transitional implication and marker inside it, the differential and corpus-guard tests. **No behaviour change.** **Slice 2:** Rules 1 and 2, the removal rule, the lock method and decision point, `PermissionsReadable`, typed refusals for the touched actions, the writer guard. **Slice 3, one release unit:** implication removal; the preserving data step and its summary event; the three-consumer switch of P-18; the grants and enforcement switches of P-17 and P-19; the obsolete-list prune of `user.read` on Role. **Slice 4:** the audited repair | Viability. Scope containment. Recovery Access behaviour | 10.1; AUD-1.2 | The three-scenario table and the refusing path for each; the differential result |
| **IAM-10.2b** Viability | P-8 to P-11, I-VIABLE | The gate, the viability query (both flavours), the guard as one call, the rewritten guards and policy, the Entra-link and configuration coverage, typed 409 refusals, `DescribeAsync` | Role editing. IAM-11 additions (they call the guard when they ship) | 10.2a (same service file; sequential) | Race and coverage matrix per mutation |
| **IAM-10.3** Decision and step-up | P-28, P-29, P-30(a), the capability flags `CanSearchDirectory` and `CanVerifyConnections` | `AuthorizationDecision`, `DecideAsync`, step-up at the seam on every allow path, `RequireAsync`, denial auditing, deletion of the unreferenced result scaffold | No call-site mass migration; the `bool` overload stays | 10.1 | Parity corpus result |
| **IAM-10.4** UI | Names, descriptions, categories and sensitivity shown; refusal messages for every P-37 code; assignable-role presentation; D-12 documentation | `RoleDetail`, `RoleNew`, `RolesList`, assignment dialogs, `ManagementClient` handling of the new codes | No raw action strings as primary text. No context UI | 10.1, 10.2a | bUnit and render test list |
| **IAM-10.5a** Attribution | P-38 | The recorder reads the designation in the audited transaction; the verification tests; the dated AUD-1.2 note | The legacy branch (it stays until 10.5b). No backfill | 10.0 only (may precede 10.2) | The verification results |
| **IAM-10.5b** Dormant surface | P-20, P-36, dormant annotations, `relationship.manage` removal, ADR-0012 and ADR-0013 amendments | Branch, endpoint, option and claim removal; the marker-free re-run of the 10.5a tests; annotations; the C-4 and C-5 corrections | No new capability | 10.2a; **10.5a merged and verified**; owner rulings OQ-6 | Removed-surface list |

**Downstream.** IAM-11 needs 10.2a, 10.2b and 10.3 (its credential-recovery permission is meaningless until escalation is closed) and calls the viability guard. LST-1.4 needs `operational_team.manage` (10.2a). The first context-scoped consumer needs its own gate. SDK-8.3 should land the module declaration change of P-14 in the same window.

## 7. Acceptance and test matrix

**Legend.** **U** pure unit test, no database. **S** real SQL Server, on a locking READ COMMITTED catalog and a READ_COMMITTED_SNAPSHOT catalog, throwaway catalogs, **failing rather than skipping** without SQL Server. **H** the real Management Host over HTTP with real principals. **B** bUnit or render test. **G** source or architecture guard.

### 7.1 The three escalation scenarios

| Scenario | Today | Refusing path (proposed) | Tests |
|---|---|---|---|
| 1. A `role.assign` holder assigns the Platform Administrator role to themselves or a colleague | `AssignRoleAsync` allows it (G-1) | P-3 (b), decided under the locks of P-5 | A10.2.7, U, S, H |
| 2. A `permission.assign` holder adds a permission they do not hold | `SetRolePermissionsAsync` allows it | P-2 on the added set | A10.2.8, U, S, H |
| 3. A `role.manage`-only custom role acts as root | The implication (G-2) | P-6 deletion plus the P-32 data step | A10.2.2, A10.2.6, U, S |

### 7.2 Acceptance criteria

**IAM-10.1**

| # | Criterion | Level |
|---|---|---|
| A10.1.1 | Every registered permission has a display name, description, category and sensitivity; the test pins the P-15 classification | U |
| A10.1.2 | The module registrar rejects an incomplete declaration naming the module; the five in-tree modules register | U |
| A10.1.3 | No name or description contains a planning label, epic or track identifier, or migration name | G |
| A10.1.4 | The catalog endpoint exposes the new fields; `Source` and `ModuleId` come from `Origin` | U, H |
| A10.1.5 | The dead scaffolding no longer exists; the two named catalog exceptions of P-6 are listed in one place | G, U |

**IAM-10.2a, equivalence and held-set**

| # | Criterion | Level |
|---|---|---|
| A10.2.1 | **Differential equivalence (slice 1):** the current evaluator and `EffectivePermissionSet.Allows` agree over generated role shapes (empty, blank, malformed and element-malformed JSON; `role.manage`-only; the administrator with empty JSON; null-resource grants; module, slot and instance scopes; several roles; marker sessions) against the corpus of production checks of §2.2. **Corpus guard:** a new production check of a §2.2 combination fails the guard until added | U, G |
| A10.2.2 | After slice 3 a role holding only `role.manage` is refused rows 2 to 8 and 10 of §2.2 unless it holds them explicitly. **This replaces the test that pins the implication** | U |
| A10.2.3 | `Allows` table: case differences in action and type; a grant with no resource; instance, module and slot containment including `module:X` not holding `module:Y`; several roles; an unreadable role contributing nothing without hiding the others; the floor with empty JSON; an unresolved or non-human actor; target scope taken from the argument, not `permission.Resource.ScopeKey` | U |
| A10.2.4 | Floor ⊆ repair arrays ⊆ the administrator's stored grants after startup; the only non-catalog stored grants are the two named exceptions | U, S |
| A10.2.5 | **Window monotonicity (P-39):** over pre-step data, the new evaluator's allow-set is a subset of the old evaluator's, for every generated shape | U |
| A10.2.6 | **Preservation (P-32):** on a catalog seeded in the previous schema (custom roles with only `role.manage`, with some of the eight, with none, with no-resource grants, with unreadable JSON), after the step every production check of §2.2 gives the same answer as before; the step adds exactly the missing grants; a second run changes nothing; one summary event lists the changes; the role editor data shows explicit grants | S |

**IAM-10.2a, rules, concurrency and contract**

| # | Criterion | Level |
|---|---|---|
| A10.2.7 | Self-promotion and colleague promotion to Platform Administrator are refused `platform_administrator_restricted` (403) whether or not the target already holds it; nothing changes; one refusal row | U, S, H |
| A10.2.8 | A grant outside the actor's held set is refused `grant_exceeds_authority` listing up to 20 missing identities; the role is unchanged | U, S, H |
| A10.2.9 | Assign: a role exceeding the actor is refused; a subset role succeeds; self-assignment of a subset role changes nothing. Remove (P-4): the same entitlement; the order of P-4 (authorization refusal before no-op; no-op silent and unaudited; state refusal only for a real change); a user can remove a role they hold from themselves. Module-scoped matrix (an actor with only `module:X` grants can grant only `module:X`). An unreadable role is refused on assign and can be replaced | U, S |
| A10.2.10 | **Focused race tests (P-5)**, each in both isolation modes, each with a deterministic interleaving (one session holds an uncommitted change; the operation is shown waiting; the change commits; or the reverse using the unit's re-authorization callback as the pause point): **RT-1** the actor's assignment is removed, writer first and operation first; **RT-2** a permission is removed from a role the actor holds; **RT-3** the target role is edited while it is assigned (never "R plus P assigned by an actor lacking P"); **RT-4** two replacements of the same role by different actors; **RT-5** the actor edits a role they hold; **RT-6** two identical assignments (one row, one audit row); **RT-7** a deadlock victim reruns once under a **recording execution strategy**, recomputes the decision, writes no refusal row; **RT-8** the startup repair writes the administrator role while an assignment of it is in flight; **RT-9** the target role is deleted while it is assigned (the existing IAM-8 test extended) | S |
| A10.2.11 | **Property test:** over randomized sequences of create, replace, assign and remove by a non-administrator actor (in-memory fakes), no step increases that actor's effective set | U |
| A10.2.12 | M6 for each audited unit that changes: audit-insert failure leaves nothing; a lost acknowledgement is verified, not rerun; a confirmed rollback reruns once; an unknown outcome is wholly committed or absent; the operation lock is not granted | S |
| A10.2.13 | **Typed refusals (P-37) through the host:** every code and status of the table; a business refusal is never 404; a genuine not-found is 404; an `AuditInvariantException` and an unknown `InvalidOperationException` are 500; `outcome_unknown` and `committed_result_unavailable` are unchanged; the touched actions contain no `catch (InvalidOperationException)`; the roles routes still carry `error` | H, G |
| A10.2.14 | The startup repair is audited, writes one event only when it changes something, adds missing and removes obsolete grants | S |
| A10.2.15 | The grants and enforcement switches of P-17 and P-19 are exact: recipients as named; the old permission alone no longer authorizes the routes (no dual-accept); the new capability flags are correct | S, U |
| A10.2.16 | Writer guard (P-7): both mechanisms and the role-JSON writers are enumerated; a new production writer fails the test. Provisioning with an initial role the approver could not assign is refused | G, U, S |
| A10.2.17 | The three consumers of P-18 agree with the APIs for a role holding only `user.read` on User; `CanManageUsers` is false and `PUT .../username` is 403 for it; no test pins `user.read` on Role | U, H |
| A10.2.18 | Slice 1 and 2 ship with no change to any check outside the new write restrictions (the A10.2.1 differential result is attached to the slice 2 review) | U |

**IAM-10.2b**

| # | Criterion | Level |
|---|---|---|
| A10.2b.1 | **Sequential and concurrent:** for every pair of covered mutations (role removal, Local and Entra link removal, credential disable, the policy, configuration change; and two of the same kind on different users), two concurrent requests that together would leave no viable ordinary administrator: exactly one commits, the other is refused with its existing audit row, nothing changed; **the test fails with the gate removed.** The same two operations run one after the other: the second is refused | S |
| A10.2b.2 | The sequential hole: two administrators who both sign in only locally cannot both have their credential disabled | S |
| A10.2b.3 | **Viability is not assignment count:** an administrator with a disabled credential and no proven Entra link is not viable; removing the other administrator's role is refused although two assignments exist | S |
| A10.2b.4 | Recovery Access is never counted: removing the last ordinary administrator is refused although Recovery Access is healthy and holds the role | S |
| A10.2b.5 | `pre` empty never refuses; a mutation that cannot reduce the set is never refused; a no-op for the last administrator is a silent no-op | U, S |
| A10.2b.6 | Removing an **Entra** link from the last viable administrator is refused (it was unguarded) | S |
| A10.2b.7 | Entra registration and sign-in policy changes that would leave no viable administrator are refused beside `ValidateNoLockout` (if OQ-10 is accepted) | S, H |
| A10.2b.8 | F-10B: two concurrent applications of the Entra-primary policy for the last two Entra-independent administrators while Recovery Access is unhealthy: exactly one credential is disabled, the other records the deferral row | S |
| A10.2b.9 | Coverage guard: every writer of the administrator role's `UserRoles` rows, credential disablement, identity links, Entra registrations and the designation flag is in the §3.2 table and calls the guard | G |
| A10.2b.10 | A held gate past the bound gives `governance_operation_busy` (409) and nothing changes; the merged-snapshot and `EntraConfigurationState` notions of "usable" agree on a generated corpus | S, U |

**IAM-10.3 and 10.4**

| # | Criterion | Level |
|---|---|---|
| A10.3.1 | `DecideAsync` returns each reason for the matching cause; the `bool` overload equals `Outcome == Allowed` over a corpus of call-site shapes | U |
| A10.3.2 | **No step-up exemption:** a flagged permission is denied by the `bool` path without recent strong authentication on **every allow path** (stored grant, floor, marker session), allowed with it, `StepUpNotConfigured` with no mechanism; a native Recovery Access session satisfies it through native TOTP; a legacy marker session does not; a guard fails on a flagged permission evaluated for a non-current actor | U, G |
| A10.3.3 | An enforced denial of an Elevated or Critical permission writes exactly one row; a Standard denial none; capability probes and `DecideAsync` none | U, S |
| A10.3.4 | Capability flags `CanSearchDirectory` and `CanVerifyConnections` match the routes | U, H |
| A10.4.1 | The role editor and picker show display name, description, category and a sensitivity marker; no raw action string is a primary text | B |
| A10.4.2 | Each code of P-37 renders a specific message and keeps the loaded page; unknown codes fall back to the generic message | B |
| A10.4.3 | The user and role pages offer only the roles the actor may assign; the D-12 statement is in the administrator and module documentation | B |

**IAM-10.5a and 10.5b**

| # | Criterion | Level |
|---|---|---|
| A10.5a.1 | A native Recovery Access session (password, then MFA, through the host) performing an audited action writes a row marked `viaRecoveryAccess` | S, H |
| A10.5a.2 | An ordinary administrator performing the same action is not marked | S, H |
| A10.5a.3 | A legacy break-glass session is still marked until 10.5b | S |
| A10.5a.4 | Designation moved or revoked: later rows follow the new state; earlier rows are unchanged | S |
| A10.5a.5 | A failed designation lookup fails the unit as an audit invariant failure and never writes an unflagged row; no caller can supply the flag | S, U |
| A10.5a.6 | The test that today builds a marker actor "as Recovery Access" is replaced by one using a designated user without the marker | S |
| A10.5b.1 | After removal, A10.5a.1, .2, .4 and .5 pass again with the marker code gone; the legacy endpoint, option and claim no longer exist; Recovery Access is unaffected | S, H |
| A10.5b.2 | `relationship.manage` is absent from the catalog and from every role; editing a role that held it is not refused | S, U |
| A10.5b.3 | Dormant types annotated; ADR-0012 and ADR-0013 carry the amendments, and ADR-0012 does not claim ownership evaluation; C-4 and C-5 corrected | G |

### 7.3 Migration test matrix

| Case | Pre-state | Expected | Level |
|---|---|---|---|
| Role with only `role.manage` | Implied access | The eight added; every production check unchanged | S |
| Role with `role.manage` and some of the eight | Partial | Only missing grants added | S |
| `role.manage` with a non-instance scope, or on a non-Role type | Not implying | Unchanged | S |
| Role without `role.manage` | Any | Unchanged | S |
| System role | Platform Administrator | Not touched by the expansion; the repair handles it | S |
| Grants with no resource, unreadable JSON, unreadable element | Legacy | Listed in the event; not changed | S |
| Holders of `schedule.manage` | Any | Gain `operational_team.manage` (OQ-5) | S |
| Holders of `auth.configuration.read` or `auth.entra.manage`; holders of `connection.read` | Any | Gain `auth.directory.search`; gain `connection.verify` | S |
| Holders of `user.read` on Role (the administrator) | Obsolete | Removed through the repair; the three consumers already on `user.read` on User | S |
| Second run; run with no custom roles | Migrated or clean | No change, no event | S |

## 8. Risks

| # | Risk | Mitigation |
|---|---|---|
| R-1 | A permission flagged for step-up locks out a function where no mechanism is configured | Only the IAM-11 recovery permission is flagged in RC1; `StepUpNotConfigured` is a distinct reason; OPS-1 owns making a mechanism part of the supported install |
| R-2 | The viability invariant refuses an operation that used to succeed | Intended; codes unchanged; the UI explains the cause (10.4); direction of this review |
| R-3 | The preserving step leaves `role.manage`-only roles root-equivalent, now explicitly | Visible in the editor and the event; the silent **path** is closed; only dev and test databases exist today (SEC-2 closure) |
| R-4 | `UPDLOCK, HOLDLOCK` on the actor's and role rows lengthens a transaction and can deadlock between overlapping units | Rare operator actions; a fixed lock order; a deadlock victim reruns (A10.2.10, RT-7) |
| R-5 | Enforced-denial rows could be inflated by an authenticated user | Only enforced Elevated and Critical denials write; no probe writes; a throttle is a separate ruling |
| R-6 | The floor and the repair arrays drift | A10.2.4 |
| R-7 | A module author understates sensitivity | Declared classification only (ADR-0001); documented |
| R-8 | Deleted scaffolding has an undiscovered reference | `git grep` at package start; compile-time check |
| R-9 | Rebuilding the `UserRoles` key for the assignment constraint when the first context consumer ships | Additive until a user may hold the same role at two scopes; designed here so RC1 does not block it (P-27 (3)) |
| R-10 | Facts marked † were surveyed, not re-derived | Each package starts with a re-read of the cited lines; the acceptance tests are the proof |
| R-11 | The pre-correction audit rows of the real Recovery Access remain unflagged | Append-only table; documented in the dated AUD-1.2 note; no backfill |
| R-12 | The upgrade window (P-39) | Deny-only, proven by A10.2.5; OQ-11 |

## 9. What may start

IAM-10.0 is a **revised proposal** awaiting final owner review. Nothing in this record changes code. On approval, packages start one at a time on explicit instruction: IAM-10.1, IAM-10.2a, IAM-10.2b, IAM-10.3, IAM-10.4, IAM-10.5a, IAM-10.5b, with a review and CI checkpoint after 10.2a and 10.2b. IAM-10.5a may be started earlier than 10.2 since it depends only on this gate. The RC1 program gate amendment of §4.2 is applied in the change that records the owner's rulings. RC1 is not complete; the `role.manage` finding of the program gate §8 stays open until IAM-10.2a ships, A10.2.1 to A10.2.18 pass, and the security and product owners record the disposition before RCV-1 closes.

## 10. Revision record (Revision 1, 2026-10-15)

Corrections to claims in the first revision (local commit `458929e`), found by re-reading the source against the review points:

| # | First revision | Correction |
|---|---|---|
| 1 | "Eight explicit grants" were presented as the whole implied set | The implied set is **ten** combinations. Eight are catalog-valid grants added by the step; `user.read` on Role (not in the catalog, with **three** consumers) is replaced by switching them; `role.assign` on User is not in the catalog and has **no consumer**. The Platform Administrator holds nine of the ten explicitly (§2.2) |
| 2 | The data step "runs before the new evaluator serves requests" | **Unsupported.** Request serving is not gated on migration completion; the completion state feeds a readiness health check only. The design now relies on a deny-only window (P-39), not on ordering |
| 3 | P-6 called `workflow.execute` at instance scope "a named, administrator-only floor member" | **Wrong.** The evaluator's special authorization is the ten combinations only. `workflow.execute` at instance is an ordinary stored grant forced by the repair (G-4) |
| 4 | P-18 listed two consumers of `user.read` on Role and placed the switch in 10.3 | There are **three** (the governance dashboard card was missed), and the switch must ship in 10.2a slice 3 with the implication removal, or a `role.manage`-only role loses governance read in between |
| 5 | P-7 listed `EmergencyAccessService` and `FirstAdminBootstrapService` as role-assignment writers | They give the administrator role by creating a user that carries role ids (`SqlUserRepository.CreateAsync`), not through `AssignToUserAsync`. The guard now covers both mechanisms (G-19) |
| 6 | P-4 granted self-removal as an exception | Unnecessary: a user removing a role they hold satisfies Rule 2 trivially |
| 7 | G-10 said every `InvalidOperationException` maps to 404 | Also true of `AuditInvariantException`, which derives from it and whose documentation forbids that mapping; audit programming errors currently read as "not found" on role assignment |
| 8 | The viability predicate was "would leave no viable administrator" | Made exact: refuse iff `pre ≥ 1` and `post = 0` (so an already-empty instance can still be repaired) |
| 9 | P-10 said the busy case maps to "a retryable status" | The precedent is the ownership gate's 409 with a stable code; used here |
| 10 | P-29 allowed the per-operation credential re-enable gate to look like a standing exception, and did not place step-up relative to the marker branch | The rule now has no exemption on any allow path, including the marker and the floor; the re-enable gate is an unflagged permission, not an exemption |
| 11 | The pre-check outside the unit was implied to be sufficient | It is advisory only; the decision is inside the unit on locked rows (P-5) |
| 12 | P-36 / P-38 treated `viaRecoveryAccess` as an open question about ownership | Settled by direction: server-side designation, prerequisite to removal; attribution evidence added (one marker producer; a test that pins no marker for Recovery Access) |

Unchanged from the first revision: P-12 to P-16, P-20 (except sequencing), P-21 to P-27, P-35, the conflicts C-3 to C-9 in substance, and the package order apart from the split of 10.2a into slices, the placement of the governance-read switch, and the split of 10.5.
