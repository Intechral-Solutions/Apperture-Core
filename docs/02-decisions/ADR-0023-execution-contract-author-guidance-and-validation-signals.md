# ADR-0023: Execution contract author guidance and validation signals

## Status

Accepted

## Context

Epics 20-23 established truthful runtime capture/projection semantics and introduced declared authoring contracts:

- success intent via `OperatorExecutionResult.Success*`,
- failure intent via `OperatorExecutionResult.Fail*` / `OperatorExecutionFailureException`,
- backward-compatible legacy paths (plain returns, ordinary exceptions) remain supported.

Author guidance exists but is fragmented across interface remarks and ADRs, and there is no lightweight runtime signal that distinguishes declared vs legacy authoring paths.

## Decision

1. Define an explicit authoring taxonomy:
   - **Preferred:** `OperatorExecutionResult.Success*` and `OperatorExecutionResult.Fail*`.
   - **Supported fallback:** plain successful returns and ordinary exceptions.
   - **Discouraged:** new handler code that relies on fallback paths when declared contracts are feasible.

2. Keep runtime behavior backward-compatible:
   - No breaking changes to plain returns or ordinary exceptions.
   - No schema/API/UI changes.

3. Add lightweight validation signals:
   - Emit additive diagnostic logs at canonical processor seams indicating whether execution used declared or legacy contract paths.
   - Signals are guidance only; they do not alter run outcomes or capture semantics.

4. Consolidate author guidance:
   - Maintain one module-author guide section that explains selection criteria for `Success*` and `Fail*` variants.

## Consequences

- Module teams get a clear preferred path without false enforcement claims.
- Existing modules keep working unchanged.
- Operators and platform maintainers can observe adoption drift through non-breaking signals.
