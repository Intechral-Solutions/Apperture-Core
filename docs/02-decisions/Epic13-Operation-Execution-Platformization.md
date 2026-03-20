# Epic 13 - Operation Execution Platformization

## Status
Completed

## Summary
This epic took operation execution from MVP plumbing to a coherent platform feature. Operations were strengthened as first-class platform concepts with stable identity, operator-facing read/query surfaces, metadata-driven input validation, normalized result/error projection, operation-centric routes, and corrected authorization semantics.

A corrective pass was completed to remove workflow/generic drift from operator-facing operation surfaces, make run-kind resolution deterministic, align validation behavior with the published contract, and close targeted test/documentation gaps.

## Goals
- Make operations a first-class platform concept at the API, UI, read-model, and authorization boundary
- Preserve reuse of the existing step-handler execution pipeline underneath
- Provide operator-facing operation definition, start, run list, and run detail surfaces
- Support metadata-driven inputs and normalized result/error rendering
- Clarify and harden operation authorization semantics
- Remove workflow-shaped leakage from operation-facing contracts

## Scope
### Included
- Stable operation definition identity and operator-facing contracts
- Operation definition query/list/detail endpoints
- Operation start route using stable identity
- Run list/detail support for operations
- Metadata-driven input schema and server-side validation
- Normalized result/error shape for operation run detail
- Operation authorization seams for read/start semantics
- Module authoring documentation updates for operation identity and metadata
- Corrective pass on operation-facing contract drift, validation alignment, run-kind resolution, and targeted tests

### Excluded
- Dedicated operation handler abstraction
- Multi-step operation execution redesign
- Custom module UI as the default operation input path
- Broad result-schema framework/versioning system
- Re-absorption of operations into workflow persistence concepts

## Major Architectural Outcomes

### 1. Operations remain first-class
Operations are now modeled as operation-centric concepts at the public boundary rather than being exposed as thin wrappers over workflow concepts.

### 2. Stable operation identity
Operation routes and contracts were anchored on stable `OperationDefinitionId` rather than relying on `Name` as the external identity.

### 3. Metadata-driven input path
Operations now expose input schema metadata sufficient for platform-driven validation and future platform-generated input forms.

### 4. Normalized result/error model
Operation run detail now includes normalized result and error projection for operator consumption.

### 5. Corrected operator-facing contracts
A corrective pass introduced operation-first run surfaces and cleaned up generic/workflow-shaped drift in operation-facing APIs.

## Implemented Changes

### Identity and read-model foundation
- Added operation definition query contracts/services
- Added operator-facing operation definition DTOs and input schema models
- Added:
  - `GET /operations`
  - `GET /operations/{operationDefinitionId}`
- Updated start route to:
  - `POST /operations/{operationDefinitionId}/start`

### Operator-facing run/query support
- Added/extended operation run list and detail projection
- Added run filtering support including operation definition filtering
- Added operation-specific run endpoints during corrective pass:
  - `GET /operations/runs`
  - `GET /operations/runs/{runId}`

### Authorization
- Added operation definition read authorization seam
- Wired definition visibility and start capability into operation query surfaces
- Clarified semantics:
  - visibility = `operation.read`
  - startability = execute/start authorization
  - `CanStart` reflects startability independently from visibility

### Input validation
- Added operation input validation service
- Enforced:
  - required
  - type
  - allowed values
  - `maxLength`
  - numeric `min`
  - numeric `max`
- Start endpoint now returns `400` for invalid/unsafe input rather than surfacing exceptions

### Result/error normalization
- Added normalized result/error contracts for run detail
- Populated result/error projection in run query service
- Rendered normalized result/failure sections in operator-facing run detail UI

### Deterministic run-kind resolution
- Removed per-row probing of workflow vs operation instance tables
- Replaced with deterministic ownership-based resolution from start-request tables

### Documentation and ADRs
- Updated API contracts docs
- Updated architecture docs
- Updated module authoring guide
- Added ADR for operation identity and result normalization

## Corrective Pass Outcomes
The corrective pass resolved:
- operation-facing contract drift
- brittle run-kind inference
- validation-contract mismatch
- ambiguous definition visibility vs startability semantics
- missing operation-centric seam tests
- documentation overclaiming or mismatch

## Testing
Targeted and regression test coverage added/updated around:
- operation definition query behavior
- input validation behavior
- operation run projection/filtering
- operation result/failure extraction consistency
- visible-but-not-startable semantics
- controller auth behavior for operation surfaces

## Documentation / ADRs
Updated:
- `docs/04-api/contracts.md`
- `docs/05-modules/module-authoring-guide.md`
- relevant architecture docs

Added:
- `ADR-0013-operation-definition-identity-and-result-normalization.md`

## Completion Notes
Epic 13 is complete after the corrective pass. It established a credible operator-facing operation platform slice without introducing speculative execution abstractions.

## Follow-on Impact
This epic directly enabled the next governance-focused work by proving:
- governed visibility vs action semantics
- stable resource identity
- platform metadata contracts
- operation-centric authorization at public boundaries