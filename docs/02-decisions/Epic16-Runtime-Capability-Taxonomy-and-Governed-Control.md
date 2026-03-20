# Epic 16 - Runtime Capability Taxonomy and Governed Control

## Status
Completed

## Summary
This epic converted the runtime layer from a truthful but limited metadata/enrollment inspection slice into the first real governed runtime capability and control model.

It introduced a canonical governed runtime capability catalog, validated runtime capability claims at invitation/enrollment time, surfaced governed capabilities through operator-facing inspection endpoints, and added one real governed runtime control action: shutdown request.

A shutdown control request is now represented durably, authorization-gated, auditable on request creation, and applied by the worker host through a queued-to-applied lifecycle.

## Goals
- Replace free-form runtime capability metadata with a canonical governed capability model
- Make runtime capability inspection truthful and authorization-aware
- Introduce one real governed runtime control action
- Persist runtime control requests and expose a durable control intent path
- Keep the cycle small and real without inventing a giant runtime control bus

## Scope
### Included
- Platform-seeded runtime capability catalog
- Validation of invitation scopes against canonical capability ids
- Runtime capability inspection DTOs and filtering
- One governed runtime control action:
  - `runtime.control.shutdown.request`
- Durable runtime control request persistence
- Runtime control request service and audit on request creation
- Worker-host application of queued shutdown requests
- Docs and ADR alignment for runtime capabilities and control

### Excluded
- Broad runtime RPC framework
- Full runtime orchestration platform
- Rich runtime health/heartbeat subsystem redesign
- Large capability DSL or capability negotiation framework
- Multiple runtime control actions beyond the initial proving-ground action
- General runtime control completion/rollback/retry framework

## Major Architectural Outcomes

### 1. Canonical governed runtime capabilities
Runtime capabilities are no longer treated as arbitrary declared strings. A canonical platform capability catalog now exists and invitation scope values are validated against it.

### 2. Governed runtime capability inspection
Operator-facing runtime inspection now returns governed capabilities using stable identifiers and kinds, rather than unvalidated raw metadata.

### 3. First real governed runtime control action
The platform now supports a real privileged runtime control path:
- shutdown request creation
- authorization check
- audit on creation
- durable persistence
- worker-host application

### 4. Durable control intent representation
Runtime control is no longer only a conceptual future concern. A persisted control request model now exists and can serve as the seed of future runtime control expansion.

## Implemented Changes

### Runtime capability catalog
Added a platform-seeded runtime capability catalog including:
- `Inspection`
- `ControlShutdown`

This catalog became the canonical source for currently supported runtime capability ids.

### Invitation and enrollment validation
Updated invitation creation and related services to:
- validate requested capability/scope ids against the runtime capability catalog
- reject unknown capability ids
- persist the capability ids deterministically

### Runtime inspection surface
Updated runtime inspection to return governed capability DTOs such as:
- `capabilityId`
- `kind`

Legacy or unknown persisted capability ids are filtered from the inspection surface.

### Governed shutdown control path
Added a new runtime control action:
- `runtime.control.shutdown.request`

Implemented:
- durable `RuntimeControlRequestEntity`
- EF mapping/persistence
- `IRuntimeControlService`
- `RuntimeControlService`
- request creation audit
- operator endpoint:
  - `POST /api/runtime-instances/{runtimeId}/controls/shutdown/request`

This endpoint is:
- RBAC-gated by `runtime.control.shutdown.request`
- checked against runtime capability prerequisites using persisted runtime identity

### Worker host request application
Updated the worker host to:
- consume queued shutdown requests
- mark them as applied
- stop the host

Extracted shutdown request application logic for testability.

### Permissions and seeding
Updated platform permission seeding to include:
- `runtime.control.shutdown.request`

## Testing
Added or expanded targeted tests for:
- governed runtime inspection filtering
- capability validation behavior
- shutdown request endpoint/service behavior
- queued → applied shutdown lifecycle behavior
- runtime control request handling at the worker host level

## Documentation / ADRs
Updated:
- `docs/01-architecture/environment-runtime-governance.md`
- `docs/04-api/contracts.md`
- `ADR-0015-environment-runtime-identity-and-control-taxonomy.md`

These updates removed old “metadata-only” phrasing and documented:
- canonical governed runtime capabilities
- the shutdown control request endpoint
- the runtime control request path

## Completion Notes
Epic 16 is complete.

It established:
- a canonical governed runtime capability model
- one real governed runtime control action
- durable runtime control request persistence
- worker-host application of runtime shutdown requests

It did **not** attempt to become a full runtime orchestration framework, which is the correct boundary for this epic.

## Follow-on Impact
Epic 16 enables the next major step:
- building a unified operator control facade
- unifying execution, control, schedules, activity, and attention into a simpler top-level platform experience