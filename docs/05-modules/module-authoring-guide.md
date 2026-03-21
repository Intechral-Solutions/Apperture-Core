# Module Authoring Guide

See [.ai/playbooks/module-sdk.md](../../.ai/playbooks/module-sdk.md) and [module-manifest-spec.md](./module-manifest-spec.md).

## Steps

1. Create module project(s) — Module, Contracts, optional UI
2. Define workflow(s) with parameter schema and step graph
3. Implement step handlers
4. Register workflows with the runtime
5. Provide manifest metadata

Sample.HelloWorld demonstrates the pattern.

## Execution Contract Authoring (Epic 23+)

Use one explicit model for operator-visible run semantics.

### Preferred patterns

- **Success intent:** return `OperatorExecutionResult.SuccessInline(...)`, `SuccessSummaryOnly(...)`, or `SuccessWithheld()`.
- **Failure intent:** throw `OperatorExecutionResult.FailInline(...)`, `FailSummaryOnly(...)`, or `FailWithheld(...)`.

### Supported fallback patterns

- Plain successful return values (`object?`) are still supported for backward compatibility.
- Ordinary exceptions are still supported and are captured by default failure behavior.

### Discouraged patterns

- New handlers relying on plain successful returns or ordinary exceptions when declared intent is feasible.
- Using implicit behavior where operator intent (inline vs summary-only vs withheld) should be explicit.

### Quick selection matrix

| Goal | Preferred authoring |
|---|---|
| Show inline operator payload on success | `return OperatorExecutionResult.SuccessInline(payload);` |
| Show only a success headline | `return OperatorExecutionResult.SuccessSummaryOnly(payload);` |
| Intentionally suppress success payload | `return OperatorExecutionResult.SuccessWithheld();` |
| Fail with summary-only diagnostics | `throw OperatorExecutionResult.FailSummaryOnly(summary);` |
| Fail with inline-safe diagnostics payload | `throw OperatorExecutionResult.FailInline(summary, diagnostics);` |
| Fail while withholding diagnostics payload | `throw OperatorExecutionResult.FailWithheld(summary);` |

## Operations (Execution MVP Contract)

Modules can also expose direct executable operations by implementing `IOperationDefinitionProvider` and returning `OperationDefinition` records.

For the current MVP, operation execution reuses the existing step-handler execution pipeline with a strict one-step mapping contract:

1. `OperationDefinition.Name` is treated as the single step/handler name to execute.
2. That handler must be registered by the module via the existing step-handler registrar path.
3. Operation execution is direct and single-step; use workflows for multi-step durable orchestration.

This contract is intentionally narrow to keep operations and workflows distinct while sharing the runtime substrate.

### Operation identity and metadata conventions

For operator/API surfaces:

1. Use `OperationDefinition.Id` as the stable `OperationDefinitionId`.
2. Treat `OperationDefinition.Name` as execution mapping metadata (single-step handler name), not external identity.
3. Keep `OperationDefinition.Id` stable once published; changing display labels should not break API routes or persisted run references.

### Input metadata contract

Modules can provide a JSON `ParameterSchema` with a `fields` array to drive platform-generated start forms and validation.

Per-field keys supported by current platform rendering/validation:

- `key` (required)
- `type` (`string`, `int`, `decimal`, `bool`, `datetime`, `enum`, `json`)
- `label`
- `helpText`
- `required`
- `defaultValue`
- `order`
- `allowedValues`
- `min` / `max` / `maxLength`

If schema metadata is unsupported or malformed, the platform falls back safely and may reject invalid input at API boundary.

Current server-side enforcement for declared metadata includes:

- required fields
- type compatibility (`string`, `int`, `decimal`, `bool`, `datetime`, `enum`, `json`)
- `allowedValues` (for enum-like constraints)
- `maxLength` for string inputs
- numeric `min` / `max` bounds

## Governance Contributions

Modules can contribute governance metadata in `ModuleManifest`:

- `Permissions` for direct permission declarations (existing behavior).
- `GovernedResources` for module-defined resource types and default scope keys.
- `GovernedActions` for module-defined action keys mapped to resource types.

Validation rules enforced at startup:

- action keys must be lowercase and dot-separated.
- resource type must be non-empty.
- invalid declarations fail startup registration with explicit diagnostics.
