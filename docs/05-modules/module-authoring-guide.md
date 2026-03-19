# Module Authoring Guide

See [.ai/playbooks/module-sdk.md](../../.ai/playbooks/module-sdk.md) and [module-manifest-spec.md](./module-manifest-spec.md).

## Steps

1. Create module project(s) — Module, Contracts, optional UI
2. Define workflow(s) with parameter schema and step graph
3. Implement step handlers
4. Register workflows with the runtime
5. Provide manifest metadata

Sample.HelloWorld demonstrates the pattern.

## Operations (Execution MVP Contract)

Modules can also expose direct executable operations by implementing `IOperationDefinitionProvider` and returning `OperationDefinition` records.

For the current MVP, operation execution reuses the existing step-handler execution pipeline with a strict one-step mapping contract:

1. `OperationDefinition.Name` is treated as the single step/handler name to execute.
2. That handler must be registered by the module via the existing step-handler registrar path.
3. Operation execution is direct and single-step; use workflows for multi-step durable orchestration.

This contract is intentionally narrow to keep operations and workflows distinct while sharing the runtime substrate.
