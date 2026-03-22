# Epic 35 — Run inspection and execution forensics

## Status

Implemented.

## Context

Run detail already exposed technical execution data, retry-of links, and some cross-links, but operators lacked a compact, truthful **investigation read model**: module framing, inferred start origin (manual vs scheduled vs retry-as-new-run), schedule correlation from durable conventions, reverse retry lineage, and consistent navigation hints—without inventing a parallel forensic subsystem or new speculative persistence.

## WA1 — Baseline inventory (durable evidence)

| Area | Location / notes |
|------|------------------|
| **Run grain** | `OperationRunEntity`: `Id`, `WorkflowInstanceId`, `DefinitionId`, `Status`, timestamps, `CorrelationKey`, `RetryOfRunId`, failure/output fields. No dedicated `Origin` column. |
| **Workflow handoff** | `WorkflowStartRequestEntity`: `IdempotencyKey`, `CorrelationId`, claim fields, timestamps. |
| **Operation handoff** | `OperationStartRequestEntity`: same shape. |
| **Retry chain (forward)** | New run’s `RetryOfRunId` → original run (workflow retry-as-new and recovery retry). |
| **Schedule ↔ run (today)** | `ScheduleDispatchRunner` sets `IdempotencyKey = schedule:{scheduleId}:{fireTimeUtc:O}` and `CorrelationId = schedule:{scheduleId}`. No `ScheduleRunId` FK on runs. |

See also: [contracts.md](../04-api/contracts.md) (run list filters and `RunDetailDto` forensics).

## Decision

### Read model

- Extend `GET /runs/{id}` with optional nested **`RunForensicContextDto`** on `RunDetailDto` (`ForensicContext`), mirrored on the management client for Blazor deserialization.
- Populate in `RunQueryService.GetRunAsync` in the same round trip: module id resolution (workflow vs operation), origin classification, optional schedule id/fire time from idempotency parse, `RetriedByRunIds` (indexed query on `RetryOfRunId`), optional **current** composition snapshot hints, and structured **`InvestigationHints`** (kinds + ids only; UI builds routes).

### Origin classification (`RunOriginClassifier`)

First match:

1. **`retry`** — `RetryOfRunId` is set (retry-as-new-run). API retry vs recovery retry is **not** distinguished; same field.
2. **`scheduled`** — start `IdempotencyKey` matches platform pattern `schedule:{id}:{ISO-8601}` (aligned with `ScheduleDispatchRunner`). If `CorrelationId` is present and does not match `schedule:{sameId}`, origin remains scheduled but the detail string notes the mismatch.
3. **`manual`** — persisted start request exists, not retry, not schedule pattern.
4. **`unknown`** — no start request or insufficient metadata (`hasStartRequest == false` in classifier; normal API-projectable runs typically still have a start row once kind is resolved).

### Truth boundaries (do not overclaim)

- **Scheduled schedule link** is **inferred** from the idempotency convention, not a stored association; custom keys could theoretically collide—document in UI disclaimer.
- **Module / composition hints** reflect **current** runtime composition, not necessarily state at run start.
- **No durable “replay”** flag unless evidenced elsewhere (e.g. lifecycle events)—not invented here.
- **No distributed trace** or speculative causality engine.

### UI

- **Run detail**: “Investigation” section: origin, module/schedule links when known, retried-by, investigation hints, composition disclaimer when hints present.
- **Runs list**: query param `?correlationKey=` maps to `RunQueryFilter.CorrelationKey` (e.g. `schedule:{scheduleId}` for schedule deep links).
- **Schedule detail**: link “Runs for this schedule” → runs list with that correlation filter.
- **Audit timeline**: when filtered by `runId`, banner link to `/runs/{runId}`.

## Implementation summary

- **Classifier**: [RunOriginClassifier.cs](../../src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunOriginClassifier.cs)
- **Query + DTOs**: [RunQueryService.cs](../../src/Apperture.ControlPlane/Apperture.ControlPlane.Security/RunQueryService.cs), [IRunQueryService.cs](../../src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IRunQueryService.cs), [IManagementClient.cs](../../src/Apperture.ControlPlane/Apperture.Management.Client/IManagementClient.cs)
- **UI helpers**: [Epic33UiDisplayHelpers.cs](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Epic33UiDisplayHelpers.cs)
- **Pages**: [RunDetail.razor](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunDetail.razor), [RunsList.razor](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Runs/RunsList.razor), [ScheduleDetail.razor](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor), [AuditTimeline.razor](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Control/AuditTimeline.razor)

## Future (out of scope for Epic 35)

- Explicit persisted run origin at dispatch if inference is insufficient.
- Durable schedule↔run association table.
- Symmetric operation retry lineage if product requires it.
