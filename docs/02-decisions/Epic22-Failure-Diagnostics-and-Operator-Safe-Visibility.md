# Epic 22 Summary

## Overview

Epic 22 introduced canonical failure diagnostics and operator-safe failure visibility for runs.

Where Epic 20 and Epic 21 matured the success-path model for result/output capture and display intent, Epic 22 extended the same discipline to failures. Before this epic, the platform could tell operators that a run had failed and could often show timeline or lifecycle evidence that a failure occurred, but it lacked a coherent run-grain model for operator-visible failure diagnostics. There was no unified answer to questions such as:

- what failure summary should an operator see
- whether any diagnostics were captured
- whether those diagnostics are safe for inline display
- whether diagnostics were intentionally summary-only or withheld

Epic 22 closed that gap by adding a bounded, policy-aware, run-grain failure diagnostics model. It introduced persisted failure summary and diagnostics fields, a canonical capture helper, processor-side capture on failure paths, server-side projection into the existing run result contract, and UI support for diagnostics availability and policy.

This epic deliberately did not attempt to build a logging platform, tracing system, or raw exception viewer. Instead, it established a minimal but explicit model for operator-visible failure diagnostics that fits naturally beside the success-path output model introduced in Epics 20 and 21.

## Goals

Epic 22 focused on the following objectives:

- introduce a canonical persisted model for run-grain failure diagnostics
- distinguish failed outcome from operator-visible failure summary and diagnostics payload
- make failure diagnostics policy-aware and operator-safe by default
- align workflow and operation failure handling
- project diagnostics through the existing canonical run-detail path
- render missing, summary-only, inline, and withheld diagnostics honestly in `RunDetail`

## Why This Epic Matters

By the end of Epic 21, the platform could answer the success-path questions well:

- what did this run produce
- was that output intended to be inline, summary-only, or withheld

What remained under-modeled was the failure path:

- why did this run fail
- what diagnostic information is available
- what diagnostic information is safe or intended to be shown to operators

Epic 22 gave the run surface a failure model with the same kind of intentionality that success-path output had already gained.

## Implemented

### 1. ADR for failure diagnostics visibility

Epic 22 introduced a formal decision record for failure diagnostics:

- `ADR-0021-failure-diagnostics-visibility.md`

ADR-0021 defines the distinction between failure summary, diagnostics availability, diagnostics display policy, and inline diagnostics payload. It also documents what the platform does and does not guarantee, keeping the semantics honest and bounded.

Documentation updates also included:

- `Epic22-Failure-Diagnostics-and-Operator-Safe-Visibility.md`
- updates to `docs/04-api/contracts.md`
- updates to `.ai/memory/decisions-index.md`

### 2. Persisted failure diagnostics model on run records

Epic 22 added a new run-grain persisted diagnostics model to `OperationRuns` through fields on `OperationRunEntity`:

- `FailureSummary`
- `FailureDiagnosticsJson`
- `FailureDiagnosticsStatus`
- `FailureDiagnosticsVersion`
- `FailureDiagnosticsDisplayPolicy`

A new EF migration was added:

- `AddRunFailureDiagnostics`

This established failure diagnostics as first-class run data rather than UI reconstruction or incidental side effects from lifecycle history alone.

### 3. Canonical failure diagnostics capture helper

Epic 22 introduced:

- `RunFailureCapture`

This helper is the canonical path for bounded, policy-aware failure capture. It mirrors the architectural role that `RunOutputCapture` plays for successful output and keeps the run model coherent between success and failure paths.

The helper provides a stable place to determine:

- failure summary
- diagnostics payload
- diagnostics availability status
- diagnostics display policy
- bounded inline-safe diagnostics behavior

### 4. Processor-side failure capture

Both execution processors were updated so failed runs now capture and persist run-grain failure diagnostics:

- `WorkflowStartRequestProcessor`
- `OperationStartRequestProcessor`

This is an important architectural step. Failure diagnostics are now captured at the failure boundary in execution processing rather than inferred later from timeline events, raw lifecycle records, or UI stitching.

### 5. Failed lifecycle detail alignment

Failed lifecycle `DetailsJson` was updated to use captured diagnostics payload when present.

This helps keep lifecycle evidence and run-grain diagnostics aligned while still preserving the run-grain diagnostics fields as the primary operator-facing source of failure detail.

### 6. Projection and result model updates

`RunResultDto` was extended with:

- `DiagnosticsAvailability`
- `DiagnosticsDisplayPolicy`

Both security-side and management-client contracts were updated.

`RunQueryService.BuildResult` was updated to project diagnostics status, policy, and JSON into the canonical run result model.

Additional work included:

- workflow failure parity projection through `BuildWorkflowError`
- operation failure projection updated to prefer run-grain failure fields first

This keeps failure visibility within the same canonical run-detail model rather than requiring a separate diagnostics surface.

### 7. RunDetail UI support for failure diagnostics

`RunDetail.razor` was updated so the failure panel can now render:

- diagnostics availability
- diagnostics display policy
- honest missing-detail states
- summary-only and withheld cues
- inline diagnostics when policy and availability allow it

This is a major improvement over generic failed-state handling because the page no longer assumes that raw details always exist or should always be shown.

### 8. Tests and verification

Epic 22 added:

- `RunFailureCaptureTests`

Covered scenarios include:

- default summary-only behavior
- inline diagnostics behavior
- withheld behavior

`RunQueryServiceTests` was also updated to cover:

- workflow failed run projecting `RunErrorDto` plus diagnostics availability and policy
- operation failed run projecting inline diagnostics correctly

Verification included:

- targeted tests for `RunFailureCaptureTests` and `RunQueryServiceTests`
- full unit suite passing:
  - `288` passed, `0` failed
- UI build success for `Apperture.Management.Ui.Client`
- no linter errors on edited files

## Resulting Operator Experience

After Epic 22, operators can now distinguish between several different failure states in a more truthful way.

`RunDetail` can now communicate:

- that a run failed
- the operator-facing failure summary
- whether diagnostics were captured
- whether diagnostics are intended for inline display
- whether diagnostics are summary-only or withheld
- whether no diagnostics were available at all

This is a significant improvement over a generic failed status with loosely interpreted details.

## Key Architectural Decisions

### Failure summary and diagnostics are separate concepts

Epic 22 distinguishes:

- failed outcome
- operator-facing failure summary
- diagnostics payload
- diagnostics availability
- diagnostics display policy

A failed run can have a summary without inline diagnostics. It can also have diagnostics intentionally withheld.

### Failure diagnostics are run-grain, not ad hoc UI reconstruction

The canonical operator-facing failure information now lives on the run record and is projected through the run-detail query path.

### Failure diagnostics mirror the success-path output model

Epic 22 intentionally parallels the architecture introduced for successful outputs:

- capture helper
- persisted fields
- availability vocabulary
- display policy
- canonical projection through `RunResultDto`

This keeps the run surface coherent across both success and failure paths.

### Workflow and operation failure paths were aligned

Epic 22 made a deliberate effort to achieve workflow and operation parity so the failure-diagnostics model is not operation-only or special-cased.

### The platform does not claim broad safe-error disclosure

Epic 22 did not turn the platform into a comprehensive safe-diagnostics engine. It established a bounded operator-visible model and documented its limits.

## What Epic 22 Did Not Do

Epic 22 intentionally did not attempt to solve broader failure-observability concerns.

Out of scope for this epic were:

- raw exception dumping into operator UI
- broad logging or tracing platforms
- giant sanitization or redaction engines
- per-step failure history beyond existing modeled evidence
- incident-management workflows
- exportable diagnostic artifact systems
- broad support tooling
- complete elimination of all supporting-record error text concerns

These remain future concerns and should be addressed explicitly rather than folded into the epic informally.

## Final Assessment

Epic 22 was a disciplined failure-diagnostics epic.

It introduced canonical run-grain failure diagnostics, made diagnostics availability and display policy explicit, aligned workflow and operation failure handling, and integrated the result into the canonical run-detail model without fragmenting the operator surface. It also improved UI honesty by distinguishing summary-only, withheld, inline, and missing diagnostics states instead of assuming raw details always exist.

In practical terms, Epic 22 established a stronger canonical answer to the next major operator question:

- Why did this run fail, and what diagnostics are safe and useful to show?

That makes Epic 22 a meaningful continuation of the execution-model work established in Epics 18 through 21 and a strong foundation for future work around declared execution contracts, support-facing artifacts, and richer operator-safe diagnostics.
