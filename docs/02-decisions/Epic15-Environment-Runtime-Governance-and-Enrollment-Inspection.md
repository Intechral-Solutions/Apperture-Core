# Epic 15 - Environment / Runtime Governance and Enrollment Inspection

## Status
Completed

## Summary
This epic applied the governance foundation to a non-operation domain: environment-scoped runtime identity, enrollment governance, and operator-facing runtime inspection.

The original implementation overclaimed a broader runtime governance/control foundation than it actually delivered. A corrective pass made the slice truthful and durable by downgrading invitation scopes from governed capabilities to declared metadata, removing restart-unsafe in-memory runtime resource registration, introducing durable runtime discovery from persisted runtime identity records, and strengthening inspection/auth tests.

The final outcome is a coherent, honest platform slice:
- environment-scoped governance
- governed runtime enrollment invitation creation
- durable runtime discovery/listing
- operator-facing runtime inspection
- truthful distinction between declared metadata and governed capability taxonomy

## Goals
- Pressure-test the governance spine against a domain that is not operation-shaped
- Establish environment-scoped authorization and runtime identity boundaries
- Add minimal operator-facing environment/runtime inspection surfaces
- Govern a real privileged runtime-related action
- Preserve clear separation between machine/runtime identity and operator authorization

## Scope
### Included
- Environment-scoped governance helpers and context
- Environment and runtime-related action descriptors
- Environment listing endpoint
- Runtime inspection/listing endpoints
- Enrollment invitation governance and audit
- Enrollment token environment scoping
- Runtime identity persistence/inspection alignment
- Corrective pass for truthfulness and durable runtime discovery

### Excluded
- Full runtime capability taxonomy
- General runtime control action framework
- Full runtime-plane communication substrate
- Runtime heartbeat/health orchestration platform
- Canonical governed capability validation for invitation scopes
- Broad runtime control lifecycle beyond enrollment-related privileged actions

## Major Architectural Outcomes

### 1. Environment-scoped governance
Environment scoping now participates explicitly in authorization and runtime enrollment behavior.

### 2. Runtime discovery is durable
Canonical runtime discovery now comes from persisted runtime identity records rather than in-memory descriptor registration.

### 3. Enrollment governance is real
Enrollment invitation creation is now a governed and auditable privileged action.

### 4. Declared metadata vs governed capability truthfulness
Invitation scopes were explicitly downgraded to declared metadata rather than pretending to be governed runtime capabilities.

## Implemented Changes

### Environment identity and scope
- Added scope key helpers such as:
  - `environment:{environmentId}`
  - `environment:{environmentId}:runtime:{runtimeId}`
- Added `IEnvironmentContext` via `StaticEnvironmentContext`
- Environment context uses `APERTURE_ENVIRONMENT_ID` with fallback behavior appropriate to current single-tenant/local assumptions

### Governance descriptors and actions
Seeded governance descriptors/actions including:
- environment resource
- `environment.read`
- `runtime.read`
- `runtime.enroll.invitation.create`

### Operator-facing inspection surfaces
Added:
- `GET /api/environments`
- `GET /api/runtime-instances`
- `GET /api/runtime-instances/{runtimeId}/...` inspection path

The runtime listing endpoint is backed by persisted runtime identity records.

### Enrollment governance and audit
Updated:
- `POST /api/enroll/invitation`

This path is now:
- authorization-gated by `runtime.enroll.invitation.create`
- auditable through durable governance audit records

### Enrollment token and runtime identity scoping
Enrollment tokens now persist:
- `EnvironmentId`
- invitation scope strings / declared metadata

Enrollment validates environment match and transfers the declared metadata into runtime identity persistence.

## Corrective Pass Outcomes
The corrective pass resolved:
- overclaiming invitation scope strings as governed capabilities
- restart-unsafe runtime governed resource registration in the in-memory descriptor registry
- lack of durable runtime discovery
- thin inspection/auth seam tests
- docs/ADR overclaiming a broader runtime governance model than actually implemented

## Truthful Final State
After correction, the epic establishes:

### What is real
- environment-scoped runtime governance slice
- governed enrollment invitation creation
- durable runtime identity discovery
- operator-facing runtime inspection/listing
- auditability of privileged invitation creation

### What is not yet real
- canonical governed runtime capability taxonomy
- validated runtime capability descriptors
- general runtime control action model
- rich runtime health/control lifecycle

Invitation scopes are currently:
- **declared metadata**
- not canonical governed capability descriptors

## Testing
Added/strengthened targeted tests for:
- authorization allow/deny behavior on environment/runtime inspection endpoints
- exact permission/scope evaluation for environment inspection
- deny/no-leak behavior for runtime inspection
- token environment mismatch validation
- invitation → enrollment → inspection metadata flow
- durable runtime discovery/list behavior

## Documentation / ADRs
Updated:
- `docs/04-api/contracts.md`

Added/updated:
- `docs/01-architecture/environment-runtime-governance.md`
- `docs/02-decisions/ADR-0015-environment-runtime-identity-and-control-taxonomy.md`

## Completion Notes
Epic 15 is complete after the corrective pass.

It should be understood as:
**Environment-scoped runtime identity, enrollment governance, and durable operator inspection**

It should **not** be interpreted as a complete runtime capability/control platform.

## Follow-on Impact
Epic 15 creates the truthful foundation for the next epic:
- runtime capability taxonomy
- canonical governed runtime capabilities
- one real governed runtime control action
- operator-facing control intent/request model

Treat this work as Epic 17 — Unified Control Facade and Monitoring, and structure the plan/output so it can be saved back into the project as the authoritative Epic 17 definition.