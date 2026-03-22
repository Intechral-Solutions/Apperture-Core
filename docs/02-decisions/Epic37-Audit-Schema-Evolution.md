# Epic 37 — Audit Schema Evolution and Filter Performance Hardening

## Status

Implemented (corrective pass complete)

## Corrective Pass (2026-03-22) — SQL-Backed Verification

Epic 37’s core claims (migration, backfill, DB-backed filters) were not previously verified against a real SQL Server. The corrective pass addressed this.

### Corrective changes made

1. **Test database creation** — `SqlServerTestConfiguration.EnsureTestDatabaseExists` creates `Apperture_IntegrationTests` via master when missing (integration tests previously failed with “database does not exist”).
2. **Migration discovery** — Added `[DbContext]` and `[Migration]` attributes to `AddAuditFirstClassFields` and `AddScheduleRunClaimedByHostId` migrations; EF Core was not applying them without these.
3. **Test isolation** — Tests use unique `ActionPrefix` values (`ep37.ordered.`, `ep37.actionprefix.`, etc.) to avoid shared-database interference; `[Collection("SqlServerAudit")]` enforces sequential execution.
4. **Backfill verification test** — `Migration_Backfill_PopulatesFirstClassFieldsFromDetails` inserts pre-Epic-37 shaped rows (null first-class columns, extractable data in Details), runs backfill SQL, and asserts Outcome/ModuleId/ScheduleId/RunId are populated correctly.

### SQL-backed verification completed

All 9 Epic 37 integration tests run and pass against SQL Server (Docker on localhost:1433):

- Migration applies successfully
- `Query_ReturnsOrderedEntries_NewestFirst` — ordering and time-range filter
- `Query_WithActionPrefix_ReturnsFiltered` — action prefix filter
- `Query_WithRunIdFilter_ReturnsMatching` — runId filter (DB-backed)
- `GetById_ReturnsDetailWithRefs` — get-by-id with first-class refs
- `Query_WithOutcomeFilter_ReturnsMatching_DBBacked` — outcome filter
- `Query_WithOutcomeFilter_ExcludesNullOutcomeRows` — null outcome excluded
- `Query_WithModuleIdFilter_ReturnsMatching_DBBacked` — moduleId filter
- `Query_WithScheduleIdFilter_ReturnsMatching_DBBacked` — scheduleId filter
- `Migration_Backfill_PopulatesFirstClassFieldsFromDetails` — historical backfill

### Backfill behavior verified

`Migration_Backfill_PopulatesFirstClassFieldsFromDetails` proves:

- Rows with null Outcome/ModuleId/ScheduleId/RunId and extractable data in Details
- Backfill SQL populates Outcome (first segment), ModuleId (`module=`), ScheduleId (`schedule:`), RunId (`runId=` / `RunId=` / `originalRunId=`)
- Rows lacking extractable values keep nulls
- Read path returns correct RelatedReferences from backfilled columns

### DB-backed filter behavior verified

Filters for Outcome, ModuleId, ScheduleId, RunId are applied in SQL via `SqlAuditService.QueryAsync`; `AuditTimelineQueryService` passes them to `AuditQuery`. No bounded in-memory fetch; all promoted fields are DB-backed.

### Tests added/updated and actually run

- **Added**: `Migration_Backfill_PopulatesFirstClassFieldsFromDetails`
- **Updated**: All 8 existing tests with `EnsureTestDatabaseExists`, unique ActionPrefix, sequential collection
- **Run**: `dotnet test --filter "FullyQualifiedName~AuditTimelineSqlServerTests"` with `MSSQL_SA_PASSWORD` and SQL Server on localhost:1433

### Remaining limitations

1. SQL Server must be running (e.g. `docker compose up -d sqlserver`) and `MSSQL_SA_PASSWORD` set; tests throw if unreachable.
2. Tests share `Apperture_IntegrationTests`; isolation is via ActionPrefix and sequential execution.
3. Other integration test classes (e.g. `RuntimeHealthBlockedDeferredSqlServerTests`) do not yet call `EnsureTestDatabaseExists`; they may need similar treatment.

### Closure verdict for Epic 37

**Epic 37 is closed.** Migration, backfill, and DB-backed filters are verified against a real SQL Server. The corrective pass provides provider-backed evidence rather than compile-only confidence.

## Context

Epic 32 made audit discoverable and operator-usable, but with bounded in-memory filtering and best-effort parsing from `Details` for outcome, moduleId, scheduleId, and runId. As operational activity grew, audit performance and first-class filterability became limiting. This epic evolves the audit schema and query path so the most important dimensions become DB-backed and performant, without redesigning the audit architecture or creating a second audit store.

## Summary of Schema Evolution

- **New columns** (nullable): `Outcome`, `ModuleId`, `ScheduleId`, `RunId` on `AuditLogs`
- **Indexes**: `IX_AuditLogs_At_Id`, `IX_AuditLogs_Outcome_At`, `IX_AuditLogs_ModuleId_At`, `IX_AuditLogs_ScheduleId_At`, `IX_AuditLogs_RunId_At` (filtered where column IS NOT NULL)
- **Write path**: Outcome and reference fields persisted to columns; Details no longer merges Outcome
- **Read path**: All filters applied in DB; no bounded in-memory fetch

## Chosen First-Class Fields and Rationale

| Field       | Rationale                                                                 |
|-------------|----------------------------------------------------------------------------|
| **Outcome** | Already in domain model; all writers provide it; highest value for operator investigations (failed/blocked) |
| **ModuleId**| Module lifecycle events emit `module=X`; operators need "audit for this module" |
| **ScheduleId** | Scheduler events emit `schedule:{id}`; operators need "audit for this schedule" |
| **RunId**   | Scheduler, recovery, lifecycle events emit `runId=X` or `RunId=X`; operators need "audit for this run" |

## Schema/Migration and Write-Path Changes

**Migration**: `20260322240000_AddAuditFirstClassFields`

- Adds columns `Outcome` (nvarchar 64), `ModuleId` (nvarchar 128), `ScheduleId` (nvarchar 128), `RunId` (nvarchar 64)
- Creates indexes for timeline and filter queries
- One-time backfill from `Details` for historical rows (Outcome, ModuleId, ScheduleId, RunId extraction via SQL)

**AuditRecord** (domain): Added optional `ModuleId`, `ScheduleId`, `RunId` parameters.

**SqlAuditService.AppendAsync**: Persists Outcome and reference columns; Details stores only `record.Details` + Resource suffix.

**Call-site updates**: ScheduleDispatchRunner, RecoveryController, SqlModuleRegistryService, WorkflowStartRequestProcessor, OperationStartRequestProcessor, WorkflowRetryService now pass first-class refs where available.

## Query/Read-Path Improvements

- **AuditQuery**: New `FilterByOutcome`, `FilterByModuleId`, `FilterByScheduleId`, `FilterByRunId`
- **SqlAuditService.QueryAsync**: DB predicates for all new filters; returns Outcome and refs from entity
- **AuditTimelineQueryService**: Passes all filters to `AuditQuery`; no in-memory filtering; removed bounded fetch limits
- **MapToEntry**: Uses entity Outcome when present, else parses from Details (backward compat); merges first-class refs with parsed refs

## Backward Compatibility / Historical Data

1. **New rows**: First-class columns populated by write path.
2. **Historical rows** (pre-migration): All new columns null until backfill.
3. **Backfill**: Migration includes SQL to extract Outcome, ModuleId, ScheduleId, RunId from Details into new columns where possible.
4. **Filter behavior**: Rows with null first-class columns are excluded from DB-backed filter results. Display still uses `AuditDetailParser` for Outcome/refs when columns are null.

## API/UI Truthfulness

- **API docs**: "All filters are DB-backed (fromUtc, toUtc, actor, actionPrefix, outcome, moduleId, scheduleId, runId)"
- **AuditFilterCriteria**: Updated to describe DB-backed filters and historical exclusion
- **UI**: Added Outcome filter dropdown; subtitle updated to "Filters are DB-backed"

## Remaining Limitations

1. **Historical data without backfill**: Rows with null first-class columns excluded from DB-backed filter results (backfill mitigates).
2. **Multiple RunIds per event**: Recovery "retry dispatched" has originalRunId and newRunId; only one stored (original). Filter matches stored value.
3. **Details still best-effort for display**: `ParseOutcome` / `ParseRelatedReferences` remain for display and null-column rows.
4. **No full-text search**: Details is not indexed for arbitrary text search.

## Files Modified

- Schema: `AuditLogEntity`, `AppertureDbContext`, migration
- Domain: `AuditRecord`
- Write: `SqlAuditService`, ScheduleDispatchRunner, RecoveryController, SqlModuleRegistryService, WorkflowStartRequestProcessor, OperationStartRequestProcessor, WorkflowRetryService
- Query: `AuditQuery`, `SqlAuditService`, `AuditTimelineQueryService`, `AuditTimelineModels`
- API/UI: `AuditController`, `AuditTimeline.razor`
- Tests: `AuditTimelineSqlServerTests`, `SqlServerTestConfiguration`
- Corrective pass: `SqlServerTestConfiguration` (EnsureTestDatabaseExists), migration attributes, `AuditTimelineSqlServerTests` (isolation, backfill test)

## Supersedes

Epic 32 filter semantics for outcome, moduleId, scheduleId, runId. See [Epic32-Audit-Timeline-Discoverability.md](Epic32-Audit-Timeline-Discoverability.md) for original design; filter behavior updated by Epic 37.
