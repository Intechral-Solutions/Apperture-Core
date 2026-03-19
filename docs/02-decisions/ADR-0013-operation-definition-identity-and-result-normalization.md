# ADR-0013: Operation Definition Identity and Result Normalization

## Status

Accepted

## Context

Operation execution MVP introduced first-class operation persistence and execution, but operator/API contracts still leaned on generic run/workflow naming. Cycle 2 requires operation-centric surfaces without introducing a new execution engine.

## Decision

1. Operation API identity is the stable `OperationDefinitionId` (`OperationDefinition.Id`).
2. `OperationDefinition.Name` remains execution-mapping metadata for the current single-step handler model.
3. Operator-facing operation discovery/start uses operation definition query contracts (`/operations`, `/operations/{operationDefinitionId}`).
4. Run query/read supports operation-centric filtering while retaining shared run substrate.
5. Run detail includes normalized result/error projection for operator clarity.

## Consequences

### Positive

- Stable operation references across persistence, API routes, and UI links.
- Clear separation between execution mapping metadata and external identity.
- Improved operator clarity without requiring workflow internals knowledge.
- No speculative operation-handler framework introduced.

### Trade-offs

- Shared run DTO shape still carries generic execution terms for cross-kind support.
- Result normalization is intentionally minimal and may need extension in later cycles.

## Non-goals

- Multi-step operation orchestration design.
- Module-custom operation start UI as default behavior.
- New operation execution runtime abstraction beyond existing step-handler reuse.

