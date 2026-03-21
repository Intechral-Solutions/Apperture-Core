# Epic 21 Summary

## Overview

Epic 21 introduced intentional operator-output display policy for run results.

Where Epic 20 established canonical run-grain output capture and operator-visible result projection, Epic 21 addressed the next critical gap: the distinction between **capturing output** and **intending that output to be shown to operators**. Before this epic, the platform could persist best-effort JSON from successful handler return values, but it still relied mainly on implicit behavior. If a returned value was serializable and within bounds, it could become operator-visible output even when a module author had not explicitly declared how that output ought to be shown.

Epic 21 solved that problem by adding a small but explicit output-intent model. It introduced operator output display policy, a wrapper type for intentional operator-visible output, persistence of display policy alongside captured output, and policy-aware projection into the canonical run-detail surface.

This epic stayed deliberately bounded. It did not attempt to build a broad redaction engine, artifact subsystem, or generalized output-classification framework. Instead, it established a disciplined and extensible model for deciding whether operator-visible output should be shown inline, summarized only, or withheld.

## Goals

Epic 21 focused on the following objectives:

- introduce explicit operator-output display intent
- distinguish capture status from operator display policy
- allow modules and workflows to intentionally declare operator-visible output behavior
- preserve the canonical run-grain output model introduced in Epic 20
- prevent payload JSON from being persisted or shown when output is intended to be summary-only or withheld
- prove the policy model through HelloWorld without introducing special-case behavior

## Why This Epic Matters

Epic 20 answered an important question:

- what did this run produce?

Epic 21 answered the next one:

- what part of that output was actually intended to be shown to operators?

That distinction matters because successful execution return values are not automatically the same thing as operator-facing output. A module may want to expose:

- a full inline payload
- only a human-readable summary
- no operator-visible payload at all

Epic 21 made that intent explicit and kept it within the same canonical run model instead of spawning a separate output system.

## Implemented

### 1. ADR for operator output display policy

Epic 21 introduced a formal decision record for operator-output display intent:

- `docs/02-decisions/ADR-0020-operator-output-display-policy.md`

ADR-0020 defines the distinction between capture semantics and display policy, describes how display policy integrates with the persisted run-output model from Epic 20, and explicitly avoids making larger claims about full redaction or safety classification than the implementation actually enforces.

ADR-0019 was also updated to reference ADR-0020 for the additive display-policy-aware states introduced by Epic 21.

### 2. Operator output display policy vocabulary

Epic 21 introduced an explicit operator display policy model:

- `OperatorOutputDisplayPolicy`

The implemented display policy vocabulary is:

- `Inline`
- `SummaryOnly`
- `Withheld`

These policies allow a module or workflow to state how operator-visible output should be surfaced rather than leaving the decision to implicit JSON serialization alone.

### 3. Canonical wrapper for intentional operator-visible output

Epic 21 introduced:

- `OperatorRunOutput`

This wrapper provides explicit factories for the supported display behaviors:

- `OperatorRunOutput.Inline(...)`
- `OperatorRunOutput.SummaryOnly(...)`
- `OperatorRunOutput.Withheld(...)`

This is a major architectural improvement because it gives modules a platform-native way to distinguish:

- internal execution return value
- operator-facing output intent

Plain return values remain supported for backward compatibility and continue to follow the Epic 20 implicit-inline path, but `OperatorRunOutput` is now the canonical way to express intentional operator-visible output policy.

### 4. Policy-aware output capture

`RunOutputCapture` was extended to become display-policy-aware.

Key additions include:

- unwrapping `OperatorRunOutput`
- support for additive output statuses such as:
  - `summary_only`
  - `withheld`
- `RunOutputCaptureResult.CapturedOutputDisplayPolicy`
- explicit suppression of persisted JSON payload for summary-only and withheld output

This last point is especially important. Epic 21 did not merely hide payloads at projection time. It prevented payload JSON from being persisted at all for `SummaryOnly` and `Withheld` output, which keeps the model more truthful and safer.

### 5. Persisted display policy on run records

Epic 21 added persisted display-policy support on the run entity through:

- `OperationRunEntity.CapturedOutputDisplayPolicy`

`AppertureDbContext` was updated to map the new field, and a new EF migration was added:

- `20260321013739_AddCapturedOutputDisplayPolicy`

This allows display intent to survive execution completion and become part of the canonical run record rather than remaining a transient in-memory execution concept.

### 6. Processor integration for policy-aware persistence

Both execution processors were updated to persist output display policy alongside captured output:

- `OperationStartRequestProcessor`
- `WorkflowStartRequestProcessor`

This preserved the existing Epic 20 result/output capture architecture while extending it with explicit display policy.

### 7. Projection and API updates

`RunResultDto` was extended with:

- `OutputDisplayPolicy`

Both the security-side contract and management client contract were updated accordingly.

`RunQueryService.BuildResult` was updated so that:

- payload JSON is only projected inline when output status is `captured` or `truncated`
- and display policy is either `null` or `inline`

For summary-only and withheld output, payload JSON is not surfaced. Instead, the result projection passes through policy and summary information honestly.

### 8. RunDetail UI support

`RunDetail.razor` was updated to display:

- output availability
- output display policy
- inline payload only when policy and status allow it

The UI explicitly handles summary-only output with an explanatory note and avoids inventing “missing payload” messaging where the output was intentionally suppressed.

For withheld output, the page relies on result summary and output policy visibility rather than pretending the payload was simply unavailable by accident.

### 9. HelloWorld proving path

HelloWorld was updated to demonstrate two different operator-output policies:

- `HelloWorldCompleteStepHandler` now returns `OperatorRunOutput.SummaryOnly(...)`
- `HelloWorldDoWorkStepHandler` now returns `OperatorRunOutput.Inline(...)`

This is important because Epic 21 did not just add a wrapper type in the abstract. It proved both explicit summary-only and explicit inline behavior through a real module fixture.

### 10. Tests and documentation

Epic 21 added or expanded tests in:

- `RunOutputCaptureTests`
- `RunQueryServiceTests`

Covered scenarios include:

- withheld output
- summary-only output
- inline output
- plain object vs wrapped output behavior
- summary-only behavior with serialization failure
- summary-only and withheld projection behavior
- legacy captured output path still projecting with `OutputDisplayPolicy == null`

Documentation updates include:

- ADR-0020
- ADR-0019 note pointing to ADR-0020
- `docs/04-api/contracts.md`
- `docs/02-decisions/Epic21-Operator-Output-Display-Policy.md`
- `.ai/memory/decisions-index.md`

## Resulting Operator Experience

After Epic 21, operators can now distinguish not just whether output exists, but whether it was intended to be shown inline.

`RunDetail` can now reflect:

- output availability
- output display policy
- inline payload when explicitly allowed
- summary-only output when payload is intentionally not stored
- withheld output when operator-visible payload is intentionally suppressed

This is a much stronger operator-facing model than generic JSON capture alone.

## Key Architectural Decisions

### Capture status and display policy are separate concepts

Epic 21 preserved the distinction between:

- what happened during capture
- what the module intended operators to see

Even when the same token appears in both domains for wrapped output, the concepts remain distinct.

### `OperatorRunOutput` is the intentional-output path

The platform now has an explicit way to declare operator-facing output intent rather than relying only on implicit return-value capture.

### Summary-only and withheld output do not persist payload JSON

This is the most important discipline in the implementation. If a module says output should not be shown inline, the platform does not merely hide the payload later; it avoids persisting the payload JSON in the first place.

### Plain return values remain supported

Epic 21 preserved Epic 20 behavior for backward compatibility. Plain serializable returns still follow the implicit-inline path with `OutputDisplayPolicy == null`.

### The canonical run model remains unified

Epic 21 extended the existing run output model rather than creating a separate output platform.

## What Epic 21 Did Not Do

Epic 21 intentionally did not attempt to solve broader output-governance concerns.

Out of scope for this epic were:

- broad redaction engines
- full data-classification frameworks
- artifact storage or download endpoints
- per-step output history
- generalized security guarantees for arbitrary serializable objects
- removal of the plain-object fallback
- large-scale module contract registries

These remain future concerns and should be addressed explicitly rather than smuggled into the platform through ad hoc changes.

## Final Assessment

Epic 21 was a disciplined output-governance epic.

It introduced explicit operator-output display intent, extended the run-grain output model with persisted display policy, and ensured that summary-only and withheld output are handled truthfully at capture, persistence, projection, and UI layers. It also proved the model through HelloWorld in a way that generalizes to future modules.

In practical terms, Epic 21 established a stronger canonical answer to a new operator question:

- Was this output intended to be shown inline, summarized, or withheld?

That makes Epic 21 a meaningful follow-on to Epic 20 and a strong foundation for future work around safer output governance, richer module contracts, and support-friendly execution semantics.
