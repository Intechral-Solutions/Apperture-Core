# Epic 20: Canonical Run Result and Operator-Visible Output

## Overview

Epic 20 completed the next major piece of the operator-facing run model by introducing canonical result and output visibility for completed runs.

By the end of Epic 19, the platform could answer three critical questions reliably:

- what this run is
- how it started
- what happened to it over time

What was still missing was the next obvious operator question:

- what did this run actually produce?

Epic 20 closed that gap in a bounded and disciplined way. It introduced persisted run-grain capture of successful execution output, explicit output availability semantics, bounded JSON serialization, and server-side projection of result/output information into the canonical run-detail surface.

This epic deliberately did not attempt to build a generalized artifact system, step-output history, or redaction framework. Instead, it established a truthful, minimal, reusable output model that can support future work without inventing semantics the platform does not yet own.

## Goals

Epic 20 focused on the following objectives:

- introduce a canonical persisted output model at the run level
- capture successful execution output in a bounded, operator-visible form
- distinguish final run outcome from output payload availability
- project result and output server-side through the canonical run query path
- render output states honestly in `RunDetail`
- prove the behavior using HelloWorld with real returned payloads

## Why This Epic Matters

Epic 20 completes the core operator-facing execution surface established across recent epics:

- **Epic 18** made run detail truthful and action availability server-owned
- **Epic 19** made execution history truthful through the canonical run timeline
- **Epic 20** made final result and output visible through a persisted, bounded, canonical model

With Epic 20 in place, the platform can now answer the four most important execution questions from a single run surface:

- What is this run?
- What can be done to it now?
- What happened during execution?
- What did it produce?

## Implemented

### 1. ADR and persisted schema for output capture

Epic 20 introduced a formal decision record for run output capture semantics:

- `docs/02-decisions/ADR-0019-run-output-capture-semantics.md`

A persisted output shape was added to `OperationRuns` through new fields on `OperationRunEntity`:

- `CapturedOutputJson`
- `CapturedOutputSummary`
- `CapturedOutputStatus`
- `CapturedOutputVersion`

A migration was added to create these columns:

- `20260320233000_AddRunCapturedOutputFields.cs`

`AppertureDbContext` was updated accordingly to configure the new fields.

This work established output capture as a real persisted concern rather than a UI reconstruction or transient processor behavior.

### 2. Canonical output capture helper

Epic 20 added a dedicated helper for bounded output capture:

- `RunOutputCapture`

This helper provides a single canonical path for transforming a returned object into persisted operator-visible output metadata and payload. It includes:

- JSON serialization using `System.Text.Json`
- bounded payload capture with a 64 KiB cap
- output summary extraction
- explicit capture status semantics
- graceful handling of serialization failure

The helper returns a structured result via `RunOutputCaptureResult`.

### 3. Explicit output availability vocabulary

Epic 20 introduced a clear status vocabulary to describe output capture state:

- `none`
- `empty`
- `captured`
- `truncated`
- `unsupported`

These statuses matter because output visibility is not binary. A run may have:

- no output
- an intentionally empty output
- a captured inline payload
- a payload that exceeded the inline capture budget
- a returned value that could not be serialized safely into the current model

This made the result surface more honest and avoided pretending that every successful run has a neat, fully displayable payload.

### 4. Processor-side capture on successful completion

Output capture was integrated into the canonical execution processors:

- `OperationStartRequestProcessor`
- `WorkflowStartRequestProcessor`

For operations, the processor captures the successful returned result of the operation execution path.

For workflows, the processor captures the final step return value on successful workflow completion.

In both cases, capture happens only on successful terminal completion and writes to the persisted run entity fields.

This is an important boundary decision: output is captured at execution completion time, not inferred later from logs, history, or UI behavior.

### 5. Canonical server-side projection into run detail

Epic 20 extended result projection through the existing run-detail query path rather than inventing a separate output endpoint.

`RunResultDto` was extended with:

- `OutputAvailability`

`RunQueryService.BuildResult` was updated to project result/output information from the persisted captured-output fields, including:

- outcome
- summary
- payload JSON where applicable
- output availability state

This kept result/output truth aligned with the existing canonical run-detail model.

### 6. RunDetail UI support for honest output visibility

`RunDetail.razor` was updated so operators can now see:

- final run outcome
- result summary
- output availability
- inline payload when capture is available for inline viewing

The page renders different messages and states for:

- `none`
- `empty`
- `captured`
- `truncated`
- `unsupported`

Inline payload is shown only for `captured` and `truncated`, and truncated output is presented explicitly as partial rather than complete.

This preserves semantic honesty and keeps the operator-facing surface aligned with what the platform genuinely knows.

### 7. HelloWorld proving path

HelloWorld was updated to prove the new output path using real serializable returned payloads.

Updated handlers include:

- `HelloWorldCompleteStepHandler`
- `HelloWorldDoWorkStepHandler`

These now return serializable objects such as:

- workflow completion payloads
- operation completion payloads

This proved that a generalized module/workflow execution path can feed operator-visible output through the canonical capture and projection model.

### 8. Tests and documentation

Epic 20 included targeted tests and documentation updates.

Tests added or expanded include:

- `RunOutputCaptureTests`
- `RunQueryServiceTests`

Covered scenarios include:

- `null`
- simple object capture
- string capture
- oversized payload truncation
- captured output projection into run detail

Documentation was updated in:

- `docs/04-api/contracts.md`
- `docs/02-decisions/Epic20-Run-Result-Output.md`

## Resulting Operator Experience

After Epic 20, `RunDetail` can now show four distinct classes of execution truth:

### Start and provenance
How the run was initiated and what parameters were used.

### Current action availability
What actions are currently valid for the operator to perform.

### Execution timeline
What happened over time, based on persisted lifecycle and control evidence.

### Final result and output
How the run ended and what output, if any, was captured for operator review.

This means the platform now has a much more complete operator-facing execution model without relying on fake history, UI reconstruction, or undocumented handler conventions.

## Key Architectural Decisions

### Output is run-grain, not step-grain
Epic 20 captures output at the run level, not as per-step history. This keeps the model small and honest.

### Output capture is bounded
The platform captures only bounded JSON output suitable for inline operator visibility, not arbitrary-sized artifacts.

### Output availability is explicit
The platform distinguishes between missing, empty, captured, truncated, and unsupported outputs rather than collapsing them into one vague result state.

### Projection remains server-owned
The UI does not determine output semantics. It consumes server-projected result/output truth.

### HelloWorld is a proving fixture, not a special case
The HelloWorld changes validate the general path without introducing bespoke module-specific handling.

## What Epic 20 Did Not Do

Epic 20 intentionally did not attempt to solve broader output and observability concerns.

Out of scope for this epic were:

- per-step output history
- artifact storage
- download/export endpoints
- generalized output redaction or classification policy
- rich output governance rules
- non-JSON output viewers
- large-scale reporting or observability features

These are future concerns and should be addressed explicitly rather than smuggled into the platform through ad hoc output capture behavior.

## Final Assessment

Epic 20 was a disciplined result/output epic.

It introduced a persisted, bounded, canonical model for operator-visible run output and integrated it into the existing run-detail architecture without fragmenting the execution surface. It kept output capture explicit, server-owned, and availability-aware. It also proved the model using HelloWorld in a way that generalizes to future modules.

In practical terms, Epic 20 established a stronger canonical answer to the final missing operator question:

- What did this run produce?

That makes Epic 20 a meaningful continuation of the run-model work established in Epics 18 and 19 and a strong foundation for future work around output governance, redaction policy, richer result semantics, and module-declared output intent.