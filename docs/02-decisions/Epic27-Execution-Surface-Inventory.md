# Epic 27 — Execution surface inventory

This inventory classifies module-influenced **execution** surfaces for lifecycle truth. Canonical narrative: [Epic27-Runtime-Composition-Truth-and-Lifecycle-Gated-Execution.md](./Epic27-Runtime-Composition-Truth-and-Lifecycle-Gated-Execution.md).

| Surface | Location | Classification | Notes |
|--------|----------|----------------|-------|
| Workflow start request resolution | `WorkflowStartDispatchService`, `WorkflowDefinitionResolver` | Lifecycle-governed immediately | `IModuleActivationFilter` at resolution |
| Workflow step execution | `WorkflowStartRequestProcessor`, `ResolveDefinitionOrThrowAsync` | Lifecycle-governed at next resolution | Definition unavailable → run fails |
| Operation start resolution | `OperationStartDispatchService`, `OperationRegistryService` | Lifecycle-governed immediately | Filter during registry init |
| Operation execution | `OperationStartRequestProcessor` | Lifecycle-governed at next resolution | Same pattern as workflow |
| Workflow start authorization | `WorkflowStartAuthorizationService`, `WorkflowModuleResolver` | Lifecycle-governed immediately (Epic 27) | Resolver skips disabled modules; auth fails if definition not resolvable |
| Workflow run read/cancel/retry auth | `WorkflowModuleResolver`, definition resolver | Partial | Retry of new run gated at dispatch; read/cancel use definition + resolver semantics |
| Scheduled dispatch | `SchedulerHostStub` | Not implemented | Future: `IWorkflowStarter` → dispatch → filtered resolution |
| Step handler registration | WorkerHost `Program.cs` | Restart-bound | Handlers registered for all DI modules; invocation gated by definition resolution |
| Module permission registration | `ModulePermissionRegistrationHostedService` | Restart-bound for updates | Seeds from `GetEnabledDescriptorsAsync` at host start |
| Navigation/menus | `MenusController` | Lifecycle-governed immediately | `GetEnabledDescriptorsAsync` |

Code references: see XML comments on the types above citing `Epic27-Execution-Surface-Inventory.md`.
