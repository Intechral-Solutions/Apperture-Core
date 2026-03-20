# Epic 19 Summary

## Overview

Epic 19 added a canonical, operator-facing execution timeline for runs.

Where Epic 18 strengthened the run-detail surface by projecting persisted start-request provenance and server-side action availability, Epic 19 took the next disciplined step by adding truthful execution-history visibility. The platform can now present an ordered timeline of persisted execution evidence for a run without relying on UI reconstruction, fake workflow-step history, or inferred narratives that are not genuinely modeled.

This epic stayed deliberately bounded. It introduced a stable run-timeline contract, a server-side projection service, a dedicated API endpoint, client support, and a run-detail UI section that renders timeline events sourced only from persisted records.

## Goals

Epic 19 focused on the following objectives:

- introduce a canonical run timeline contract for operator-facing execution history
- project execution-history evidence server-side from persisted records only
- keep execution-history semantics honest and extensible
- reuse the run-detail page as the canonical operator anchor
- avoid inventing workflow-step history, output history, or fabricated causal narratives
- preserve authorization parity with existing run-read semantics

## Key Outcomes

### 1. Canonical run-timeline contracts were introduced

Epic 19 established a stable vocabulary and contract surface for run timeline projection:

- `IRunTimelineQueryService`
- `RunTimelineDto`
- `RunTimelineEventDto`
- `RunTimeline` event/source constants

These contracts provide a durable foundation for operator-facing execution history without overloading existing run-detail or action-availability models. The timeline now has an explicit shape of its own rather than being implied through scattered status fields or page-level reconstruction.

### 2. Execution timeline projection is now server-side and evidence-based

A dedicated `RunTimelineQueryService` was added to project run timeline events from persisted execution artifacts only.

The projection draws from:

- `OperationRuns`
- `WorkflowStartRequests`
- `OperationStartRequests`
- `WorkflowLifecycleEvents`
- `OperationLifecycleEvents`
- `WorkflowExternalControlRequests` for workflow runs

The service enforces deterministic ordering using `OccurredAt` and then `EventId`, which means the timeline is stable and reproducible rather than dependent on accidental query ordering.

This is the core architectural win of Epic 19: the server now owns the assembly of execution-history truth.

### 3. Timeline visibility is now exposed as part of a coherent /runs/{id} API family whose detail, actions, and timeline surfaces follow aligned projectability and authorization semantics.

Epic 19 introduced a dedicated endpoint:

- `GET /runs/{id}/timeline`

This makes execution-history visibility a first-class control-plane capability rather than an implementation detail buried inside a page. It also creates a reusable seam for future consumers beyond the current run-detail UI.

### 4. Run detail now includes an Execution timeline section that serves as the canonical historical surface for a run, avoiding overlapping or competing history narratives in the UI.

The `RunDetail` page was extended to load and render the canonical run timeline using the management client.

This keeps `RunDetail` as the canonical operator-facing execution surface and allows an operator to answer a critical new question:

- what has happened to this run, in what order, and based on what persisted evidence?

The UI renders ordered events including source and metadata, but does so by consuming server-projected timeline results rather than inventing its own event assembly logic.

### 5. Authorization and run-kind resolution were consolidated so that run detail, actions, and timeline all follow the same run classification and read-boundary semantics.

The timeline projection service enforces authorization parity with run reads. This is important because execution history is not merely decorative; it is operationally meaningful system state and should follow the same visibility discipline as the rest of the run surface.

### 6. Epic 19 preserved semantic honesty

Epic 19 did not attempt to fill the screen with speculative execution details.

It did not introduce:

- fake workflow-step history
- inferred output/result history
- UI-only reconstruction of run events
- duplicate history models for workflows vs operations
- overloaded action/history contracts

That restraint is what makes the epic successful. The timeline is smaller and stricter, but trustworthy.

## Architectural Impact

Epic 19 improved the platform in several important ways.

### Execution-history truth now has a canonical home

Before Epic 19, operators could see run provenance and current action availability, but there was no single authoritative account of what had happened to a run over time. That gap is now closed with a dedicated run-timeline query seam and contract.

### Persistence-backed observability improved

Execution visibility is now grounded in persisted artifacts already present in the system. This moves the platform closer to durable, operator-meaningful observability without prematurely introducing a broader logging or tracing abstraction.

### Run detail became more complete without becoming dishonest

Epic 18 made run detail more truthful. Epic 19 made it more narratively useful while still remaining honest. `RunDetail` can now show:

- how the run started
- what can be done to it now
- what has happened to it over time

### Future execution-facing work now has a stronger base

Epic 19 creates a clean bridge toward later epics such as:

- honest result/output visibility
- richer control-plane observability
- durable step execution history, if and only if the domain later models it properly
- broader operational lifecycle analytics

## Main Code Changes

### Contracts and query services

- Added canonical run timeline contracts and vocabulary:
  - `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunTimelineQueryService.cs`

- Added server-side timeline projection service:
  - `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunTimelineQueryService.cs`

### Dependency injection

- Registered timeline service in DI:
  - `src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.Governance.cs`

### API surface

- Added timeline endpoint:
  - `GET /runs/{id}/timeline`
  - `src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/RunsController.cs`

### Client contracts and implementation

- Added `GetRunTimelineAsync(string runId, ...)`
- Added client-side timeline DTOs:
  - `RunTimelineDto`
  - `RunTimelineEventDto`

Files:
- `src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs`
- `src/Apperture.ControlPlane/Apperture.Management.Client/ManagementClient.cs`

### UI

- Added **Execution timeline** section to run detail:
  - `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor`

### Documentation

- Updated API contract documentation to include:
  - `GET /runs/{id}/actions`
  - `GET /runs/{id}/timeline`

File:
- `docs/04-api/contracts.md`

## Persisted Evidence Sources Used

Epic 19 is notable because of what it chose to trust.

Timeline events are projected from persisted evidence already modeled by the system, including:

- run records
- workflow and operation start requests
- workflow and operation lifecycle events
- workflow external control requests

This matters because it means the timeline is not a UI convenience layer. It is a durable projection over actual control-plane records.

## Testing and Validation

Epic 19 included targeted tests around the new run-timeline seam and the new controller endpoint.

### Added or expanded tests

- `tests/Apperture.UnitTests/ControlPlane/RunTimelineQueryServiceTests.cs`
- `tests/Apperture.UnitTests/RunsControllerTests.cs`

### Covered scenarios

- timeline ordering
- timeline mapping from persisted records
- authorization behavior
- endpoint behavior for:
  - `404`
  - `403`
  - `200`

### Verification run

Targeted test execution passed successfully:

- `dotnet test tests/Apperture.UnitTests/Apperture.UnitTests.csproj --filter "RunTimelineQueryServiceTests|RunsControllerTests"`

Reported result:

- Passed: 19
- Failed: 0

Additionally, edited files were checked for lint issues and reported with no linter errors.

## What Epic 19 Did Not Do

Epic 19 intentionally did not attempt to solve all future execution-visibility concerns.

It did not introduce:

- workflow-step execution history unless truly modeled
- output or result history
- generalized observability beyond the run-timeline slice
- duplicate event-history models for different run kinds
- action-availability logic mixed into the timeline contract
- status snapshots presented as though they were durable historical events unless backed by persisted evidence

This was the right boundary. The epic remained coherent and honest.

## Final Assessment

Epic 19 was a disciplined execution-history epic.

It added a canonical run timeline backed by persisted evidence, exposed that timeline through a dedicated server-side query seam and API endpoint, and integrated it into the run-detail UI without compromising semantic honesty. The work preserved authorization parity, enforced deterministic event ordering, and avoided the common trap of inventing workflow history that the domain has not actually modeled.

In practical terms, Epic 19 established a stronger canonical answer to a new set of operator questions:

- What has happened to this run?
- In what order did those things happen?
- What persisted evidence supports that story?
- Which events came from lifecycle records versus start/control records?

That makes Epic 19 a meaningful follow-on to Epic 18 and a strong bridge toward later work such as truthful result/output visibility and richer execution observability, provided those future capabilities remain grounded in real domain modeling rather than UI invention.
