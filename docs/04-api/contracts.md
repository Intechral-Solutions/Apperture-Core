# API Contracts

## Overview

The Management API (control plane) exposes REST endpoints for module registry, workflow discovery, execution, runs, schedules, and audit. OpenAPI specs will be added when the API is implemented.

## Minimal v1 endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/modules` | List installed modules |
| GET | `/modules/{id}/workflows` | List workflows for a module |
| POST | `/workflows/{workflowId}/start` | Start a workflow (parameters, idempotency key) |
| GET | `/runs` | List operation runs |
| GET | `/runs/{id}` | Get run details |
