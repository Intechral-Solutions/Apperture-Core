# ADR-0021: Failure diagnostics visibility and capture semantics

## Status

Accepted

## Context

After Epic 21, operator-visible success/output intent is explicit, but failure diagnostics are still uneven: operations project lifecycle failure details while workflow failures rely more on supporting records. The platform needs a small, truthful, run-grain model for operator-safe failure diagnostics without exposing raw exception dumps.

## Decision

1. **Run-grain failure diagnostics fields**
   - Add nullable fields to `OperationRuns`:
     - `FailureSummary`
     - `FailureDiagnosticsJson`
     - `FailureDiagnosticsStatus`
     - `FailureDiagnosticsVersion`
     - `FailureDiagnosticsDisplayPolicy`
   - This is the canonical operator-facing diagnostics snapshot for failed runs.

2. **Failure diagnostics vocabulary**
   - `none`: no diagnostics captured.
   - `summary_only`: operator summary captured; no inline diagnostics payload.
   - `captured`: inline-safe diagnostics payload captured.
   - `withheld`: diagnostics intentionally not exposed.
   - `unsupported`: diagnostics capture failed/unsupported.

3. **Capture path**
   - Processors (`WorkflowStartRequestProcessor`, `OperationStartRequestProcessor`) capture diagnostics in terminal failure catch paths through a shared helper (`RunFailureCapture`).
   - Default policy is `summary_only` to avoid accidental raw exception leakage.

4. **Projection and UI**
   - `RunQueryService` projects diagnostics availability/policy on `RunResultDto`.
   - `RunErrorDto` remains canonical failure block; message uses `FailureSummary` when present.
   - `RunDetail` renders diagnostics state explicitly and only renders inline diagnostics when status/policy permit it.

5. **Non-claims**
   - This ADR does not create a logging/trace platform and does not claim full redaction/classification guarantees.

## Consequences

- Workflow and operation runs gain parity for failure diagnostics projection.
- Operators can distinguish failed outcome from diagnostics visibility.
- The model stays bounded and compatible with existing run-detail architecture.
