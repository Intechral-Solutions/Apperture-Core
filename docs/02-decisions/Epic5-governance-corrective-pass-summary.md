# Platform Governance Cycle — Corrective Pass Summary

## Status

Completed. Audit status: **Needs corrective pass before proceeding** → addressed. No scope expansion.

## Summary of corrective changes

1. **Registry collision semantics** — `InMemoryResourceDescriptorRegistry` now uses explicit duplicate handling: exact duplicate (same descriptor + family) is idempotent; conflicting duplicate (same key, different descriptor) throws `InvalidOperationException` with actionable diagnostics. Applies to both resource and permission registration.

2. **Module contribution neutrality** — `ModulePermissionRegistrar` no longer defaults missing `ResourceType` to `OperationDefinition` or assigns `ResourceFamily.Operation` unconditionally. Explicit `ResourceType` is required; omission throws with clear message. Generic contributions use `ResourceFamily.Core`.

3. **Identity validation** — ResourceType and ScopeKey (no leading/trailing whitespace; ResourceType must start with a letter), Action (no leading/trailing whitespace) enforced at registration boundaries.

4. **Role-permission assignment validation** — `UserManagementService.SetRolePermissionsAsync` validates each requested permission against `IResourceDescriptorRegistry.IsPermissionRegistered` before persistence. Unknown or malformed permissions throw `InvalidOperationException` with index and descriptor details. `RolesController.SetPermissions` maps this to 400 Bad Request.

5. **Focused governance tests** — Added tests for registry duplicate/collision behavior, module contribution validation (ResourceType required), role-permission assignment validation, `GovernanceController` (401/403/200), `RolesController` (400 on unknown permission, 403 on unauthorized), `SchedulesController`, `ScheduleGovernanceService`, and effective-permissions correctness.

6. **Docs and ADR reconciliation** — Updated `contracts.md`, `authorization-and-governance-foundation.md`, and ADR-0014 to match implementation: routes under `/api`, collision semantics, module contribution rules, role-permission validation.

## File-by-file change list

| File | Change |
|------|--------|
| `InMemoryResourceDescriptorRegistry.cs` | Explicit duplicate handling; conflicting throws; identity validation; `IsPermissionRegistered` |
| `IResourceDescriptorRegistry.cs` | Added `IsPermissionRegistered(PermissionDescriptor)` |
| `ModulePermissionRegistrar.cs` | ResourceType required; ResourceFamily.Core for contributions |
| `UserManagementService.cs` | Injected `IResourceDescriptorRegistry`; validation before `UpdatePermissionsAsync` |
| `InMemoryResourceDescriptorRegistryTests.cs` | Idempotent exact duplicate; conflicting duplicate (resource + permission); ResourceType validation; IsPermissionRegistered |
| `ModulePermissionRegistrarTests.cs` | Replaced default-ResourceType test with throws-when-omitted |
| `RolePermissionAssignmentValidationTests.cs` | New: unknown permission throws; known succeeds; empty list allowed |
| `GovernanceControllerTests.cs` | New: 401 no actor; 403 not authorized; 200 with data; effective-permissions |
| `RolesControllerTests.cs` | New: 400 unknown/malformed permission; 403 unauthorized; 204 success; 400 null permissions |
| `SchedulesControllerTests.cs` | New: 403 when CanManage false; 200 when true |
| `ScheduleGovernanceServiceTests.cs` | New: false when no actor; false when denied; true when allowed |
| Stub registries (WorkflowStart, OperationStart, etc.) | Added `IsPermissionRegistered` implementation |
| `contracts.md` | Routes `/api/...`; governance API notes (collision, validation, ResourceType) |
| `authorization-and-governance-foundation.md` | Registry semantics; module contribution; role-permission validation |
| `ADR-0014-governance-evaluation-seams-and-assignment-surface.md` | Implementation notes (corrective pass) |

## Decisions made

- **Duplicate semantics:** Exact same (descriptor + family) = idempotent. Same key, different descriptor/family = fail with `InvalidOperationException`. No silent drops.
- **Generic module contribution:** Require explicit `ResourceType`. Use `ResourceFamily.Core` for module-contributed permissions/resources/actions. Reject omission at registration.
- **Descriptor validation:** Minimal durable rules: no leading/trailing whitespace; ResourceType starts with letter (PascalCase). Enforced in registry at registration and in `IsPermissionRegistered`.
- **Role-permission validation:** Validate against active catalog before persistence. Reject unknown and malformed with index + descriptor in error message. Distinct wrapping for "not in catalog" vs "malformed" (validation exception from registry).

## Deferred items (intentionally left untouched)

- AuthorizationEvaluationContext audit emission
- Relationship and resource-ownership evaluation
- Machine/System actor role resolution
- Full policy authoring or ABAC
- Expansion of break-glass audit beyond UserManagementService mutations

## Verification

- **Build:** Succeeds.
- **Tests:** 194/194 unit tests pass (including 26 new governance-focused tests).
- **Sample.HelloWorld:** Unaffected; already provides explicit `ResourceType` for all permissions.
