# Module Types

**Status:** Placeholder — no formal module-type taxonomy or scaffolding templates are implemented in Apperture-Core today.

## What exists today

Modules are **workflow-centric packages** distinguished by behavior and manifest content, not by a declared `moduleType` field:

| Pattern | Shipped indicators | Example in repo |
|---------|-------------------|-----------------|
| **Workflow module** | `WorkflowDefinition` + step handlers | `Sample.HelloWorld` |
| **Workflow + operations** | Also implements `IOperationDefinitionProvider` | `Sample.HelloWorld` |
| **Integration / connector module** | Slot declarations + connector bindings; may use platform connectors (SQL, Campus Nexus, SFTP) | `Proof.CampusNexus.StudentReview`, `Unisa.CampusNexus.PaymentPlans` |
| **Procedure surface** | `ModuleManifest.Procedures` for integration-scoped launches | HelloWorld, Proof |

There is no manifest property that marks a module as "Connector" vs "REST" vs "Generic". Authors follow [module-authoring-guide.md](./module-authoring-guide.md) and declare only what the module actually provides.

## Aspirational categories (not implemented)

The following labels were sketched for future scaffolding; **do not use them as runtime contracts** until a future epic defines them:

- **Connector** — External system integration via slots and bindings
- **REST** — HTTP/API-focused workflows
- **Generic** — General-purpose workflow module

When module scaffolding ships, this document should be replaced with concrete templates and manifest conventions.

## See also

- [module-authoring-guide.md](./module-authoring-guide.md)
- [module-manifest-spec.md](./module-manifest-spec.md)
- [runtime-connection-resolution.md](../01-architecture/runtime-connection-resolution.md)
