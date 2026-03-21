# Epic 26 — Module lifecycle and contribution registration

## Status

Accepted

## Context

Module behavior was previously activated by host-level `IEnumerable<IModule>` composition and startup side effects. This made lifecycle state implicit and made it difficult to answer which module is installed/enabled and which contributions are actively applied.

## Decision

The platform now uses descriptor-driven module lifecycle truth:

- Modules produce explicit `ModuleDescriptor` objects through `IModule.BuildDescriptorAsync(...)`.
- Contribution declarations are typed (`workflow`, `operation`, `permission`, `navigation`) and gathered through one descriptor builder.
- Lifecycle state is modeled as:
  - `Discovered`
  - `InstalledDisabled`
  - `InstalledEnabled`
- A platform-owned `IModuleRegistryService` performs discovery, install/enable/disable/uninstall transitions, and emits structured blockers.
- SQL persistence stores installation/enabled state in `ModuleInstallationStates` per environment/module.
- Persistence is deployed through migration `20260321183051_AddModuleInstallationStates`.
- Active contributions are filtered through enabled descriptors:
  - workflow resolution
  - operation registry population
  - module manifest exposure for governance registration
  - navigation menu materialization
- Cross-enabled-module contribution collisions are rejected deterministically for:
  - workflow ids
  - operation ids
  - navigation ids
  - navigation routes
  - permission identities (`action:resourceType`)
- Lifecycle actions are audited via `IAuditService` with actor (when available), action, outcome, blockers, and effect classification.

## Lifecycle semantics

- `install`: persists module installation in disabled state. No executable activation.
- `enable`: marks module enabled and eligible for active descriptor projection.
- `disable`: marks module disabled and removes it from effective descriptor projection.
- `uninstall`: removes persisted installation state.

Effect semantics returned by lifecycle operations:

- `Immediate`: change is fully effective without restart (catalog/read behavior).
- `PartialUntilRestart`: control-plane/runtime descriptor projections change immediately, but already-loaded executable registrations may still require process restart for complete deactivation guarantees.
- `RequiresRestart`: operator restart/recomposition is required for full executable effect.

## Guardrails

- Disable/uninstall does not claim assembly unload.
- Uninstall/disable is blocked when contributed definitions are already referenced by persisted workflow/operation instances.
- Duplicate module IDs and cross-enabled-module contribution conflicts fail deterministically.
- Invalid persisted state is surfaced in module listing as validation issues and non-effective activation.

## Consequences

- Module presence and contribution state are queryable from `GET /modules` and `GET /modules/{id}`.
- Lifecycle operations (`install`, `enable`, `disable`, `uninstall`) are explicit control-plane actions with blocker details.
- Module listing distinguishes `enabled` from `effectivelyActive` and returns validation issues for invalid persisted states (for example, descriptor missing or enabled-set conflicts).
- Existing sample module behavior remains compatible and now advertises descriptor-driven navigation contributions.

## Deferred

- True in-process assembly unloading.
- External package repository/marketplace flows.
- Full cross-process hot add/remove orchestration.
