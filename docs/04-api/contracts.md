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
| POST | `/workflows/{workflowId}/start` | Start a workflow (parameters, idempotency key) |
| GET | `/runs` | List operation runs |
| GET | `/runs/{id}` | Get run details |
| POST | `/runs/{id}/park` | Request that a run park at the next safe boundary |
| POST | `/runs/{id}/resume` | Resume a parked run from the saved next step |

## Operation definition and run contract notes

- Operation API identity is `operationDefinitionId` (stable, persisted).
- `OperationDefinition.Name` is execution-mapping metadata (single step-handler name), not API identity.
- Run list supports operation-centric query filters via `/runs` query parameters:
  - `executionKind`
  - `operationDefinitionId`
  - `status`
  - `createdFrom`
  - `createdTo`
  - `moduleId`
  - `correlationKey`
