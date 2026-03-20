# Epic 18 Summary

## Overview

Epic 18 hardened the operator-facing run detail experience by making run detail pages more truthful, more actionable, and more tightly aligned with persisted execution state.

Where Epic 17 corrected control-center drill-down integrity and removed misleading summary semantics, Epic 18 took the next step by strengthening the canonical run-detail surface. The result is that operators can now see not just the current run snapshot, but also the persisted start-request provenance behind the run and the server-projected action availability that determines what can actually be done next.

This work deliberately avoided inventing fake execution history or UI-only control semantics. Instead, it extended the existing run-detail surface using persisted start-request data, canonical server-side action projection, and explicit authorization boundaries.

## Goals

Epic 18 focused on the following objectives:

- make run detail more truthful by surfacing persisted start-request metadata
- move run action availability out of page-local UI logic and into a server-side projection seam
- ensure run actions are authorized and computed consistently across workflow and operation runs
- strengthen the run-detail page as the canonical operator-facing execution surface
- use HelloWorld as a proving fixture for generalized execution UX rather than as a special-case path

## Key Outcomes

### 1. Persisted start-request provenance is now visible on run detail

`RunDetailDto` was expanded so run detail can surface the originating start-request metadata for both workflow and operation runs. The run-detail projection now includes:

- `StartIdempotencyKey`
- `StartCorrelationId`
- `StartParametersJson`
- `StartRequestedAt`
- `StartClaimedBy`
- `StartClaimedAt`

These fields are projected from persisted start-request records rather than reconstructed in the UI. This makes the run-detail page more honest about how a run began, what parameters were used, and what request-level execution context existed at the start of processing.

### 2. Run action availability is now projected server-side

Epic 18 introduced a dedicated server-side query seam for run action availability:

- `IRunActionAvailabilityQueryService`
- `RunActionAvailabilityQueryService`

This service reuses the existing control action availability projection path and computes run actions from server truth rather than page-local heuristics. It also enforces read authorization based on run kind and incorporates queued park/resume state using persisted `WorkflowExternalControlRequests`.

This work established a cleaner separation of concerns:

- the server decides which execution actions are available
- the UI renders those decisions
- authorization remains explicit and centralized
- the meaning of an enabled or disabled action no longer depends on page-specific logic

### 3. Run detail UI now reflects canonical server truth

The `RunDetail` page was updated to consume the new action availability endpoint and render action availability using server-projected results. Operator controls such as:

- `execution.cancel`
- `execution.retry`
- `execution.park`
- `execution.resume`

are now enabled or disabled based on server-side projection rather than inferred client behavior.

The page also now includes a dedicated **Start request** section that displays the persisted start-request metadata and parameters JSON for the current run.

This reinforces `RunDetail` as the canonical operator-facing execution surface rather than a thin status page with hidden logic.

### 4. HelloWorld was used as a proving fixture

HelloWorld-backed tests were used to validate that persisted workflow parameters flow through the run-detail projection and that action availability behaves correctly through the canonical projection path.

This is important because HelloWorld was used to prove generalized platform behavior rather than to justify ad hoc or module-specific implementation.

## Architectural Impact

Epic 18 improved the platform in several architectural ways.

### Semantic honesty improved

Run detail now shows persisted execution provenance instead of implying or reconstructing it. The UI is closer to the real execution model.

### Canonical control semantics improved

Action availability is now determined by a dedicated server-side query seam rather than by duplicated UI logic.

### Authorization boundaries remained explicit

The run-actions projection enforces read authorization based on run kind, preserving the distinction between workflow and operation visibility.

### Existing projection seams were reused

The new query service reuses `IControlActionAvailabilityProjection.ProjectExecutionRunActionsAsync` rather than introducing a parallel action computation model.

### Future operator-facing execution work now has a stronger base

Epic 18 strengthens the foundation for later work such as:

- honest execution timeline/history visibility
- result and output modeling
- richer operator observability
- clearer lifecycle state understanding

## Main Code Changes

### Contracts and query services

- Expanded `RunDetailDto`
  - `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunQueryService.cs`
  - `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs`

- Added run action availability query seam
  - `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunActionAvailabilityQueryService.cs`
  - `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunActionAvailabilityQueryService.cs`

### Projection and service implementation

- Populated run-detail start-request fields in:
  - `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunQueryService.cs`

- Registered new query service in DI:
  - `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs`

### API surface

- Added run actions endpoint:
  - `GET /runs/{id}/actions`
  - `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/RunsController.cs`

### Client and UI

- Added `GetRunActionsAsync` to management client:
  - `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs`
  - `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs`

- Updated run detail page:
  - `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor`

## Testing and Validation

Epic 18 included targeted automated test coverage for both the new action-availability seam and the expanded run-detail projection.

### Added or expanded tests

- `tests/Apperture.UnitTests/ControlPlane/RunActionAvailabilityQueryServiceTests.cs`
- `tests/Apperture.UnitTests/ControlPlane/RunQueryServiceTests.cs`
- `tests/Apperture.UnitTests/RunsControllerTests.cs`
- `tests/Apperture.UnitTests/OperationsControllerTests.cs`

### Covered scenarios

- queued park behavior
- operation run action shape
- unauthorized read behavior
- schedule-free `execution.*` action shape
- workflow and operation start-request metadata mapping
- HelloWorld parameter projection through run detail
- new runs controller action endpoint behavior for `404`, `403`, and `200`

### Verification runs

Targeted test execution passed successfully, including:

- `RunQueryServiceTests`
- `RunActionAvailabilityQueryServiceTests`
- `RunsControllerTests`
- `OperationsControllerTests`
- `UnifiedControlFacadeTests`

Reported result:

- Passed: 65
- Failed: 0

## What Epic 18 Did Not Do

Epic 18 intentionally did not attempt to solve everything about execution visibility.

It did not introduce:

- a canonical execution timeline
- fake workflow step history
- output or result modeling beyond persisted start-request visibility
- UI-driven action semantics
- duplicate execution truth models for different pages

That restraint was important. Epic 18 stayed bounded and strengthened the platform without introducing misleading abstractions.

## Final Assessment

Epic 18 was a disciplined execution-facing hardening pass.

It made the run-detail experience more credible by grounding it in persisted start-request data and server-projected action availability. It moved important execution truth out of the UI and into explicit server-side query seams. It preserved authorization discipline, reused existing control projection infrastructure, and validated the behavior using targeted tests and HelloWorld-backed proof scenarios.

In practical terms, Epic 18 established a stronger canonical answer to the following operator questions:

- What started this run?
- What parameters were used?
- When was it requested and claimed?
- What execution actions are actually available right now?
- Why are some actions unavailable?

That makes Epic 18 a meaningful bridge between Epic 17's control-surface integrity work and the next likely stage of platform maturity: honest execution history and eventually truthful result/output visibility.
