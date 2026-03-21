# Epic 22: Failure diagnostics and operator-safe visibility

## Overview

Epic 22 extends the run model with canonical, run-grain failure diagnostics visibility. The goal is to answer: **why did this run fail, and what diagnostics are intentionally safe to show operators?**

## Implemented

- Added run-grain failure diagnostics persistence on `OperationRuns`:
  - `FailureSummary`
  - `FailureDiagnosticsJson`
  - `FailureDiagnosticsStatus`
  - `FailureDiagnosticsVersion`
  - `FailureDiagnosticsDisplayPolicy`
- Added `RunFailureCapture` helper for bounded, policy-aware failure capture.
- Wired failure capture into:
  - `WorkflowStartRequestProcessor` failure catch path
  - `OperationStartRequestProcessor` failure catch path
- Extended `RunResultDto` projection with:
  - `DiagnosticsAvailability`
  - `DiagnosticsDisplayPolicy`
  - bounded/conditional `DiagnosticsJson`
- Updated `RunQueryService` so workflow and operation failure projections are parity-aligned.
- Updated `RunDetail.razor` to present diagnostics availability/policy and summary-only/withheld cues.

## Semantics

- Failure diagnostics are a **run snapshot**, not timeline reconstruction.
- Inline diagnostics are only shown when diagnostics status/policy explicitly allow.
- Default failure capture policy is summary-only, avoiding raw exception dumps by default.

## Out of scope

- Observability/tracing platforms, raw stack trace export, broad redaction engines, per-step failure history redesign, artifact downloads.
