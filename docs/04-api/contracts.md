# API Contracts

## Overview

The Management API (control plane) exposes REST endpoints for module registry, workflow discovery, execution, runs, schedules, and audit. OpenAPI specs will be added when the API is implemented.

## Minimal v1 endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/modules` | List installed modules |
| GET | `/modules/{id}/workflows` | List workflows for a module |
| GET | `/operations` | List operation definitions visible to caller |
| GET | `/operations/{operationDefinitionId}` | Get one operation definition by stable id |
| POST | `/operations/{operationDefinitionId}/start` | Start an operation |
| GET | `/operations/runs` | List operation runs (operation-centric contract) |
| GET | `/operations/runs/{runId}` | Get operation run detail (operation-centric contract) |
| POST | `/workflows/{workflowId}/start` | Start a workflow (parameters, idempotency key) |
| GET | `/runs` | List operation runs |
| GET | `/runs/{id}` | Get run details |
| POST | `/runs/{id}/park` | Request that a run park at the next safe boundary |
| POST | `/runs/{id}/resume` | Resume a parked run from the saved next step |
| GET | `/api/governance/descriptors` | List registered governed resource/action descriptors |
| GET | `/api/governance/effective-permissions` | Inspect caller effective permissions |
| GET | `/api/roles/{id}/permissions` | Read explicit role permission assignments |
| PUT | `/api/roles/{id}/permissions` | Replace explicit role permission assignments |
| GET | `/api/schedules/capabilities` | Schedule governance capability projection |
| GET | `/api/environments` | Inspect the current environment (authorization gated) |
| GET | `/api/runtime-instances/{runtimeId}/capabilities` | Inspect runtime declared capability/scopes (authorization gated) |
| POST | `/api/enroll/invitation` | Operator-issued runtime enrollment invitation (governance gated + auditable) |
| POST | `/api/enroll` | Runtime enrollment using enrollment token (anonymous; token-scoped) |

## Operation definition and run contract notes

- Operation API identity is `operationDefinitionId` (stable, persisted).
- `OperationDefinition.Name` is execution-mapping metadata (single step-handler name), not API identity.
- Definition visibility is governed by operation read permission; a visible definition may still have `CanStart=false` when execute permission is missing.
- Run list supports operation-centric query filters via `/runs` query parameters:
  - `executionKind`
  - `operationDefinitionId`
  - `status`
  - `createdFrom`
  - `createdTo`
  - `moduleId`
  - `correlationKey`
- Run kind resolution is deterministic from durable start-request ownership:
  - workflow run: present in `WorkflowStartRequests` only
  - operation run: present in `OperationStartRequests` only
  - ambiguous/unknown ownership is treated as non-projectable.

## Governance API notes

- Permission assignment endpoints are intentionally API-first; UI can remain thin while governance semantics stabilize.
- Descriptor listing is sourced from `IResourceDescriptorRegistry` after platform seeding and module contribution registration.
- Module governance contributions require explicit `ResourceType` in manifest declarations (`GovernedResources`, `GovernedActions`); no operation-shaped defaults. Contributions use `ResourceFamily.Core` for generic module resources.
- Registry collision semantics: exact duplicate (same descriptor + family) is idempotent; conflicting duplicate (same key, different descriptor) fails with clear diagnostics.
- Role-permission assignment (`PUT /api/roles/{id}/permissions`) validates all requested permissions against the canonical descriptor catalog before persistence; unknown or malformed permissions return 400 with actionable error details.
- Environment/runtime governed surfaces:
  - Canonical environment descriptor uses `ResourceTypeConstants.Environment` with scope key `environment:{environmentId}`.
  - Canonical runtime (governed) descriptor uses `ResourceTypeConstants.RuntimeInstance` with scope key `environment:{environmentId}:runtime:{runtimeId}`.
  - Authorization actions:
    - `environment.read` gates environment inspection (`GET /api/environments`).
    - `runtime.read` gates runtime capability inspection (`GET /api/runtime-instances/{runtimeId}/capabilities`).
    - `runtime.enroll.invitation.create` gates operator creation of enrollment invitations (`POST /api/enroll/invitation`).
  - Privileged operator control (`POST /api/enroll/invitation`) writes durable audit records on successful authorization and invitation creation.
  - Runtime enrollment (`POST /api/enroll`) remains token-authenticated with `AllowAnonymous`, and validates token-scoped environment mapping before registering runtime identity.
