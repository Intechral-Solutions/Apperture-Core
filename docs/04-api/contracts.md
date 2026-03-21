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
| GET | `/runs/{id}/actions` | Get server-projected run action availability |
| GET | `/runs/{id}/timeline` | Get canonical persisted-evidence run timeline |
| POST | `/runs/{id}/park` | Request that a run park at the next safe boundary |
| POST | `/runs/{id}/resume` | Resume a parked run from the saved next step |
| GET | `/api/governance/descriptors` | List registered governed resource/action descriptors |
| GET | `/api/governance/effective-permissions` | Inspect caller effective permissions |
| GET | `/api/roles/{id}/permissions` | Read explicit role permission assignments |
| PUT | `/api/roles/{id}/permissions` | Replace explicit role permission assignments |
| GET | `/api/schedules/capabilities` | Schedule governance capability projection |
| GET | `/api/environments` | Inspect the current environment (authorization gated) |
| GET | `/api/runtime-instances` | Inspect governed runtime capabilities (validated capability ids + kind) list (authorization gated) |
| GET | `/api/runtime-instances/{runtimeId}/capabilities` | Inspect governed runtime capabilities for one runtime (capabilityId + kind) (authorization gated) |
| POST | `/api/runtime-instances/{runtimeId}/controls/shutdown/request` | Request runtime shutdown (governance gated + auditable) |
| GET | `/api/runtime-instances/{runtimeId}/controls/shutdown/requests` | List persisted runtime shutdown requests (permission-gated by `runtime.read`) |
| POST | `/api/enroll/invitation` | Operator-issued runtime enrollment invitation (governance gated + auditable) |
| POST | `/api/enroll` | Runtime enrollment using enrollment token (anonymous; token-scoped) |
| GET | `/api/control/center` | Unified operator control facade (targets + recent activity + attention) |
| GET | `/api/control/activity` | Unified operator activity feed (executions + durable runtime control requests) |
| GET | `/api/control/attention` | Cross-domain attention items for triage (permission-safe) |

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

## Run result and output (Epic 20 + Epic 21)

- `GET /runs/{id}` returns `RunDetailDto` with `Result` (`RunResultDto`).
- `RunResultDto` includes:
  - `Outcome`: terminal outcome derived from lifecycle status (`succeeded`, `failed`, `cancelled`, `in_progress`).
  - `Summary`: human-readable one-liner. For **succeeded** runs, if persisted captured-output summary is non-empty (`CapturedOutputSummary` from ADR-0019), the API uses that string; for **withheld** capture (ADR-0020), the API uses a fixed platform phrase instead of a fake payload; otherwise a fixed phrase for the outcome. This field blends “outcome headline” with “output headline” when capture provided a summary; `PayloadJson` remains the full captured JSON when inline policy allows it.
  - `PayloadJson`: operator-visible captured output JSON only when inline display is allowed: `OutputAvailability` is `captured` or `truncated`, and `OutputDisplayPolicy` is null (legacy Epic-20 plain return) or `inline`. For `summary_only` or `withheld`, full JSON is not exposed here even if it existed transiently during capture.
  - `OutputAvailability`: technical capture state: `none`, `empty`, `captured`, `truncated`, `unsupported` (ADR-0019), plus `summary_only` and `withheld` (ADR-0020 additive statuses).
  - `OutputDisplayPolicy`: null for legacy/plain returns; otherwise `inline`, `summary_only`, or `withheld` when the handler returned `OperatorRunOutput` (ADR-0020).
  - `DiagnosticsJson`: operator-visible failure diagnostics payload only when diagnostics status/policy allow inline display.
  - `DiagnosticsAvailability`: failure diagnostics state (`none`, `summary_only`, `captured`, `withheld`, `unsupported`) for failed runs.
  - `DiagnosticsDisplayPolicy`: diagnostics display policy when set (`inline`, `summary_only`, `withheld`).
- Captured output is computed on successful terminal completion from the last step’s return value (see ADR-0019, ADR-0020).
- Failure diagnostics are captured on terminal failure through run-grain fields (see ADR-0021).
- Module authors can now declare preferred success/failure operator intent via `OperatorExecutionResult` (ADR-0022); legacy plain success returns remain compatible.
- Legacy runs (pre-output-migration) show `OutputAvailability: none`; no payload.

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
    - `runtime.read` gates runtime identity discovery (`GET /api/runtime-instances`).
    - `runtime.read` gates governed runtime capability inspection (`GET /api/runtime-instances/{runtimeId}/capabilities`).
    - `runtime.enroll.invitation.create` gates operator creation of enrollment invitations (`POST /api/enroll/invitation`).
    - `runtime.control.shutdown.request` gates request of runtime shutdown (`POST /api/runtime-instances/{runtimeId}/controls/shutdown/request`).
  - Privileged operator control (`POST /api/enroll/invitation`) writes durable audit records on successful authorization and invitation creation.
  - Runtime shutdown request creation (`runtime.shutdown.request`) writes durable audit records on successful authorization and capability checks.
  - Runtime enrollment (`POST /api/enroll`) remains token-authenticated with `AllowAnonymous`, and validates token-scoped environment mapping before registering runtime identity.
