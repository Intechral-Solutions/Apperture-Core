# IAM-10.2a slice 3: implementation record

| | |
|---|---|
| Package | IAM-10.2a, slice 3 of 4 (the [IAM-10.0 design gate](iam10-design-gate.md) §6 calls it "one release unit"). |
| Branch | `feat/iam-10.2a-anti-escalation`, on top of slice 2. Not pushed, not merged. |
| Scope | Implication removal; the preserving startup data step; the governance-read switch; `operational_team.manage`, `auth.directory.search` and `connection.verify` grants and enforcement; the `CanSearchDirectory` and `CanVerifyConnections` flags; `maxSensitivity` on grant and assignment events; the upgrade-window proof. |
| Not in scope | Slice 4's audited administrator repair, IAM-10.2b viability and its global lock, Recovery Access, the UI, context scope, removal of `relationship.manage`. |

This record states what was built and the choices the gate left open. The contract is [docs/04-api/contracts.md](../../../04-api/contracts.md) (governance API notes) and the gate; where this record and the gate differ, this record says so under "Interpretations".

## What changed

| Area | Change |
|---|---|
| Evaluator | `EffectivePermissionSet` no longer implies anything from `role.manage`. The Platform Administrator floor is the eight combinations that remain (`user.read` on Role and `role.assign` on User left it). |
| Data step | `RoleAuthorizationDataStep` (Persistence) runs from `AppertureDatabaseMigrator` after the last migration, on every start, in one audited transaction (`SqlAuditedUnitOfWork`, system actor `system:data-step`), with the Roles table read under `UPDLOCK, HOLDLOCK`. It writes one `governance.authorization.grants_migrated` row only when it changed something. |
| Consumers | `CanViewGovernance`, `GET api/governance/descriptors` and the governance dashboard card check `user.read` on User. `OperationalTeamManagementService` checks `operational_team.manage`. The two directory-search routes check `auth.directory.search`. `VerifyConnectionAsync` checks `connection.verify`. The attention contact group service stays on `schedule.manage`. |
| Administrator repair lists | Add the three switched permissions (otherwise the administrator would lose team management, directory search and test-connect), drop `user.read` on Role from the lists, and gain an obsolete list that removes it. This is the existing unaudited repair. The audited repair that reconciles the lists with the catalog's administrator defaults is slice 4. |
| Flags | `CanSearchDirectory` and `CanVerifyConnections`, additive, last in both capability records (server and client), false by default. |
| Audit | New action `governance.authorization.grants_migrated`. Optional `maxSensitivity` (`standard`, `elevated`, `critical`) on `governance.role.created`, `governance.permission.assignment.updated` and `governance.role.assignment.created`. |

## The data step, exactly

For each custom role, over its stored grants, in this order (a grant "holds" a combination when action and resource type match ignoring case and the scope is exactly `instance`):

1. A holder of `role.manage` on Role is given each of the eight catalog permissions it does not hold: `permission.assign` and `role.assign` on Role; `auth.configuration.read`, `auth.ui_policy.manage`, `auth.entra.manage` on InstanceConfiguration; `user.read`, `user.manage`, `credential.manage` on User. Nothing is added for `user.read` on Role or `role.assign` on User.
2. A holder of `schedule.manage` on Schedule is given `operational_team.manage` on OperationalTeam.
3. A holder of `auth.configuration.read` or `auth.entra.manage` (including those step 1 just gave) is given `auth.directory.search`. A holder of `connection.read` on Connection is given `connection.verify`.
4. `user.read` on Role is removed, at any scope.

System roles are never touched. A role whose stored permissions cannot be read is listed and left exactly as stored. A grant with no resource is never changed; its role is listed. The summary records exact counts and, for at most 100 entries of each list, the changed role ids, the `roleId|+identity` / `roleId|-identity` changes, and the ids left alone. It records no name and no stored JSON.

## Interpretations the gate left open

- **When it runs.** The gate says "between EF migrations". Slice 3 adds no migration and the step needs no schema of its own, so there is no "between". It runs after the last migration on every start. It is idempotent and silent when there is nothing to do, so a start with nothing to change costs one locked read of the Roles table.
- **Broad grants.** "Grants carrying no resource are listed and left unchanged" is read as: the broad grant itself is never changed and its role is listed. A role that also holds `role.manage` on Role still receives the eight, because the stated invariant (every production check gives the same answer before and after) requires it. A broad grant is never a recipient of the team, search or verify rules: those name a resource type.
- **Unreadable roles** contribute no grants to any check today, so there is nothing to preserve. They are listed and left alone, which also keeps the deny-only window honest.
- **`connection.verify` is not additive to `connection.read`.** The gate says the route switches to the new permission, so a caller holding only `connection.verify` can test-connect and receives the connection detail in the response. Structure browse, by contrast, requires both. This is recorded in the API contract; requiring both is a one-line change if the owners prefer it.
- **A custom role holding only the obsolete `user.read` on Role** loses governance read and is not given `user.read` on User: that would widen it to every user API. Nothing in the catalog could create such a role through the API; it can exist only from hand-edited data.
- **Provisioning directory search** (`EntraProvisioningDirectoryService`, authorized by `user.manage`) is not one of the "two directory searches" of P-19 and is unchanged.

## A dependency the implication hid

`POST /api/roles` and `PATCH /api/roles/{id}` reported the committed role by reading the role's holders, which needs `user.read` on User. A role manager held that only through the implication, so once the implication was gone a manager without `user.read` got 403 for a role that had in fact been created. The two routes now report the holder count through `CountRoleAssignmentsAsync`, which needs only `role.manage` (the permission the mutation needed; the delete refusal already reports the same count to a `role.manage` holder). The holders list (`GET .../assignments`, role detail) still needs `user.read`.

## Known interim limitation (slice 4)

Rule 1 stops an actor from granting what the actor does not hold, and the administrator's stored grants are still the seed plus today's repair lists. The catalog permissions the administrator cannot yet hand out are pinned by `AdministratorGrantableCatalogTests`. The three permissions switched here are no longer on that list; `relationship.manage` is dormant, is not an administrator default and must not be added to make the list empty.
