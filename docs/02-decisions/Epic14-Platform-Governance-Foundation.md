# Epic 14 - Platform Governance Foundation

## Status
Completed

## Summary
This epic established the first durable platform-wide governance spine for Apperture. It introduced shared authorization evaluation seams, governed resource/action contribution patterns, role-permission assignment surfaces, governance descriptor inspection APIs, and cross-domain application beyond operations.

A corrective pass was completed to remove operation-shaped defaults from generic governance contribution paths, harden registry collision semantics, validate role-permission assignment against canonical descriptors, strengthen governance tests, and align docs/ADR with implementation truth.

## Goals
- Establish a platform-wide resource/action governance model
- Keep authentication and authorization separate
- Allow disciplined module contribution of governed resources/actions
- Align authorization evaluation across API/application/service layers
- Introduce role-permission management surfaces
- Improve auditability for privileged governance actions
- Prove the governance spine beyond operations

## Scope
### Included
- Shared authorization evaluation seam
- Governance contracts and descriptors
- In-memory governance descriptor registry
- Module manifest extensions for governed resources/actions
- Module registration and startup validation/diagnostics
- Role permission read/update APIs
- Governance descriptor/effective-permissions APIs
- Governance application to workflows, connections, schedules, and key services
- Governance corrective pass

### Excluded
- Authentication redesign
- Full policy DSL or enterprise policy framework
- Universal governance coverage across every domain
- Full ABAC or relationship-based authorization
- Machine/runtime auth redesign
- Broad audit event system redesign

## Major Architectural Outcomes

### 1. Shared governance evaluation seam
A shared authorization evaluation seam now exists and is used in multiple services rather than relying on controller-only enforcement.

### 2. Disciplined module governance contribution
Modules can contribute governed resources/actions through manifest declarations and startup registration paths.

### 3. Role-permission assignment surface
The control plane now has a real role-permission read/update surface instead of ad hoc hidden internals.

### 4. Governance inspection surface
Descriptor and effective-permissions APIs were added to inspect the governance model.

### 5. Corrective hardening
The corrective pass removed operation-shaped defaults and strengthened identity, collision handling, and assignment validation.

## Implemented Changes

### Governance model foundation
- Added shared governance evaluation context and evaluation services
- Added governed action/resource descriptors and registry infrastructure
- Hardened descriptor registry with:
  - explicit duplicate semantics
  - conflicting duplicate rejection
  - identity validation
  - catalog lookup support

### Authorization evaluation alignment
Applied shared governance evaluation to core services including:
- workflow start
- operation start
- workflow listing
- connection management
- schedule governance surfaces

### Module contribution model
- Extended module manifests with governed resource/action declarations
- Added registrar validation and startup diagnostics
- Corrected generic registration to remove operation-biased defaults
- Required explicit `ResourceType` for generic module contributions

### Governance APIs
Added or expanded:
- `GET /api/governance/descriptors`
- `GET /api/governance/effective-permissions`
- `GET /api/roles/{id}/permissions`
- `PUT /api/roles/{id}/permissions`

### Role-permission assignment hardening
- Added validation of requested permissions against the canonical governance descriptor catalog before persistence
- Rejected unknown/malformed permission assignments with clear error behavior

### Audit conventions
- Added governance audit action naming conventions
- Applied canonical governance audit names to key privileged role/permission operations

## Corrective Pass Outcomes
The corrective pass resolved:
- silent duplicate/collision suppression in the registry
- operation-shaped defaults in generic module contribution paths
- soft descriptor identity validation
- unvalidated role-permission assignment
- shallow governance API/service test coverage
- docs/ADR drift from implementation truth

## Testing
Added/expanded targeted tests for:
- registry duplicate/collision behavior
- module contribution validation
- governance controller behavior
- roles controller behavior
- schedules controller behavior
- schedule governance service behavior
- effective-permissions correctness
- role-permission assignment validation

## Documentation / ADRs
Updated:
- architecture docs for governance
- API contracts docs
- module authoring guide

Added:
- `ADR-0014-governance-evaluation-seams-and-assignment-surface.md`

## Completion Notes
Epic 14 is complete after the corrective pass. It established a real governance spine that is platform-first enough to be reused outside operations.

## Follow-on Impact
This epic enabled environment/runtime governance work by providing:
- shared evaluation seams
- governed resource/action registration patterns
- permission assignment validation
- governance inspection surfaces