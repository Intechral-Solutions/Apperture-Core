# SDK-3B Operation Itemization Read APIs Implementation Plan

> **For agentic workers:** Implement task-by-task. Spec gate = this plan + `docs/audits/sdk3-itemized-operation-model-design-audit.md` + SDK-3A store.

**Goal:** Management read/query APIs + DTOs/client for durable operation groups/items; lightweight run-detail summary. No UI, FileImport adapter, or retry execution.

**Architecture:** Mirror `RunTimelineQueryService` — authorize like run detail, query via EF/`IOperationItemStore` extensions. Routes nest under `api/runs/{runId}/…`. Client DTOs mirror Security/API JSON.

**Tech Stack:** ASP.NET Minimal patterns in `RunsController`, ControlPlane.Security query service, Persistence SQL + in-memory store, Management.Client.

## Global Constraints

- Same permission model as viewing the run (workflow/operation run-read authz).
- No new permission.
- No unbounded payloads; expose already-bounded JSON strings only.
- Expose retry fields; do not execute retry.
- No UI panel (SDK-3D), no FileImport adapter (SDK-3C).

## Design answers

| # | Decision |
|---|----------|
| 1 | Routes: `GET /api/runs/{runId}/operation-groups` and `…/operation-groups/{groupId}/items` (avoid clash with `OperationsController`). |
| 2 | Separate endpoints for groups vs items. |
| 3 | Run detail gets **summary only** (`groupCount`, `failedItemCount`, `skippedItemCount`, `hasOperationItems`) — optional trailing field. |
| 4 | Gate with existing workflow/operation **run-read** authz (same as timeline). |
| 5 | Pagination: `skip` + `take` (default 50, clamp 1–200), return `{ items, totalCount, skip, take }`. |
| 6 | v1 filters: `status`, `retryableOnly`, `q` (subjectKey/displayLabel contains). |
| 7 | Missing run → 404; unauthorized → 403; no groups/items → 200 empty. Wrong group/run → 404. |
| 8 | Expose `summaryJson` / `changeSummaryJson` / `metadataJson` as stored bounded strings (no silent truncation). |
| 9 | Expose `isRetryable`, `retryCount`, `lastAttemptedAtUtc` now. |
| 10 | Use 3A indexes; page take-cap; group list unbounded but expected small per run; summary from group aggregates (no item scan). |

## Files

| File | Responsibility |
|------|----------------|
| `IOperationItemStore` + InMemory/Sql | Paginated item query + run summary aggregates |
| `IOperationItemizationQueryService` + impl | Authz + map to read DTOs |
| `RunsController` | HTTP endpoints |
| `IRunQueryService` / `RunQueryService` + Client `RunDetailDto` | Optional summary on get-run |
| `IManagementClient` / `ManagementClient` | Client methods + DTOs |
| Host `Program.cs` | `AddOperationItemizationPersistence` |
| Tests + authoring docs | Coverage + docs |

## Tasks

- [x] Extend store with summary + paged item query
- [x] Query service + DTOs + DI
- [x] Controller endpoints + client
- [x] Run detail summary wiring
- [x] Tests + docs + validate

## Follow-ups

- **SDK-3C** — FileImport adapter to platform writer
- **SDK-3D** — Run Detail itemization panel
- **SDK-4** — per-item retry execution and UI
