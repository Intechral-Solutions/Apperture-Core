# ADR-0019: Run Output Capture Semantics

## Status

Accepted

## Context

After Epic 19, operators can see run identity, provenance, action availability, and execution timeline—but not what a run actually produced. Step handlers return `object?` from `IWorkflowExecutor.ExecuteStepAsync`, yet processors discard the value and `OperationRunEntity` has no result/output columns. The DTOs suggest payload fields (`PayloadJson`, `DiagnosticsJson`) but persistence never populates them for successful runs.

Operators need a truthful answer to: "What did this run produce?"

## Decision

1. **Run-grain persisted capture**
   - Add nullable columns to `OperationRuns`: `CapturedOutputJson`, `CapturedOutputSummary`, `CapturedOutputStatus`, `CapturedOutputVersion`.
   - Canonical final output belongs on the run aggregate, not lifecycle events (which are a history stream; Epic 19 timeline already narrates history).

2. **Capture rule (MVP)**
   - Persist the **last executed step's return value** on **successful terminal completion** for both workflows and operations.
   - Workflow: last step in the linear completion path.
   - Operation: single handler step.
   - Do **not** capture on failure or cancel; do **not** persist per-step outputs.

3. **Status vocabulary**
   - `none`: no capture attempted (legacy runs, non-terminal).
   - `empty`: handler returned `null`.
   - `captured`: JSON serialized successfully and stored.
   - `truncated`: serialized but exceeded max size; truncated JSON stored.
   - `unsupported`: JSON serialization threw (e.g. unsupported types, circular graphs); no payload or summary stored.
   - See [ADR-0020](ADR-0020-operator-output-display-policy.md) for additive statuses `summary_only` and `withheld` (operator display policy); they do not replace the meanings above.

4. **Serialization guardrails**
   - JSON serialize with stable options (camelCase, no indentation for storage).
   - Truncate to fixed max byte budget at write time (e.g. 64 KiB).
   - There is **no** runtime type denylist: capture is best-effort JSON. If `JsonSerializer` throws, status is `unsupported`. Handlers/modules must follow existing contracts and **not** return secrets or internal objects as step results; the platform does not classify or redact return types in this MVP.
   - Never persist raw exception content from serialization failures.

5. **Versioning**
   - `CapturedOutputVersion` (int) for schema evolution; current version = 1.

## Consequences

### Positive

- Operators see honest, persisted output when modules return serializable values.
- Clear distinction between outcome (status-derived), failure (RunErrorDto), and output (captured payload).
- Single canonical run-result model; no duplicate workflow vs operation result families.

### Negative

- "Last step only" may surprise operators of multi-step workflows; must document and surface helper text.
- Truncation loses fidelity for large returns; no artifact store or download endpoint yet.

### Neutral

- Legacy runs show `none`; projection treats null status as `none` without crashing.
