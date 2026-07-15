# SDK-2 — Sample.FileImport Demo Design

**Status:** Implemented (SDK-2)  
**Track:** SDK & Demo Module Validation (after SDK-1)  
**Depends on:** `docs/audits/sdk1-demo-module-strategy-and-sdk-contract-audit.md`  
**Out of band:** Do not reopen S1–S13, SWM-1–SWM-12, or PFB-1 unless a direct regression is found.

## 1. Goal

Implement a portable `Sample.FileImport` demo module that validates realistic file-ingest module behavior and pressures future Job Itemization & Retry — without implementing platform itemization APIs.

The module must prove:

- file import workflows
- procedure parameters
- embedded sample fixtures
- per-row outcome capture (module-local, process-local)
- partial success / partial failure
- platform event emission (fail-loud)
- notification readiness (optional minimal seed)
- schedule-driven import (platform schedule targets workflow id)
- integration settings contribution
- run-detail summary fields (counts, batch id, status)

It must not depend on CampusNexus, SFTP, customer credentials, Azure, external APIs, external files, or module `.Ui` shell loading.

## 2. Non-goals (explicit)

Do **not** implement in SDK-2:

- platform `JobOperationItem` tables/APIs
- durable SQL-backed module Persistence project
- per-item retry UI
- cross-integration shared entity store
- Systems / Applications / Services model
- module `.Ui` composition into Management shell
- notification digest/journal, provider plugins, provider-native templates, normalized Priority, team subscriptions
- Azure/deployment / release-readiness work

## 3. Architecture (approved)

| Piece | Choice |
| --- | --- |
| Projects | `Sample.FileImport.Contracts` + `Sample.FileImport.Module` only |
| Persistence | None — no `Sample.FileImport.Persistence` |
| UI | No `.Ui` project / no shell loading |
| Approach | Hybrid workflow **C**: `Validate → Parse → ProcessRows → Complete` |
| Module id | `Sample.FileImport` |
| Workflow id | `Sample.FileImport.Import` |
| Procedure | `Sample.FileImport.procedures.import` → workflow |
| Outcome store | `IImportOutcomeStore` (Contracts) + `InMemoryImportOutcomeStore` (Module, DI singleton) |
| Pattern bar | Mirror HelloWorld: manifest, fail-loud emit, settings contributor, host registration, harness tests |

### Persistence wording (required in docs and completion report)

Use precise language only:

- process-local sample outcome store
- module-local outcome capture
- row outcome shape for future itemization
- **not** durable across process restarts

SDK-3 promotes the proven shape into a durable platform/module itemized operation model.

## 4. Projects and registration

### 4.1 Layout

```
src/Apperture.Modules/Sample.FileImport/
  Sample.FileImport.Contracts/
  Sample.FileImport.Module/
```

### 4.2 Host registration

Register beside HelloWorld where applicable:

- Management.Host known modules (`AddKnownModules`)
- WorkerHost known modules
- tests / ModuleTestHarness registration as needed

No capability-slot bootstrap required for SDK-2 (no Db slot dependency for the happy path).

## 5. Workflow

### 5.1 Steps

1. **Validate** — Resolve `fixtureId` (required). Resolve effective strict mode: procedure `strictMode` overrides setting `strictModeDefault`. Validate settings bounds (e.g. `maxRowsPerImport` > 0). Fail clearly on unknown fixture id when discovery is ready.

2. **Parse** — Load embedded CSV by fixture id. Validate header/columns. Open batch via `IImportOutcomeStore` (`Running`). Produce parse artifacts for ProcessRows (row payloads / line records).  
   - Structurally unusable fixture: prefer creating a failed batch/summary when practical, then let Complete emit `import.failed`.  
   - If the workflow engine makes that awkward, a hard Parse failure that prevents Complete is acceptable (see §5.3).

3. **ProcessRows** — Own row-level success/failure. Append `ImportRowOutcome` per row. Apply `failOnFirstError` and `maxRowsPerImport` semantics (§7). Structurally unparseable lines after a valid header use error code `parse_error`.

4. **Complete** — Finalize batch via `CompleteBatch`. Write operator summary. For flows that reach Complete, emit **exactly one** fail-loud signal (§6).

### 5.2 Batch / row status enums (closed)

**Batch status:** `Running` | `Succeeded` | `CompletedWithErrors` | `Failed`

**Row status:** `Succeeded` | `Failed` | `Skipped`

### 5.3 Structural failure paths (documented)

| Path | Behavior |
| --- | --- |
| Preferred | Parse detects structural failure → create failed batch/summary if possible → Complete emits `import.failed` |
| Acceptable hard-fail | Parse fails the step before normal row processing; **no row outcomes** expected; **`import.failed` may not be emitted** from Complete unless a later failure/finally hook is added |

SDK-2 does **not** add compensation/finally workflow machinery solely to force signal emission on hard Parse failure.

### 5.4 Summary fields (operator / signal / store)

- `fixtureId`, `batchId`
- `totalRows`, `succeededCount`, `failedCount`, `skippedCount`
- `status` (`Succeeded` / `CompletedWithErrors` / `Failed`)
- `runId` / `correlationId` when available from execution context

## 6. Signals

### 6.1 Manifest signals

| Local name | When Complete chooses it |
| --- | --- |
| `import.completed` | Batch `Succeeded` |
| `import.completed.with.errors` | Batch `CompletedWithErrors` (succeeded > 0 and failed > 0) |
| `import.failed` | Batch `Failed` (including all-rows-failed with valid structure) |

> **Naming note:** Platform signal Name pattern is `[a-z0-9]+(\.[a-z0-9]+)*` (no underscores). Spec’s conceptual `import.completed_with_errors` maps to catalog name `import.completed.with.errors`.

Severity: use **existing** platform/module signal catalog severity language only if currently required by `ModuleSignalDeclaration`. Do **not** introduce the future normalized Priority model in SDK-2.

### 6.2 Payload (required catalog properties)

- `fixtureId`, `batchId`
- `totalRows`, `succeededCount`, `failedCount`, `skippedCount`
- `status`

Optional when available: `runId`, `correlationId`

Forbidden: secrets, full raw CSV rows, large fixtures.

### 6.3 Exactly one signal (Complete-scoped)

When the workflow progresses into **Complete**, emit exactly one of the three signals for that batch. Do not emit multiple signals for one completed batch.

Tests must lock this rule (§10).

### 6.4 Fail-loud emission

Follow HelloWorld: check emit result; if not `Emitted`, fail the step with a clear operator summary (do not swallow).

## 7. Integration settings

Contributor via `IModuleIntegrationSettingDefinitionContributor`. No secrets.

| Key | Type | Default | Semantics |
| --- | --- | --- | --- |
| `strictModeDefault` | bool | `false` | Default strict behavior when procedure does not pass `strictMode` |
| `maxRowsPerImport` | int | `1000` | Rows beyond the cap become `Skipped` with a clear reason/error code (not silently ignored) |
| `failOnFirstError` | bool | `false` | After the first `Failed` row, stop processing; remaining **known** unprocessed rows become `Skipped` |

**Overrides:** procedure parameter `strictMode` (optional bool) overrides `strictModeDefault` when provided.

**Strict mode rule (explicit):** when effective strict mode is true and any row has status `Failed`, finalize batch as `Failed` and emit `import.failed` (not `import.completed.with.errors`). Non-strict mixed success/failure remains `CompletedWithErrors` / `import.completed.with.errors`.

## 8. Procedure

- Id: `Sample.FileImport.procedures.import`
- Launch: workflow `Sample.FileImport.Import`
- Parameters:
  - `fixtureId` (string, required) — e.g. `customers-valid`
  - `strictMode` (bool, optional) — overrides `strictModeDefault`

Runnable from existing Execute / Integration Detail flows.

## 9. Fixtures

Embedded CSV resources (generic **customers** domain — not education-canonical):

| Fixture id | Intent |
| --- | --- |
| `customers-valid` | All rows valid → `Succeeded` / `import.completed` |
| `customers-mixed` | Intentionally bad rows → `CompletedWithErrors` / `import.completed.with.errors` (absent strict/fail-first collapse) |
| `customers-invalid-structure` | Structurally unusable → Parse hard-fail / fails before normal row processing; no row outcomes expected |
| `customers-all-bad` | Valid header, every row fails → outcomes recorded; batch `Failed`; exactly one `import.failed` if Complete runs |

**Columns:** `externalId`, `firstName`, `lastName`, `email`, `category`, `status`, `effectiveDate`

**Row error codes:**

| Code | Meaning |
| --- | --- |
| `missing_external_id` | blank/missing external id |
| `invalid_email` | email fails validation |
| `invalid_status` | status not in allowlist |
| `duplicate_external_id` | duplicate within the same import |
| `invalid_date` | malformed `effectiveDate` |
| `parse_error` | line not structurally parseable (ProcessRows, after valid header) |

Plus a clear skip reason/code for max-row / fail-on-first skips (e.g. `skipped_max_rows`, `skipped_fail_on_first_error`).

**`safeRowSummary`:** compact and safe — e.g. external id + email or a short row label. Must **not** store the full raw CSV row.

### 9.1 Invalid fixture test distinction

1. **`customers-invalid-structure`** — fails before normal row processing (Parse hard-fail); no row outcomes expected; `import.failed` may not emit if Complete does not run.
2. **`customers-all-bad`** — valid header; row outcomes recorded; batch `Failed`; if Complete runs, only `import.failed`.

## 10. Outcome store contract

### 10.1 Models

**`ImportBatch`:** batchId, fixtureId, runId?, correlationId?, startedAtUtc, completedAtUtc?, status, totalRows, succeededCount, failedCount, skippedCount

**`ImportRowOutcome`:** batchId, rowNumber, sourceKey (external id or empty), status, errorCode?, errorMessage?, safeRowSummary, createdAtUtc

### 10.2 `IImportOutcomeStore`

- create/open batch (Running)
- append row outcome
- **`CompleteBatch`** (preferred over generic UpdateBatch) — finalize status + counts + completedAtUtc
- get batch by id
- get outcomes by batch id
- get recent batches (optional, for diagnostics)

Thread-safe enough for tests and local AppHost. Tests depend on `IImportOutcomeStore`, not dictionary internals. In-memory implementation registered as **singleton** in Module DI.

## 11. Notification seed gate

**Try:** minimal built-in seed for `import.completed.with.errors` only, mirroring HelloWorld (`*NotificationDefinitionSeed` + existing seed migration pattern + thin tests).

**Defer if** it requires significant new infrastructure (new systems beyond HelloWorld footprint). Then:

- still ship the three signals
- document deferral in authoring guide / completion report
- leave seed wiring for SDK-3 or a later notification pass

Do not let the seed dominate the epic. Do not build templates/provider features beyond the minimal HelloWorld mirror.

## 12. Tests

Focused unit/harness tests under `tests/Apperture.UnitTests/Sample/FileImport/` (or equivalent):

- fixture discovery/loading; unknown fixture id
- `customers-valid` → success summary + only `import.completed`
- `customers-mixed` → module-local row outcomes + only `import.completed.with.errors`
- structurally invalid → fail before normal row processing; no expected row outcomes
- valid structure / all-bad → outcomes recorded + only `import.failed`
- batch summary counts correct
- row failure codes/messages clear; `safeRowSummary` small
- `CompleteBatch` finalizes consistently
- settings contribution registration (three keys)
- procedure parameter shape (`fixtureId` required, `strictMode` optional)
- store swappable via `IImportOutcomeStore`
- fail-loud when emit returns not emitted
- **one signal per completed batch** (explicit cases above)
- schedule targeting compatibility if practical (stable workflow definition id)

No live services, SQL persistence, or external files.

## 13. Documentation updates (implementation phase)

- `docs/05-modules/module-authoring-guide.md` — Sample catalog entry: purpose, how to run, fixture ids, settings, procedure params, signals, outcome shape, process-local store caveats, SDK-3 gap
- SDK/demo roadmap docs if appropriate (`pre-release-product-roadmap.md` or audit companion notes)

## 14. Acceptance criteria

1. `Sample.FileImport` builds and registers like a portable sample module.
2. Operator can launch import procedure with embedded fixture id.
3. Valid fixture → successful summary.
4. Mixed fixture → module-local per-row success/failure outcomes.
5. Invalid fixture fails clearly (structural vs all-bad paths covered in tests).
6. Batch summary counts correct.
7. Partial-success signal emitted and catalog-valid.
8. Optional notification seed for partial failure if implemented under the gate.
9. Settings contributed with clear defaults.
10. Platform schedule can target the import workflow id.
11. No external credentials or live services required.
12. Docs state platform itemization/retry is SDK-3, not SDK-2.
13. Unit/harness tests pass.

## 15. SDK-3 handoff

Proven in SDK-2 for promotion:

- batch + row outcome field shape
- status enums
- error codes
- summary counts for run-detail
- multi-outcome signal pattern (`import.completed` / `import.completed.with.errors` / `import.failed`)

Remaining for SDK-3 (Itemized Operation Model):

- durable platform/module item store
- APIs / UI for item lists and per-item retry
- correlation of items to platform run/operation model beyond process-local capture

## 16. Recommended next epic (post SDK-2)

Default recommendation after implementation evidence: **SDK-3 — Itemized Operation Model**, unless FileImport reveals a deeper scheduling/API gap that elevates SDK-4 (API Polling Demo).

---

## Spec self-review checklist

- [x] No TBD/TODO placeholders left intentional
- [x] Persistence wording matches Option C (process-local, not restart-durable)
- [x] No Persistence / `.Ui` / platform itemization in scope
- [x] Structural hard-fail vs Complete-emitted `import.failed` documented
- [x] Exactly-one-signal scoped to Complete
- [x] Settings override / skip semantics explicit
- [x] Notification seed gate with defer path
- [x] Tests include one-signal-per-batch cases
