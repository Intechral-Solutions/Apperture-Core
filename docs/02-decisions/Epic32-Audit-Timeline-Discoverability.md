# Epic 32 — Audit Timeline and Discoverability

## Status

Implemented

## Context

The platform emits meaningful audit for lifecycle actions, scheduler outcomes, blocked execution, startup/runtime anomalies, and remediation actions. Operators lacked a coherent way to discover, inspect, and follow that audit history through the management experience. This epic adds an audit timeline and discoverability surface without introducing a parallel audit subsystem.

## Audit Model Inventory

### Persistence Schema

**Table**: `AuditLogs` (via `AuditLogEntity`)

| Column   | Type           | Notes                    |
|----------|----------------|--------------------------|
| Id       | bigint         | PK, identity             |
| At       | datetimeoffset | Event timestamp          |
| Actor    | nvarchar(256)  | Actor identifier         |
| Action   | nvarchar(128)  | Action name              |
| Details  | nvarchar(max)  | Merged Outcome + Details |

**Write behavior**: `Outcome`, `Resource`, `CorrelationId` are merged into `Details` on write. On read-back they are not restored as separate fields.

**Indexes**: Primary key only. No index on At, Actor, or Action (table scans for queries).

### Filter Capabilities

| Dimension    | DB-filterable | Notes                                                |
|-------------|---------------|------------------------------------------------------|
| Time range  | Yes           | `FromUtc`, `ToUtc` on `At` column                   |
| Actor       | Yes           | `FilterByActorId` on `Actor` column                 |
| Action      | Yes           | `Action.StartsWith(prefix)` since Action is a column|
| Module id   | No            | Only in Details; requires parse or in-memory filter |
| Schedule id | No            | Only in Details (`schedule:XXX`)                     |
| Run id      | No            | Only in Details (`runId=X`)                          |
| Outcome     | No            | Merged into Details; parse on read                   |

### Action Names by Category

| Category         | Action Names / Prefixes                                                                 |
|------------------|-----------------------------------------------------------------------------------------|
| Lifecycle        | `execution.workflow_start.blocked`, `execution.operation_start.blocked`, `execution.workflow_run.failed_definition_unavailable`, `execution.operation_run.failed_definition_unavailable`, `runtime.composition.invalid_at_startup`, `execution.workflow_retry.blocked` |
| Scheduler        | `scheduler.schedule.dispatched`, `scheduler.schedule.blocked_by_lifecycle`, `scheduler.schedule.failed` |
| Recovery         | `recovery.blocked_work.retry_requested`, `recovery.blocked_work.retry_dispatched`, `recovery.blocked_work.retry_refused`, `recovery.schedules.run_cycle_requested` |
| Module lifecycle | `module.lifecycle.discover`, `module.lifecycle.install`, `module.lifecycle.enable`, `module.lifecycle.disable`, `module.lifecycle.uninstall` |
| Governance       | `governance.role.assignment.*`, `governance.permission.assignment.updated`, `governance.runtime.*` |
| Auth             | `auth.local.login.*`, `auth.entra.login.*`, `user.created`, `credential.*`, etc.        |

### Details Format (Reference Extraction)

Typical patterns in `Details` for related reference extraction:

- `runId=X`, `definitionId=X`, `workflowId=X`, `operationDefinitionId=X`
- `schedule:{scheduleId}` (colon-separated prefix)
- `RunId=X` (scheduler dispatched)
- `module=X`, `runtimePlaneId=X`
- `originalRunId=X`, `newRunId=X` (recovery retry dispatched)

Extraction is best-effort; formats may vary across writers.

## Decision

### Read Model

- **AuditTimelineEntry**: Id, Timestamp, Actor, Action, Category, Outcome, Summary, Details, RelatedReferences
- **AuditRelatedReference**: Kind (module | schedule | run | workflow_definition | operation_definition | runtime_plane), Id
- **AuditTimelinePage**: Items, NextContinuationToken

### API

- `GET /api/control/audit` — list/timeline with filters: fromUtc, toUtc, actor, actionPrefix, moduleId, scheduleId, runId, outcome, pageSize, continuationToken
- `GET /api/control/audit/{id}` — single entry detail

### Filters

- **DB-filterable (indexed/column)**: time range (FromUtc, ToUtc), actor, action prefix. These filters are applied in the database query.
- **In-memory (post-fetch, bounded)**: moduleId, scheduleId, runId, outcome. These are applied within the fetched page only; they do not perform global indexed search. When reference filters are used:
  - A time window is required (default 24h if omitted).
  - The effective window is capped at 168 hours (7 days) to prevent unbounded scans.
  - Fetch size is capped at 300 records per request.
  - Results are scoped to the bounded window and fetch; matching events outside the window are not returned.

### UI

- Audit Timeline page at `/control/audit` under Control domain (Control Panel menu: "Audit Timeline")
- Minimal viewer: time range (24h / 7d), category, actor. Full filters (module, schedule, run, outcome) available via API only.
- Table with Timestamp, Action, Actor, Outcome, Category; expandable Details and Related references
- Load-more pagination
- Links to related entities: runs (`/runs/{id}`), schedules (`/schedules/{id}`), modules (`/modules`)

### Out of Scope

- SIEM product
- External log shipping / long-term retention
- Full-text search over Details
- Speculative analytics dashboards
- Second audit writer or shadow store

## Consequences

- Operators can query a coherent audit timeline
- Meaningful filters exist for dimensions supported by the data
- Lifecycle, scheduler, remediation, and anomaly events remain distinguishable
- Related references enable drill-down where routes exist

## Implementation Summary

- **Read model**: [AuditTimelineModels.cs](src/Apperture.Core/Apperture.Core.Application/Services/AuditTimelineModels.cs) — `AuditTimelineEntry`, `AuditRelatedReference`, `AuditTimelinePage`, `AuditFilterCriteria`
- **Query service**: [AuditTimelineQueryService](src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Audit/) — maps `IAuditService` results, applies in-memory filters, parses Details
- **Detail parser**: [AuditDetailParser](src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Audit/AuditDetailParser.cs) — Outcome and reference extraction from Details
- **Category resolver**: [AuditCategoryResolver](src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Audit/AuditCategoryResolver.cs) — action prefix to category mapping
- **API**: [AuditController](src/Apperture.ControlPlane/Apperture.Management.Api/Controllers/AuditController.cs) — `GET /api/control/audit`, `GET /api/control/audit/{id}`
- **UI**: [AuditTimeline.razor](src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/AuditTimeline.razor)

## What This Epic Does Not Do

- SIEM product
- External log shipping or long-term retention
- Full-text search over Details
- Speculative analytics dashboards
- Second audit writer or shadow store
- Alerting on audit events

## Filter Truthfulness (Epic 32 Corrective)

Operators should expect:
- **Time, actor, category (action prefix)**: Efficient DB-backed filters. Use these for broad discoverability.
- **Module/schedule/run/outcome**: Best-effort within the requested time window. Results are bounded (max 7-day window, 300 records per fetch). Do not assume these find all matching events globally.
- **API vs UI**: The API exposes the full filter surface. The UI is a minimal timeline viewer with time range, category, and actor; use the API for module, schedule, run, or outcome filtering.

## Remaining Limitations

1. **Outcome/Resource on read**: Not stored as columns; Outcome is best-effort parsed from Details; Resource is not recoverable.
2. **Module/schedule/run filters**: Not indexed; applied in-memory within bounded fetch. Not global search.
3. **Full-text search**: Not implemented; Details returned as opaque string.
4. **Schema**: No migration; existing schema unchanged. Future: add Outcome/Resource columns, indexes on Action/At.
