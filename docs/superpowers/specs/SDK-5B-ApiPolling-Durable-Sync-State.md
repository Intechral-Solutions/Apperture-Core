# SDK-5B — Durable Sync State / Watermark Foundation

**Date:** 2026-07-19  
**Status:** Implemented  
**Prior:** [SDK-5A](SDK-5A-Sample-ApiPolling-Demo.md) · [SDK-5 design](../../audits/sdk5-api-polling-demo-strategy-and-design.md)

## Goal

Promote `Sample.ApiPolling` last-seen / watermark state to durable SQL so incremental sync survives WorkerHost restarts — without introducing a business entity store, Systems/Services, or live HTTP.

## Architecture

| Layer | Role |
|-------|------|
| `IApiPollingDurableLastSeenStore` | Durable last-seen + summary API |
| `SqlApiPollingDurableLastSeenStore` | SQL implementation |
| `HybridApiPollingStateStore` | Durable last-seen + process-local in-flight session |
| `InMemoryApiPollingStateStore` | Full process-local (tests) |
| Platform operation items | Durable per-run operator history (unchanged) |

**Not a business entity store.** Tables hold sync fingerprints for compare only. Future entity/projection epics should not treat these rows as canonical customer records.

## Schema (`Sample.ApiPolling.Persistence`)

- `ApiPollingSyncStates` — one row per `ScopeKey` (integration id)
- `ApiPollingKnownRecords` — last-seen fields + `RecordHash` checksum
- Migration history: `__EFMigrationsHistory_SampleApiPolling`
- Shared bootstrap SQL database (Unisa pattern); Management Host migrates on startup

## State keying

`ScopeKey` = integration id when present; fallback `sample` only for tests/missing context. Hosts must not collapse integrations.

## Commit rule

1. ProcessChanges **stages** upserts/removals  
2. Complete **commits** staged mutations only for `Succeeded` / `CompletedWithErrors`  
3. Complete **discards** on `Failed` (strict / all-failed)

`resetState=true` clears durable scope before Poll.

## Host DI

```csharp
AddSampleApiPollingPersistence(csFactory, applyMigrationsOnStartup: true); // Management
AddSampleApiPollingPersistence(csFactory, applyMigrationsOnStartup: false); // Worker
AddSampleApiPolling(); // Hybrid when durable registered
```

## Deferred

SDK-5C failed-record retry; shared business entity store; cross-integration lookup; live HTTP/OAuth; module `.Ui`.
