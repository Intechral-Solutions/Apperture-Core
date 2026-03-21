# Epic 21: Operator output intent and display policy

## Overview

Epic 20 and ADR-0019 established **technical** capture status (`none`, `empty`, `captured`, `truncated`, `unsupported`) and bounded JSON persistence. Epic 21 adds **module-declared operator display intent** so serializable internal objects are not implicitly treated as operator-facing output.

## Goals

- Introduce `OperatorRunOutput` + `OperatorOutputDisplayPolicy` (`Inline`, `SummaryOnly`, `Withheld`) at the handler return boundary (ADR-0020).
- Extend `RunOutputCapture` to unwrap the wrapper and persist `CapturedOutputDisplayPolicy` plus additive statuses `summary_only` and `withheld`.
- Persist **no** full `CapturedOutputJson` for `SummaryOnly` or `Withheld` (reduce leakage risk).
- Project `OutputDisplayPolicy` and refined `PayloadJson` rules through `RunQueryService.BuildResult` / `RunResultDto`.
- Update `RunDetail.razor` for `summary_only` and policy visibility.
- Prove patterns in HelloWorld: workflow `SummaryOnly`, operation explicit `Inline`.

## Implemented

| Area | Details |
|------|---------|
| ADR | [ADR-0020](ADR-0020-operator-output-display-policy.md); ADR-0019 cross-reference for additive statuses |
| Types | `OperatorOutputDisplayPolicy`, `OperatorRunOutput` in `Apperture.Core.Application.Execution` |
| Capture | `RunOutputCapture.TryCapture` recognizes `OperatorRunOutput`; `RunOutputCaptureResult.CapturedOutputDisplayPolicy` |
| Persistence | `OperationRuns.CapturedOutputDisplayPolicy` (nullable `nvarchar(32)`); EF migration `AddCapturedOutputDisplayPolicy` |
| Processors | `OperationStartRequestProcessor`, `WorkflowStartRequestProcessor` assign display policy from capture result |
| API/UI | `RunResultDto.OutputDisplayPolicy`; `RunDetail` shows policy and summary-only note |
| HelloWorld | `HelloWorldCompleteStepHandler` → `SummaryOnly`; `HelloWorldDoWorkStepHandler` → `Inline` |
| Tests | `RunOutputCaptureTests`, `RunQueryServiceTests` (summary_only / withheld projection) |

## Out of scope (unchanged)

- Redaction engines, DLP, artifacts, per-step output history, definition-level default policy (deferred).

## Final assessment

Epic 21 keeps a **single canonical run output path** from Epic 20, adds honest **intent** without claiming cryptographic safety, and remains backward compatible for plain `object?` returns.
