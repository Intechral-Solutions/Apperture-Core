# SDK-5A — Sample.ApiPolling Demo

**Date:** 2026-07-18  
**Status:** Implemented  
**Prior design:** [sdk5-api-polling-demo-strategy-and-design.md](../../audits/sdk5-api-polling-demo-strategy-and-design.md)

## Goal

Portable `Sample.ApiPolling` demo module that proves scheduled/API-style sync using embedded JSON snapshots, an in-process fake API client, process-local incremental state, durable platform operation items, and exactly one `sync.*` platform signal per completed poll.

Complements `Sample.FileImport` (one-shot import + per-item retry).

## Projects

```
src/Apperture.Modules/Sample.ApiPolling/
  Sample.ApiPolling.Contracts/
  Sample.ApiPolling.Module/
    Snapshots/*.json
```

No Persistence project. No `.Ui` project.

## Snapshots

| Id | Intent |
|----|--------|
| `snapshot-initial` | Baseline accounts (all new on first poll) |
| `snapshot-changes` | Updates + one new id after initial |
| `snapshot-errors` | Valid + invalid records |
| `snapshot-deletions` | Tombstone (`isDeleted`) + missing keys |
| `snapshot-empty` | Empty page |

Domain: generic customer/account (`externalId`, `displayName`, `email`, `status`, `tier`, `updatedAtUtc`, `version`, `isDeleted`).

## Fake API

`ISampleDirectoryApiClient` / `EmbeddedSampleDirectoryApiClient` — loads embedded JSON; supports page size/offset and `includeDeleted`. No network.

## State

`IApiPollingStateStore` / hybrid or in-memory.

**SDK-5B:** Durable last-seen via `Sample.ApiPolling.Persistence` (`IApiPollingDurableLastSeenStore` + SQL). In-flight batches stay process-local. See [SDK-5B](SDK-5B-ApiPolling-Durable-Sync-State.md).

**Watermark commit:** Stage in ProcessChanges; commit on Succeeded / CompletedWithErrors; discard on Failed.

## Workflow / procedure

- Workflow: `Sample.ApiPolling.Poll` — Validate → Poll → Compare → ProcessChanges → Complete
- Procedure: `Sample.ApiPolling.procedures.poll` — `snapshotId` required; optional `resetState`, `strictMode`, `failOnRecordError`

## Operation itemization

- Kind: `api_poll`
- One group per poll; one item per classified record
- `changeKind` in metadata: `new` / `updated` / `unchanged` / `deleted` / `failed` / `skipped`
- Failed validation → `Failed` + `IsRetryable=true`
- No raw snapshot dumps in metadata

## Signals

Exactly one from Complete (fail-loud):

| Signal | When |
|--------|------|
| `sync.completed` | Succeeded, no new/updated/deleted |
| `sync.completed.with.changes` | Succeeded with changes |
| `sync.completed.with.errors` | Partial success |
| `sync.failed` | Strict / all-failed |

## Settings

`pageSize` (50), `maxRecordsPerSync` (1000), `includeDeleted` (true), `failOnRecordError` (false), `strictModeDefault` (false).

## Deferred

- SDK-5B: durable SQL watermark / sync state
- SDK-5C: failed-record retry
- Systems/Services, shared entity store, cross-integration lookup, live HTTP/OAuth, module `.Ui`
