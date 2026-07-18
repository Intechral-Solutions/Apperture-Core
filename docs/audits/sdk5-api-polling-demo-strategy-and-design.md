# SDK-5 — API Polling Demo Strategy & Design

**Date:** 2026-07-18  
**Type:** Design / evidence audit (no module implementation)  
**Prior closed:** SDK-1 through SDK-4C  
**Proposed module id:** `Sample.ApiPolling`  
**Note:** SDK-1 called this candidate `Sample.ApiSync`. Prefer `Sample.ApiPolling` for schedule-first, poll-shaped clarity.

---

## 1. Executive summary

`Sample.FileImport` proved **one-shot row import**, durable operation itemization, per-item retry, and summary signals. The next portable sample should prove **repeatable API-style sync**: poll a fake source, compare against prior state, classify records (new / updated / unchanged / deleted / failed), write durable operation items, and emit exactly one catalog-valid summary signal — runnable from procedure **and** schedule, with **zero external HTTP / Azure / OAuth**.

**Recommended approach**

- Embedded JSON **snapshots** behind a tiny in-process `ISampleDirectoryApiClient` (no real HTTP).
- Process-local `IApiPollingStateStore` for last-seen records (like FileImport’s inspection store).
- Platform `OperationGroups` / `OperationItems` for durable operator history (SDK-3A already shipped).
- Do **not** invent Systems/Services, shared business entity store, or cross-integration lookup yet — but shape metadata so those later epics have clear keys (`externalId`, source label, change kind).

**Next implementation epic:** SDK-5A — implement `Sample.ApiPolling` (fixtures, state store, workflow, itemization, signals, settings, tests, docs).

---

## 2. Recommended sample architecture

```
Procedure / Schedule
        |
        v
Sample.ApiPolling.Poll  (workflow)
  Validate → Poll → Compare → ProcessChanges → Complete
        |
        +-- ISampleDirectoryApiClient.GetSnapshot(snapshotId)
        |       (loads embedded JSON; no network)
        +-- IApiPollingStateStore (process-local last-seen by externalId)
        +-- context.Signals.OperationItems (durable group/items)
        +-- context.Signals.Events (exactly one sync.* signal)
```

| Layer | Choice | Rationale |
|-------|--------|-----------|
| Projects | `Sample.ApiPolling.Contracts` + `Sample.ApiPolling.Module` only | Mirror FileImport; no Persistence project in v1 |
| Transport | In-process fake API client + embedded JSON | Zero setup; teaches connector-shaped abstraction without slots/OAuth |
| Sync state | Process-local `IApiPollingStateStore` | Demo incremental across runs **in one Worker process**; harness-friendly |
| Operator history | Platform operation items (SDK-3A) | Survives restart; Run Detail already works |
| Schedule | Platform schedule targeting workflow id | Same as FileImport / HelloWorld |
| Retry | Deferred | Prefer sync-delta demo first; reuse SDK-4 later if failed records are retryable |

### Complement to FileImport

| | FileImport | ApiPolling |
|--|------------|------------|
| Cadence | One-shot batch | Repeatable poll / schedule |
| Source | CSV fixture | API snapshot fixture |
| Diff | N/A (each run independent) | Compares to last-seen state |
| Items | Row outcomes | Record change classifications |
| Retry story | Per-row retry (SDK-4) | Optional later for failed records |

---

## 3. Fixture / snapshot model

### 3.1 Transport decision (Q1)

**Recommend: embedded JSON snapshots + in-memory fake API client.**

| Option | Verdict |
|--------|---------|
| Embedded JSON only (handlers load resources) | Acceptable but hides “API client” seam |
| **In-memory fake API + embedded JSON** | **Preferred** — validates connector-shaped DI without HTTP |
| Tiny local HTTP fixture server | Reject for v1 — extra ports, CI flakiness, no extra SDK proof |

Conceptual client surface:

```csharp
// Design only — not implemented in this pass
ISampleDirectoryApiClient
  Task<DirectorySnapshot> GetAccountsAsync(
      string snapshotId,
      ApiPollQuery query,
      CancellationToken ct);
```

Unknown `snapshotId` fails Validate (same pattern as unknown FileImport `fixtureId`).

### 3.2 Snapshot catalog

| Snapshot id | Intent |
|-------------|--------|
| `snapshot-initial` | Baseline set (e.g. 4–6 Active accounts) |
| `snapshot-changes` | Same keys with field/version updates + one new id |
| `snapshot-errors` | Mix of valid + invalid records (bad email / missing id) |
| `snapshot-deletions` | Subset of initial keys (missing = deleted) and/or `isDeleted: true` |
| `snapshot-empty` (optional) | Empty page — edge case for unchanged/deleted handling |

Illustrative JSON shape (generic customer/account — not education-specific):

```json
{
  "snapshotId": "snapshot-changes",
  "fetchedAtUtc": "2026-07-18T12:00:00Z",
  "records": [
    {
      "externalId": "A-100",
      "displayName": "Acme Retail",
      "email": "ops@acme.example",
      "status": "Active",
      "tier": "Standard",
      "updatedAtUtc": "2026-07-10T08:00:00Z",
      "version": 2,
      "isDeleted": false
    }
  ]
}
```

**Safety:** No secrets; fake demo emails only; no silent truncation of large payloads into metadata; no raw snapshot dumps in operation item metadata.

---

## 4. State store decision (Q2–Q3)

### Recommendation

**Hybrid (same pattern as FileImport):**

1. **`IApiPollingStateStore` (process-local, in-memory default)** — last-seen record fingerprints by `externalId`, last sync summary, optional scope key (`integrationId` when present).
2. **Platform operation items** — durable per-run history for operators.

### Why not durable module DbContext in SDK-5A?

- Proof/Unisa-style watermarks are real but couple SQL + migrations; Samples should stay zero-setup.
- Incremental demo remains valid **within a Worker lifetime** and fully deterministic in `ModuleTestHarness` (inject store).
- Document clearly: **state resets on process restart**; schedule demos in AppHost may need `resetState=false` and the same Worker.

### Fingerprint

Store per `externalId`:

- `version` when present (preferred)
- else compare `(displayName, email, status, tier, updatedAtUtc, isDeleted)`

### Reset

Procedure parameter `resetState` (bool, default false): clears last-seen for the integration/module scope before Poll — for repeatable manual demos (`snapshot-initial` → `snapshot-changes`).

### Future (out of SDK-5A)

Optional `Sample.ApiPolling.Persistence` / SQL watermark epic when teaching durable incremental sync is the goal (suggested SDK-5B).

---

## 5. Workflow / procedure design (Q8)

### Workflow

- **Id:** `Sample.ApiPolling.Poll`
- **Module:** `Sample.ApiPolling`
- **Steps:** `Validate` → `Poll` → `Compare` → `ProcessChanges` → `Complete`

| Step | Responsibility |
|------|----------------|
| Validate | Known `snapshotId`; clamp pageSize/maxRecords; resolve settings overrides |
| Poll | Call fake API; load snapshot records; fail loud on unknown/invalid structure |
| Compare | Diff snapshot vs state store → change set |
| ProcessChanges | Begin operation group; append one item per classified record; apply state updates for successful classifications; honor fail-on-first / maxRecords |
| Complete | Complete group; persist summary into state store; emit **exactly one** signal |

### Procedure

- **Id:** `Sample.ApiPolling.procedures.poll`
- **Launch:** Workflow → `Sample.ApiPolling.Poll`
- **Parameters:**

| Key | Required | Notes |
|-----|----------|-------|
| `snapshotId` | yes | One of known snapshot ids |
| `resetState` | no | Clear last-seen before poll |
| `strictMode` | no | Override setting; any record failure → `sync.failed` |
| `failOnRecordError` | no | Stop classifying further after first failed record |

### Scheduling

Platform schedule assignment targets workflow definition id `Sample.ApiPolling.Poll` (same pattern as FileImport). Modules do not declare schedule templates. Document that incremental demos across schedule ticks require the **same Worker process** while state is in-memory.

---

## 6. Operation itemization mapping (Q4–Q5)

### Group

- `operationKind`: `api_poll`
- `sourceLabel`: snapshot id
- `summaryJson`: `{ snapshotId, syncBatchId, newCount, updatedCount, unchangedCount, deletedCount, failedCount, skippedCount }`
- `metadataJson`: `{ snapshotId, recordCount, resetState }` — no raw snapshot dump

### Item (one per processed record)

| Classification | Item `Status` | `IsRetryable` | Notes |
|----------------|---------------|---------------|-------|
| new | Succeeded | false | First see of `externalId` |
| updated | Succeeded | false | Fingerprint/version changed |
| unchanged | Succeeded | false | Prefer Succeeded + `changeKind` metadata (keeps Run Detail filters simple) |
| deleted / missing | Succeeded | false | Tombstone observation; not a processing failure |
| failed (validation) | Failed | **true** | Safe error code/message; metadata for possible later retry |
| skipped (maxRecords / fail-on-first) | Skipped | false | Control-flow |

- **SubjectKey:** `externalId`
- **DisplayLabel:** `displayName` or `externalId`
- **ChangeSummaryJson:** compact `{ changeKind, summary }` — e.g. `changeKind=updated fields=email,version` — no full record dump
- **MetadataJson:** `{ externalId, changeKind, version, snapshotId }` — shapes future business-record / cross-integration keys

**ChangeKind vocabulary (metadata, not Status enum):** `new` | `updated` | `unchanged` | `deleted` | `failed` | `skipped`

Map platform item Status to SDK-3A vocabulary only (`Pending` / `Succeeded` / `Failed` / `Skipped`). Put business classification in metadata / change summary so Run Detail stays compatible.

### Counts

- Group `SucceededCount` = new + updated + unchanged + deleted
- `FailedCount` = failed
- `SkippedCount` = skipped
- Terminal group: `Succeeded` / `CompletedWithErrors` / `Failed` (strict or all-failed)

---

## 7. Platform signals (Q6)

Exactly **one** signal per completed sync (FileImport pattern). Dotted names only (underscores are not catalog-valid).

| Signal | When |
|--------|------|
| `sync.completed` | No failures; may include unchanged-only |
| `sync.completed.with.changes` | No failures **and** at least one new / updated / deleted |
| `sync.completed.with.errors` | Partial success (some failed, some ok) |
| `sync.failed` | Hard fail, strict mode with any failure, or all-failed |

**Payload:** `snapshotId`, `syncBatchId`, `operationGroupId`, counts (`newCount`, `updatedCount`, …), `status`.

**Notification seed (optional in implementation epic):** definition for `sync.completed.with.errors` (mirror FileImport partial-failure seed).

---

## 8. Settings contributor (Q7)

Via `IModuleIntegrationSettingDefinitionContributor` (no secrets):

| Key | Default | Purpose |
|-----|---------|---------|
| `pageSize` | 50 | Fake pagination clamp for client query |
| `maxRecordsPerSync` | 100 | Cap processed records |
| `includeDeleted` | true | Whether deletions/missing are classified |
| `failOnRecordError` | false | Stop after first record failure |
| `strictModeDefault` | false | Any failure → `sync.failed` |

Procedure params override settings when provided.

---

## 9. Answers to design questions (index)

1. **Fixtures:** Embedded JSON + in-process fake API client (not live HTTP).
2. **Incremental state:** Last-seen fingerprints by `externalId` in `IApiPollingStateStore`.
3. **Durability:** Process-local state + durable platform operation items (hybrid).
4. **Classifications:** new / updated / unchanged / deleted / failed / skipped (see §6).
5. **Itemization:** One group per sync; one item per record; `changeKind` in metadata.
6. **Signals:** `sync.completed`, `sync.completed.with.changes`, `sync.completed.with.errors`, `sync.failed`.
7. **Settings:** `pageSize`, `maxRecordsPerSync`, `includeDeleted`, `failOnRecordError`, `strictModeDefault`.
8. **Schedules:** Platform targets `Sample.ApiPolling.Poll`.
9. **Future business data:** Stable `externalId`, `changeKind`, source snapshot, operation group link — informs later entity/projection epics without implementing them.
10. **Deferred:** Systems/Services, shared entity store, cross-integration lookup, live HTTP/OAuth/Azure, module `.Ui`, durable watermark SQL, retry-all, production sync engine.

---

## 10. Tests needed (implementation epic)

- Snapshot load / unknown id fails Validate
- Initial then changes: new + updated counts; state fingerprints update
- Deletions snapshot: missing keys → deleted items when `includeDeleted`
- Errors snapshot: failed retryable items + `sync.completed.with.errors` or `sync.failed` under strict
- Unchanged-only poll after same snapshot: items classified unchanged; signal `sync.completed`
- `resetState` clears fingerprints
- Operation group/item metadata has no raw JSON dump / secrets
- Exactly one platform event per Complete
- `ModuleTestHarness` coverage; settings contribution contract test
- Optional: schedule target id smoke (definition id string)

---

## 11. Docs needed

- This audit: `docs/audits/sdk5-api-polling-demo-strategy-and-design.md`
- Implementation plan / spec during SDK-5A (mirror `docs/superpowers/specs/SDK-2-Sample-FileImport-Demo.md`)
- Authoring guide Sample catalog entry
- Explicit gap list: durable watermark, Systems/Services API service, cross-integration by `externalId`
- FileImport vs ApiPolling comparison table

---

## 12. Out-of-scope boundaries

Do **not** implement in SDK-5 / SDK-5A:

- Systems/Services registry
- Shared business entity / projection store
- Cross-integration lookup APIs
- Real HTTP listener or external SaaS
- OAuth / API credential connections
- Azure hosting specifics
- Module `.Ui` shell
- Full UI overhaul
- Retry-all / batch retry / sync policy engine
- Production-grade sync/watermark engine

Optional follow-ons (separate epics):

| Epic | Scope |
|------|--------|
| **SDK-5A** | Implement module per this design |
| **SDK-5B** | Durable SQL watermark for ApiPolling |
| **SDK-5C** | Per-failed-record retry handler (reuse SDK-4) |
| **SDK-6+** | Second sample + shared key for cross-integration design |

---

## 13. Product orientation (business-data-primary)

This sample should make the following **answerable later** without implementing the entity store now:

| Question | How ApiPolling shapes it |
|----------|--------------------------|
| What business record? | `SubjectKey` = `externalId`; displayName label |
| What changed? | `changeKind` + bounded change summary |
| Which source? | Module id + snapshot / sourceLabel; later Systems/Services |
| What did Apperture do? | Operation group `api_poll` + run id |
| What failed? | Failed item errorCode/message; retryable flag for future SDK-5C |
| Cross-integration key? | Stable `externalId` vocabulary shared with future samples |

Platform run/step data remains **supporting**; itemization + external ids are the bridge toward business-primary UX.

---

## 14. Recommended implementation prompt

```text
You are implementing SDK-5A — Sample.ApiPolling Demo Module, following
docs/audits/sdk5-api-polling-demo-strategy-and-design.md.

Do not reopen SDK-1..4C, SWM, PFB, or stabilization unless a direct regression.

Goal: Portable Sample.ApiPolling proving scheduled/API-style sync with embedded
JSON snapshots, in-process fake API client, process-local incremental state,
durable platform operation items, and exactly one sync.* platform signal.
Zero external HTTP/Azure/OAuth. No Systems/Services or business entity store.

Implement:
1. Sample.ApiPolling.Contracts + Sample.ApiPolling.Module (HelloWorld/FileImport layout).
2. Embedded snapshots: snapshot-initial, snapshot-changes, snapshot-errors,
   snapshot-deletions (optional snapshot-empty).
3. ISampleDirectoryApiClient + embedded loader (no network).
4. IApiPollingStateStore in-memory: last-seen by externalId + last summary;
   support resetState; scope by integrationId when available.
5. Workflow Sample.ApiPolling.Poll: Validate → Poll → Compare → ProcessChanges → Complete.
6. Procedure Sample.ApiPolling.procedures.poll (snapshotId required; resetState,
   strictMode, failOnRecordError optional).
7. Operation itemization operationKind=api_poll; one item per record; changeKind in
   metadata; Failed+IsRetryable only for validation failures; no raw snapshot dumps.
8. Signals: sync.completed | sync.completed.with.changes | sync.completed.with.errors
   | sync.failed — exactly one per Complete; fail-loud if Events disabled.
9. IModuleIntegrationSettingDefinitionContributor: pageSize, maxRecordsPerSync,
   includeDeleted, failOnRecordError, strictModeDefault (no secrets).
10. Optional notification seed for sync.completed.with.errors.
11. Register module on Management Host + Worker Host (AddSampleApiPolling pattern).
12. Tests (harness + unit) per SDK-5 design §10; docs: authoring guide + SDK-5A plan/spec.

Out of scope: durable SQL watermark, live HTTP, OAuth, Systems/Services,
cross-integration store, module UI, retry-all, production sync engine.

Validate: unit tests, solution build; optional AppHost smoke: poll initial then
changes; confirm operation items + one signal; confirm schedule can target
Sample.ApiPolling.Poll.

Completion report: projects, snapshots, state store behavior, workflow/procedure,
itemization mapping, signals, settings, tests/docs, build results, deferred (5B/5C).
```
