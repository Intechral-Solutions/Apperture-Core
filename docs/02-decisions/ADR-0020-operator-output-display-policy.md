# ADR-0020: Operator output display policy

## Status

Accepted

## Context

[ADR-0019](ADR-0019-run-output-capture-semantics.md) defines **capture status**: whether bounded JSON could be serialized and stored (`none`, `empty`, `captured`, `truncated`, `unsupported`). Epic 20 treats any plain, serializable last-step return as operator-visible subject to those rules. That is honest technically but **does not encode module intent**: a handler may return a value for execution concerns that must not be shown inline (or at all) to operators.

## Decision

1. **Separate dimensions**
   - **Capture status** (ADR-0019): technical outcome of the capture pipeline, including Epic 21 extensions below.
   - **Display policy** (this ADR): what the module declared for operator surfaces when it opted into [`OperatorRunOutput`](../../src/Apperture.Core/Apperture.Core.Application/Execution/OperatorRunOutput.cs).

2. **Opt-in wrapper**
   - Handlers may return `OperatorRunOutput` from `IStepHandler.ExecuteAsync` / the last workflow step to attach `OperatorOutputDisplayPolicy`: `Inline`, `SummaryOnly`, `Withheld`.
   - **Plain `object?` returns** (non-wrapper) behave as Epic 20: implicit operator-inline-capable capture (display policy **null** in persistence).

3. **Persistence**
   - Add nullable `CapturedOutputDisplayPolicy` on `OperationRuns` with allowed values: `inline`, `summary_only`, `withheld`, or **null** (legacy / implicit inline).
   - Do **not** persist full `CapturedOutputJson` when policy is `SummaryOnly` or `Withheld`, to avoid future APIs leaking withheld bytes.

4. **Extended capture statuses** (additive; ADR-0019 vocabulary retained)
   - `summary_only`: serialization succeeded; a summary may be stored; full JSON is **not** stored by policy.
   - `withheld`: module policy; no payload or summary is stored.

5. **Projection** (`RunQueryService.BuildResult`)
   - `PayloadJson` is exposed only when capture status is `captured` or `truncated` **and** display policy is null or `inline`.
   - `RunResultDto.OutputDisplayPolicy` exposes the persisted policy string for UI and API consumers.
   - For succeeded runs with `withheld`, `Summary` uses an explicit platform message (not a fake payload).

6. **Non-claims**
   - Display policy is **module-declared intent**, not DLP, classification, or cryptographic protection.

## Consequences

- Modules can intentionally constrain operator visibility without a redaction engine.
- Operators can distinguish “unsupported capture” from “module withheld output” via status + policy.
- Plain returns remain backward compatible with Epic 20.
