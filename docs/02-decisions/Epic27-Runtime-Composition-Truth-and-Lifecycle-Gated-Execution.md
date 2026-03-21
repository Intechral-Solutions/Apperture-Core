# Epic 27 — Runtime composition truth and lifecycle-gated execution

## Status

Accepted (implemented)

## Context

Epic 26 established descriptor-driven lifecycle, persisted install/enable state, and activation filtering for many catalog surfaces. Gaps remained between **persisted/catalog truth** and **what a running host can actually execute**, and between **authorization** and **dispatch** for some paths.

## Decision

### Terms

- **Discovered** — Module assembly is present in the host’s DI module list; descriptor can be built.
- **Persisted enabled** — `ModuleInstallationStates` has the module installed with `Enabled = true` for the environment.
- **Effectively active (catalog)** — Persisted enabled **and** no validation issues (conflicts, missing descriptor, etc.); see `ModuleCatalogEntry.EffectivelyActive`.
- **Effectively executable on this host** — Same as effectively active for execution: definitions resolve only for modules in `GetEnabledDescriptorsAsync()` / `IModuleActivationFilter.IsEnabledAsync`.

### Execution surface inventory

See [Epic27-Execution-Surface-Inventory.md](./Epic27-Execution-Surface-Inventory.md).

### Lifecycle gating

- `WorkflowDefinitionResolver` and `OperationRegistryService` filter by `IModuleActivationFilter` (immediate).
- `WorkflowStartDispatchService` / `OperationStartDispatchService` return null when definitions are unavailable (immediate).
- `WorkflowStartAuthorizationService` and `OperationStartAuthorizationService` reject starts when the definition cannot be resolved (aligned with dispatch).
- `WorkflowModuleResolver` skips disabled modules so module-scoped workflow authorization does not target disabled owners.
- Workers (`WorkflowStartRequestProcessor`, `OperationStartRequestProcessor`) re-resolve definitions at process boundaries; failure uses `DefinitionNotExecutableException` with distinct messaging when the module exists in catalog but is not effectively active.

### In-flight, queued, and scheduled work

- **No new starts** for disabled modules: API dispatch and worker resolution both enforce this.
- **In-flight**: A run that already claimed the lease may complete the current `ProcessClaimedAsync` execution; on the next claim cycle, resolution runs again and fails if the module is no longer effective.
- **Retries**: Go through `IWorkflowStartDispatchService`; gated like new starts.
- **Scheduler**: `SchedulerHostStub` is not implemented; future scheduler must call `IWorkflowStarter` / dispatch only (already the architecture rule).

### Restart-bound surfaces

Enumerated in `RestartBoundSurface` and exposed via `HostEffectiveModuleSnapshot.RestartBoundExecutionSurfaces`:

- **StepHandlerRegistry** — Handlers registered at worker startup for all DI modules; not unregistered when disabled; invocation still requires a resolved definition.
- **ModulePermissionRegistration** — Permission descriptors seeded at control-plane process start; updates require restart.

### Visibility

- `GET /api/control/runtime-composition` returns `HostEffectiveModuleSnapshot` (modules, effectively executable ids, governed vs restart-bound surface lists).
- `IRuntimeModuleCompositionQueryService` is the application seam; `RuntimeModuleCompositionQueryService` implements it for the management host.

### Startup

- `ModuleCompositionBootstrapHostedService` (control plane and worker) logs and audits when modules are persisted enabled but not effectively executable (`runtime.composition.invalid_at_startup`).

### Audit

- `LifecycleExecutionAuditActionNames`: workflow/operation start blocked (when dispatch returns null and an actor exists), run failed due to definition unavailable (worker), invalid composition at startup.

## Consequences

- Operators can inspect composition via the runtime-composition endpoint and client `GetRuntimeCompositionAsync`.
- Disabled modules cannot pass start authorization for unknown/unavailable definitions and cannot receive new durable starts through dispatch.
- CLI `ManagementClient` usage was aligned with `StartWorkflowAsync` signature (correlation id parameter).

## Related

- Epic 26: [Epic26-Module-Lifecycle-and-Contribution-Registration.md](./Epic26-Module-Lifecycle-and-Contribution-Registration.md)
