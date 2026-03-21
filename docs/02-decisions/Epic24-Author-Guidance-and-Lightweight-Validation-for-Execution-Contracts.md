# Epic 24: Author Guidance and Lightweight Validation for Execution Contracts

## Overview

Epic 24 strengthened the **authoring experience** around declared execution contracts without changing the underlying runtime model.

By the end of Epic 23, Apperture had a coherent execution-contract system for both operator-facing runtime behavior and module-author intent:

- successful runs could expose operator-visible output through declared success contracts
- failed runs could expose operator-safe diagnostics through declared failure contracts
- canonical runtime capture and projection already flowed through the established run-grain model

What was still missing was stronger guidance for module authors. The preferred authoring path existed, but the platform still relied heavily on:

- ADRs
- interface remarks
- examples
- backward-compatible permissiveness
- author discipline

That left room for drift. Module authors could still fall back to plain returns and ordinary exceptions because those paths remained supported, but the platform did not yet make the preferred patterns explicit enough or surface lightweight signals when legacy patterns were used.

Epic 24 addressed that gap by introducing a formal author-guidance taxonomy, consolidating module-author documentation, and adding lightweight runtime validation signals at the canonical execution seams. This epic deliberately avoided hard enforcement, schema changes, or runtime semantic redesign. Its purpose was to make the right patterns clearer, more discoverable, and easier to reinforce without breaking compatibility.

## Goals

Epic 24 focused on the following objectives:

- make the preferred execution-contract authoring model explicit for module developers
- distinguish preferred patterns, supported fallback patterns, and discouraged patterns
- reduce author drift without introducing breaking changes
- add lightweight runtime signals that reveal when legacy execution patterns are being used
- keep guidance and validation aligned with the existing execution model from Epics 20–23
- prove the guidance model with a small declared-failure path in HelloWorld

## Why This Epic Matters

Epics 20 through 23 established a strong runtime and contract model:

- **Epic 20** introduced canonical run result/output visibility
- **Epic 21** introduced output display policy and intentional operator-output visibility
- **Epic 22** introduced canonical failure diagnostics and operator-safe failure visibility
- **Epic 23** introduced declared execution contracts for authoring success and failure intent

At that point, the platform had the right mechanics, but not yet enough author-facing structure to encourage consistent adoption.

Epic 24 matters because it shifts the platform from merely **supporting** declared execution contracts to **guiding authors toward them**. It does this honestly: not by pretending strict enforcement exists, but by documenting the preferred path clearly and surfacing lightweight runtime signals when authors rely on supported-but-legacy patterns.

## Implemented

### 1. ADR-0023 for author guidance and validation signals

Epic 24 introduced a formal decision record for execution-contract author guidance and lightweight validation signals:

- `docs/02-decisions/ADR-0023-execution-contract-author-guidance-and-validation-signals.md`

ADR-0023 defines the posture for this epic:

- guidance, not hard enforcement
- backward compatibility preserved
- explicit taxonomy for preferred, supported fallback, and discouraged patterns
- additive runtime signals rather than behavioral changes

This was an important decision because Epic 24 could easily have drifted into fake enforcement or vague “best practices” language. ADR-0023 keeps the intent concrete and honest.

### 2. Consolidated module author guidance

Epic 24 centralized execution-contract guidance in:

- `docs/05-modules/module-authoring-guide.md`

The guide now includes a clearer success/failure decision model for module authors, including:

- preferred success patterns
- preferred failure patterns
- supported fallback patterns
- discouraged patterns
- a quick success/failure selection matrix

This gives authors a more practical starting point than ADRs and scattered remarks alone.

### 3. Explicit taxonomy for authoring patterns

Epic 24 formalized the authoring taxonomy into three categories:

#### Preferred
Use the explicit declared execution-contract path:

- `OperatorExecutionResult.Success*`
- `OperatorExecutionResult.Fail*`

This is the recommended model for new module code.

#### Supported fallback
Legacy-compatible patterns remain supported:

- plain successful return values
- ordinary thrown exceptions

These are still valid and functional, but they are no longer the preferred path for new code.

#### Discouraged
New implicit usage where explicit intent is feasible is now discouraged.

This is an important subtlety. Epic 24 does not claim these patterns are forbidden. It simply makes the platform’s preference visible and consistent.

### 4. Lightweight runtime validation signals

Epic 24 introduced lightweight runtime signals at the canonical execution seams:

- `OperationStartRequestProcessor`
- `WorkflowStartRequestProcessor`

These signals classify execution behavior as:

- **declared**
- **legacy**

The implemented signal posture is:

- declared success/failure paths log at **debug**
- legacy success/failure paths log at **warning**

This is a good fit for the epic’s scope because it provides visibility into authoring drift without changing execution behavior, persistence, API contracts, or UI semantics.

### 5. No runtime semantic redesign

Epic 24 intentionally did **not** alter the underlying execution model introduced in earlier epics.

It did not redesign:

- run output capture
- diagnostics capture
- run result projection
- policy semantics
- persisted run-grain model
- UI behavior

Instead, it layered guidance and validation signals on top of the already-established runtime truth.

### 6. HelloWorld declared-failure proof path

Epic 24 extended HelloWorld with a minimal declared-failure path in:

- `HelloWorldValidateStepHandler`

Using the `simulateFailure == true` parameter, the handler now proves the preferred declared-failure pattern by throwing:

- `OperatorExecutionResult.FailSummaryOnly(...)`

This was an important addition because it gave the authoring guidance a concrete example in a real module fixture rather than leaving failure-path guidance purely theoretical.

### 7. Focused validation-signal tests

Epic 24 added and updated focused tests in:

- `OperationStartRequestProcessorTests`

These tests verify:

- legacy success path emits the expected legacy signal
- declared failure path does not emit the legacy failure signal
- runtime signal behavior remains aligned with the intended guidance taxonomy

The tests also use an in-test logger sink to verify emitted messages without changing runtime behavior.

## Resulting Author Experience

After Epic 24, module authors have a clearer and more actionable execution-contract authoring model.

Authors can now more easily understand:

- what the preferred success path is
- what the preferred failure path is
- what legacy behavior remains supported
- what kinds of new implicit patterns are discouraged
- how the platform will signal use of legacy paths at runtime

This does not enforce compliance, but it materially improves discoverability and consistency.

## Key Architectural Decisions

### Guidance is explicit, but non-enforcing

Epic 24 makes preferred patterns visible without pretending the platform has introduced strict enforcement.

### Validation signals live at the canonical processor seams

The signals were placed where execution truth already flows, rather than in a separate analyzer or metadata subsystem.

### Backward compatibility is preserved

Plain returns and ordinary exceptions still work. Epic 24 does not break existing modules.

### Success and failure guidance are treated symmetrically

The platform now gives parallel guidance for both:

- declared success intent
- declared failure intent

### Runtime semantics remain unchanged

Epic 24 strengthens author adoption without destabilizing the run model built across earlier epics.

## What Epic 24 Did Not Do

Epic 24 intentionally did not attempt to solve broader validation or enforcement concerns.

Out of scope for this epic were:

- hard enforcement of declared execution contracts
- removal of plain-return compatibility
- analyzer infrastructure
- SDK redesign
- schema changes
- API route changes
- UI redesign
- runtime outcome/output/diagnostics behavior changes
- broad plugin validation frameworks

These remain future concerns and should be addressed explicitly if and when the platform is ready for stronger enforcement mechanisms.

## Final Assessment

Epic 24 was a disciplined author-guidance and validation-signal epic.

It formalized the preferred execution-contract authoring model, clarified the difference between preferred and fallback patterns, centralized module-author guidance, and added lightweight runtime signals that surface drift without breaking compatibility or pretending strict enforcement exists.

In practical terms, Epic 24 established a clearer answer to the next important platform question:

- How should module authors use the declared execution-contract model, and how does the platform gently reinforce that choice?

That makes Epic 24 a meaningful continuation of the work established in Epics 20 through 23 and a strong foundation for future work around richer sample modules, stronger authoring ergonomics, and incremental validation or enforcement.