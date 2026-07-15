# SDK-3 — Itemized Operation Model Design Audit

**Status:** Complete (design audit only — no implementation in this epic)  
**Track:** SDK & Demo Module Validation  
**Depends on:**  
- SDK-1 — [`docs/audits/sdk1-demo-module-strategy-and-sdk-contract-audit.md`](./sdk1-demo-module-strategy-and-sdk-contract-audit.md)  
- SDK-2 — [`docs/superpowers/specs/SDK-2-Sample-FileImport-Demo.md`](../superpowers/specs/SDK-2-Sample-FileImport-Demo.md) (`Sample.FileImport`)  
**Out of band:** Do not reopen S1–S13, SWM-1–SWM-12, PFB-1, or SDK-2 unless a direct regression is found.

---

## 1. Executive summary

**Recommend a hybrid model:**

| Layer | Owns | Why |
|-------|------|-----|
| **Platform** | Durable `OperationGroup` + `OperationItem` tables, run-detail APIs, indexes, retention, authorization | Run detail, notifications, and schedules already live on platform run/event models; process-local module stores (SDK-2) vanish on restart |
| **Module SDK** | Append-oriented writer surface (`IOperationItemWriter` / recorder) on step execution context | Modules produce items during workflows; same fail-loud quality bar as platform event emit |
| **Module metadata** | Bounded `MetadataJson` / domain-specific codes | Avoid overfitting platform columns to CSV import |

Itemized operations should be treated as an early **business-facing operation history** bridge—not merely run-log detail. Platform run/step timeline remains the **supporting** diagnostic layer.

**SDK-3 implementation (next epics) should ship durable write + read + FileImport adapter.**  
**Per-item retry execution and UI actions belong to SDK-4 (or later).** Include retryability fields now; do not build retry machinery yet.

---

## 2. Current evidence

### 2.1 What the platform run model already provides

`OperationRunEntity` / `RunDetailDto` cover **run-level** and **step-level** truth:

- Integration, procedure, definition, status, correlation, execution origin  
- Captured output (ADR-0019/0020; **64 KiB** cap) and failure diagnostics  
- Step instances (`WorkflowStepInstanceEntity`: step name, attempt, status, error)  
- Run-level **cancel / retry / park / resume** (`RunsController`) via `RetryOfRunId`  
- Timeline aggregation (`IRunTimelineQueryService`)

**Gap:** no child entity for business/file/API/row items under a run. Operators see that a run “completed with a summary,” not which external keys failed.

### 2.2 Platform events and notifications

`PlatformEventEntity` already carries `RunId`, `IntegrationId`, `ModuleId`, `ProcedureId`, `CorrelationId`, and `DataJson`, with indexes for run-scoped queries. SDK-2 FileImport emits:

- `import.completed`  
- `import.completed.with.errors`  
- `import.failed`  

Payload includes fixture/batch ids and counts. Partial-failure notification seed evaluates those events.  
**Linking path today:** event → run id → run detail.  
**Missing path:** event/group → durable item list.

### 2.3 SDK-2 FileImport prototype (proven shape)

Process-local `IImportOutcomeStore` (intentionally **not** restart-durable):

| Prototype | Purpose |
|-----------|---------|
| `ImportBatch` | Group: fixtureId, runId, correlationId, status, counts, timestamps |
| `ImportRowOutcome` | Item: rowNumber, sourceKey, status, errorCode/message, safeRowSummary |
| Batch status | `Running`, `Succeeded`, `CompletedWithErrors`, `Failed` |
| Row status | `Succeeded`, `Failed`, `Skipped` |
| Writer lifecycle | CreateBatch → AppendRowOutcome → CompleteBatch |

This is the shape SDK-3 should **promote**, not reinvent.

### 2.4 Module-owned persistence elsewhere

| Pattern | Evidence | Lesson |
|---------|----------|--------|
| Proof sync run summary | `ProofStudentReviewRunEntity` (counts + status per platform `RunId`) | Module-owned **summary** keyed by run works, but is not a shared item API |
| Proof business snapshot | `ProofStudentReviewSnapshotEntity` (integration + external student id + `SourceRunId`) | True **business records** stay module-private for now; operation items are outcome history, not the entity store |
| Unisa EF module DB | Module DbContexts / migrations | Useful for module domains; not a substitute for cross-module run-detail queries |

### 2.5 Explicit absences

- No `JobOperationItem` / `OperationItem` types in platform persistence today  
- Row-level retry does not exist; only **run-level** retry  
- SDK-1 Appendix B already called out job/operation item contract as SDK-3  

---

## 3. Proposed domain model

### 3.1 Ownership decision

**Hybrid (platform durable store + module writer contract).**  

Reject pure module-owned item tables as the only source of truth for run detail: FileImport already showed process-local loss; every module would need a custom adapter for Management UI.  

Reject embedding items only in `CapturedOutputJson`: size caps, no pagination/filter indexes, and poor fit for business item lists.

### 3.2 Entities

#### `OperationGroup` (parent batch within a run)

Logical “batch of items” produced by one itemized activity inside a run (maps from `ImportBatch`).

| Field | Notes |
|-------|--------|
| `OperationGroupId` | Platform id (stable) |
| `RunId` | **Required** FK to platform run |
| `IntegrationId` | Denormalized for query/auth |
| `ModuleId` | Declaring module |
| `ProcedureId` | Optional |
| `WorkflowDefinitionId` | Optional (supporting platform context) |
| `OperationKind` | Stable string: e.g. `file.import`, `api.sync`, `file.drop` (not Systems/Services) |
| `SourceLabel` | Fixture id, file name, watermark window label, etc. |
| `Status` | See §3.3 |
| `TotalCount`, `SucceededCount`, `FailedCount`, `SkippedCount` | Aggregates |
| `StartedAtUtc`, `CompletedAtUtc` | |
| `SummaryJson` | Bounded operator-safe group summary |
| `MetadataJson` | Bounded module extension |

One run may have **zero or more** groups (v1 FileImport: typically one).

#### `OperationItem` (child)

Maps from `ImportRowOutcome`, generalized beyond CSV.

| Field | Notes |
|-------|--------|
| `OperationItemId` | Platform id |
| `OperationGroupId` | Required |
| `RunId` | Denormalized (query/filter without join fan-out) |
| `Sequence` | Row number / discovery order |
| `SubjectKey` | External/business key (`sourceKey` / student id / file path hash) |
| `DisplayLabel` | Short human label |
| `Status` | See §3.3 |
| `Outcome` | Optional synonym or computed from status for UI copy (“succeeded”) |
| `ErrorCode`, `ErrorMessage` | Bounded |
| `IsRetryable` | Module hint; default false unless set |
| `RetryCount` | Starts at 0; reserved for SDK-4 |
| `LastAttemptedAtUtc`, `CompletedAtUtc` | |
| `ChangeSummaryJson` | Bounded safe “what happened / what would change” |
| `MetadataJson` | Module-specific extension |

**Product-facing terminology preference:** use **subject key**, **operation kind**, **outcome**, **change summary** in APIs/docs. Keep platform run fields as supporting metadata, not the primary story.

### 3.3 Statuses (v1)

**Group** (align SDK-2):  
`Running` | `Succeeded` | `CompletedWithErrors` | `Failed`

**Item:**  
`Pending` | `Succeeded` | `Failed` | `Skipped`  

- `Pending`: allowed for writers that pre-allocate; FileImport may write terminal statuses only.  
- Do **not** add `Resolved` / `Ignored` in v1 storage enums unless needed for explicit operator ack later (defer).

**Partial success:** group `CompletedWithErrors` when `SucceededCount > 0` and `FailedCount > 0` (and not strict-failure rules). Matches FileImport + signals.

### 3.4 Common vs module-specific

| Common (first-class) | Module-specific (`MetadataJson` / codes) |
|----------------------|------------------------------------------|
| Subject key, sequence, status, error code/message | Import CSV column snippets beyond safe summary |
| Display label, retryable hint | API entity etags, OData `@odata.etag` |
| Change summary (bounded) | File checksum, SFTP path segments (redacted) |
| Group operation kind + source label | Fixture-only teaching fields |

### 3.5 Answer map (audit questions)

| # | Answer |
|---|--------|
| 1 | **Hybrid** — platform store + module writer |
| 2 | Attach via **required `RunId`**; denormalize integration/module |
| 3 | **Yes** — `OperationGroup` parent |
| 4 | Subject key, sequence, status, errors, display label, change summary, timestamps, retryability |
| 5 | Domain payloads → `MetadataJson` / coded errors |
| 6 | Group + item statuses above |
| 7 | Group `CompletedWithErrors` + counts; signals already model this |
| 8 | `ErrorCode` + bounded `ErrorMessage` |
| 9 | **No raw files**; bounded `ChangeSummaryJson` / `MetadataJson` only |
| 10 | Events/notifications continue to key on **run**; optionally include `operationGroupId` in event data; UI drills into item list |
| 11 | Fields `IsRetryable`, `RetryCount`, timestamps — **no executor** |
| 12 | **Design only** for retry in SDK-3; implement actions in SDK-4 |
| 13 | Module SDK writer on execution context |
| 14 | **Append-oriented** item writes; finalize group with `Complete` |
| 15 | Indexes `(RunId, Status)`, `(OperationGroupId, Sequence)`; paged APIs |
| 16 | Tie retention to run retention; cascade or orphan-delete with run purge |
| 17 | Secrets ban; size caps; redaction; no PII dumping by default |
| 18 | Run detail: outcome strip + filterable item table (business-first) |
| 19 | Retry UI/executor, Systems/Services, shared entity store, UI shell overhaul, digests |

---

## 4. Module authoring contract

### 4.1 Writer surface (recommended)

Expose from step execution context (same ergonomics as `context.Signals.Events`):

```text
IOperationItemWriter
  BeginGroupAsync(OperationGroupStart)
  AppendItemAsync(OperationItemAppend)      // append-only per item
  CompleteGroupAsync(OperationGroupComplete)
```

Optional batching helper: `AppendItemsAsync(IEnumerable<...>)` for small batches (still append semantics).

### 4.2 Lifecycle

1. **BeginGroup** — creates `Running` group bound to current run/correlation/module/integration.  
2. **AppendItem** — writes terminal or pending items; validates status, size limits, required subject/sequence rules.  
3. **CompleteGroup** — sets final group status + counts (server may recompute counts from items for integrity).  

Fail loudly on writer misconfiguration (similar to platform event emit), without silently dropping item writes when the platform store is unavailable in environments that require durable itemization.

### 4.3 Validation rules (v1)

- `RunId` must be present on the execution context when writing.  
- `ChangeSummaryJson` / `MetadataJson` / `ErrorMessage` length caps (proposal: **2 KiB** summary, **4 KiB** metadata, **1 KiB** error message — tune in implementation).  
- Reject secrets markers / known sensitive keys if a lightweight scanner already exists; otherwise document hard rules.  
- Unknown `OperationKind` allowed as opaque string (catalog later; don’t block modules).

### 4.4 FileImport adaptation

Keep `IImportOutcomeStore` as the sample’s in-process API **or** replace internals with platform writer:

- Prefer: FileImport ProcessRows/Complete call platform writer **and** optionally mirror to in-memory store for unit tests without SQL.  
- Mapping: `ImportBatch` → `OperationGroup`; `ImportRowOutcome` → `OperationItem`; fixture id → `SourceLabel`; `sourceKey` → `SubjectKey`; `safeRowSummary` → `ChangeSummaryJson` or `DisplayLabel`.

---

## 5. Run-detail / API impact

### 5.1 New read APIs (Management)

Under existing runs resource:

| Endpoint (proposed) | Purpose |
|---------------------|---------|
| `GET /runs/{runId}/operation-groups` | List groups + counts for the run |
| `GET /runs/{runId}/operation-groups/{groupId}` | Group detail |
| `GET /runs/{runId}/operation-groups/{groupId}/items?status=&skip=&take=` | Paged/filtered items |

Authorization: same read auth as run detail (`IWorkflowRunReadAuthorizationService` / operation-run equivalent).

### 5.2 Run detail composition

Extend run detail **lightly**:

- Optional summary projection: `HasOperationItems`, primary group counts  
- Keep full item pages on child endpoints (avoid huge `RunDetailDto`)

### 5.3 Notification / event links

- Templates continue linking to **run detail**.  
- Run detail opens **operation items panel** when groups exist (business-first).  
- Optional future deep-link: `?groupId=&status=Failed`.  
- Event payload may add `operationGroupId` when available (non-breaking if optional).

### 5.4 UI v1 (run detail panel — SDK-3D)

Primary content:

- Operation kind + source label  
- Outcome counts  
- Filter chips: All / Failed / Succeeded / Skipped  
- Table: sequence, subject key, display label, status, error  

Supporting / advanced:

- Existing step timeline, captured output, forensic context  
- Raw `MetadataJson` behind “advanced”

Do **not** load module `.Ui` packages for this.

---

## 6. Retry readiness

| Include in SDK-3 | Defer (SDK-4+) |
|------------------|----------------|
| `IsRetryable`, `RetryCount`, attempt timestamps | Per-item retry command API |
| Docs: “retryable means eligible for future retry” | Retry UI buttons / bulk retry |
| Tests for field persistence | New run / sub-workflow relaunch of single items |
| | “Resolved without retry” operator ack |

**Rationale:** Run-level retry already exists; item retry needs durable items **first**, then a clear semantics story (re-enqueue work vs mark resolved). Shipping retry UI before solid read model would lock bad UX.

---

## 7. Security / privacy

Rules (promote SDK-2 FileImport discipline to platform):

1. **No secrets** in summaries, metadata, or errors (tokens, connection strings, passwords).  
2. **No raw full files** or full CSV rows on items; compact safe summaries only.  
3. **Hard size bounds** on JSON fields (reject or truncate with status — prefer reject at write for predictability).  
4. **PII minimization:** subject key + display label + short change summary; avoid dumping emails/SSNs unless the module’s product purpose requires a redacted form (FileImport already uses compact `id` + `email` summary — keep bounded).  
5. **Retention:** delete/cascade with run purge; document that groups/items are not a forever business warehouse (Proof snapshots remain the “latest state” pattern).  
6. **Authz:** items inherit run integration scope; no cross-integration listing in v1.

---

## 8. Implementation sequence

| Slice | Scope | Outcome |
|-------|--------|---------|
| **SDK-3A** | Domain entities, SQL persistence, indexes, `IOperationItemWriter`, DI on WorkerHost | Durable write path |
| **SDK-3B** | Management read APIs + DTOs + auth; optional run-detail summary hooks | Queryable itemization |
| **SDK-3C** | `Sample.FileImport` adapter → platform writer (keep harness tests portable) | End-to-end demo of durable items |
| **SDK-3D** | Run-detail UI panel (filter/paginate; business-first) | Operator visibility |
| **SDK-4** | Per-item retry semantics + actions + UI | Operational recovery |

Do **not** combine 3A–3D into one mega-PR if avoidable; 3A must be independently testable.

---

## 9. Tests needed

- Group/item persistence CRUD-ish + Complete aggregation integrity  
- Append-only writer happy path and fail-loud when store unavailable (policy TBD in 3A)  
- Status/count aggregation matches FileImport strict vs non-strict rules when adapted  
- Run attachment rejects writes without `RunId`  
- Pagination + status filter correctness and index-friendly queries  
- FileImport integration: mixed fixture → durable items + existing signals still emit  
- Notification/event: run link still works; optional group id in payload if added  
- Security: oversized payload rejected; secret-like content rejected or documented skip  
- Authorization: cannot read another integration’s items  
- Regression: run-level cancel/retry/park unchanged  

---

## 10. Docs needed

- `docs/05-modules/module-authoring-guide.md` — Operation Itemization section (writer lifecycle, safe payload rules, FileImport example)  
- Operator/run-detail note: distinguishing **steps** (platform) vs **operation items** (business outcomes)  
- Update `docs/00-overview/roadmap.md` / pre-release roadmap after 3A starts  
- Optional short ADR under `docs/02-decisions/` when field names are contested (OperationGroup/Item terminology)  

---

## 11. Recommended next implementation prompt

```text
You are continuing the Apperture SDK & Demo Module Validation track after SDK-3 design audit.

SDK-1 and SDK-2 are closed. SDK-3 design audit is closed — see:
docs/audits/sdk3-itemized-operation-model-design-audit.md

Do not reopen S1–S13, SWM-1–SWM-12, PFB-1, or SDK-2 unless you find a direct regression.
Do not implement Systems/Services, cross-integration entity store, UI shell overhaul,
module .Ui loading, Azure/deployment, or per-item retry execution/UI in this epic.

# SDK-3A — Operation Itemization Persistence & Writer

## Goal

Implement the platform-durable OperationGroup / OperationItem store and the module
authoring writer contract, following the hybrid model in the SDK-3 audit.

## Implement

1. Domain + SQL entities for OperationGroup and OperationItem with v1 statuses and fields
   from the audit (including IsRetryable/RetryCount placeholders; no retry executor).
2. Indexes for (RunId), (RunId, Status), (OperationGroupId, Sequence).
3. Module SDK writer (IOperationItemWriter or equivalent) available on step execution context:
   BeginGroup → AppendItem → CompleteGroup (append-oriented).
4. Size/validation rules for safe summaries and metadata; no raw files; no secrets.
5. WorkerHost DI registration for durable writer against platform SQL.
6. Unit/integration tests for persistence, aggregation, missing RunId, and payload bounds.
7. Docs: authoring guide section for operation itemization + pointer that SDK-3B is read APIs,
   SDK-3C FileImport adapter, SDK-3D UI panel, SDK-4 retry.

## Out of scope

- Management read APIs / run-detail UI panel (SDK-3B/3D)
- FileImport adapter (SDK-3C) unless trivial and already unblocked
- Per-item retry actions/UI (SDK-4)
- Systems/Services, shared business records, notification digests

## Validation

- Persistence + writer tests green
- Solution build
- Confirm run-level cancel/retry still green (no regression)

## Completion report

Summarize schema, writer API, tests, docs, and recommended SDK-3B prompt.
```

> **Status update (2026-07-15):** SDK-3A and **SDK-3B** are implemented. Read APIs: `GET /api/runs/{runId}/operation-groups` and `…/items`. Plan: `docs/superpowers/plans/2026-07-15-sdk3b-operation-itemization-read-apis.md`. Next: **SDK-3C** FileImport adapter.

---

## Appendix A — Mapping from SDK-2 → SDK-3

| SDK-2 | SDK-3 |
|-------|--------|
| `ImportBatch` | `OperationGroup` |
| `ImportRowOutcome` | `OperationItem` |
| `FixtureId` | `SourceLabel` (+ metadata if needed) |
| `SourceKey` | `SubjectKey` |
| `SafeRowSummary` | `DisplayLabel` and/or `ChangeSummaryJson` |
| `IImportOutcomeStore` (process-local) | `IOperationItemWriter` → platform store (+ optional test double) |
| Signals `import.*` | Keep; optionally add `operationGroupId` |

## Appendix B — What this is not

- Not a shared CRM/SIS entity graph (later demos / SDK-7 pressure)  
- Not Systems/Applications/Services catalog  
- Not a replacement for step instance recording  
- Not an unbounded audit log of every byte processed  

---

**SDK-3 status:** Design audit complete. Next implementation epic: **SDK-3A — Operation Itemization Persistence & Writer**.
