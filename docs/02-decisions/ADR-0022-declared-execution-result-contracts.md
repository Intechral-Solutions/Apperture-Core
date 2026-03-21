# ADR-0022: Declared execution result contracts

## Status

Accepted

## Context

After Epics 20-22, run-grain output and failure diagnostics are captured and projected coherently, but authoring semantics remain partially implicit:

- success intent is explicit only when handlers return `OperatorRunOutput`,
- plain `object?` returns remain compatible but convention-heavy,
- failure diagnostics intent is inferred from thrown exceptions and processor defaults.

The platform needs a small explicit authoring contract that unifies success and failure intent without breaking existing handlers.

## Decision

1. **Introduce `OperatorExecutionResult` as the preferred authoring helper**
   - Success helpers return a wrapper value:
     - `SuccessInline(payload)`
     - `SuccessSummaryOnly(payload)`
     - `SuccessWithheld()`
   - Failure helpers return a typed exception to throw:
     - `FailInline(summary, diagnostics?)`
     - `FailSummaryOnly(summary)`
     - `FailWithheld(summary)`

2. **Introduce `OperatorExecutionFailureException`**
   - Carries declared failure summary, diagnostics object, and diagnostics display policy.
   - `RunFailureCapture` recognizes it and honors declared policy before default behavior.

3. **Preserve backward compatibility**
   - Plain `object?` success returns remain supported.
   - Non-declared exceptions still use default failure capture behavior.

4. **Keep canonical runtime model unchanged**
   - Existing run-grain persistence, `RunOutputCapture`, `RunFailureCapture`, processors, projection, and `RunDetail` remain canonical.
   - Epic 23 adds authoring clarity, not a parallel execution-result pipeline.

### Minimal authoring example

For declared failure intent, handlers should throw `OperatorExecutionResult.Fail*`:

```csharp
// Declared operator-facing failure summary without inline diagnostics payload.
throw OperatorExecutionResult.FailSummaryOnly("Validation failed for the requested input.");
```

Ordinary exceptions remain valid and are still captured by default behavior, but `Fail*` makes operator-facing intent explicit.

## Consequences

- Module authors get one discoverable contract family for success and failure operator intent.
- Existing modules continue to work without changes.
- Success/failure intent is more symmetric and less convention-dependent.
- See ADR-0023 for preferred/supported/discouraged author guidance taxonomy and non-breaking validation signals.
