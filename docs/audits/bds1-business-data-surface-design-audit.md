# BDS-1 — Business Data Surface Design Audit

**Date:** 2026-07-21  
**Status:** Audit complete — BDS-2 implemented (`docs/audits/bds2-closure.md`); next is BDS-3 demo projection  
**Prior closed tracks:** S1–S13; SWM-1–SWM-12; PFB-1; SDK-1–SDK-6D; FileImport itemization/retry; ApiPolling sync/retry; SDK-6D readiness UX  
**Non-goals this pass:** code, Systems/Services build, platform-wide entity model, UI overhaul, cross-integration writes, Azure/live deps, removal of run/item surfaces

---

## 1. Executive summary

**Recommended first strategy:** staged **demo business-record projection**, not a platform entity model and not Systems/Services first.

1. **BDS-2 — Business metadata alignment** for `Sample.FileImport` + `Sample.ApiPolling` (normalize emitted fields; **deliberately overlap demo keys**; lightly reframe Run Detail itemization as “affected records”).
2. **BDS-3 — Demo `BusinessRecordProjection`** read model that correlates both modules by `recordType` + `businessKey`, with provenance to operation items/runs (and ApiPolling durable last-seen as current-state input).
3. **BDS-4 / BDS-5 — Read APIs + business-first Records UI** with platform details subordinate.

**First surface:** a **demo Customers list/detail** (canonical demo entity = **customer**), clearly labeled sample/demo foundation—not a universal Apperture entity registry.

**Reject for now:** Option D (platform `BusinessRecord` precursor) and Option E (Systems/Services first). ADR-0034 already defers Systems; evidence does not force a platform entity table yet.

**Critical evidence gap:** FileImport fixtures use `C-*` external ids; ApiPolling snapshots use `A-*`. Concepts align (`externalId` as subject); **demo keys do not**. Cross-integration matching cannot be proven until BDS-2 overlaps keys and metadata.

---

## 2. Current evidence

### 2.1 Platform operation itemization

Durable platform model (SDK-3+):

| Concept | Role |
| --- | --- |
| `OperationGroup` | Per-run batch: `IntegrationId`, `ModuleId`, `ProcedureId`, `OperationKind`, `SourceLabel`, counts, summary/metadata JSON |
| `OperationItem` | Per-row/record outcome: `SubjectKey`, `DisplayLabel`, `Status`, errors, `IsRetryable`, `RetryCount`, `ChangeSummaryJson`, `MetadataJson`, timestamps |
| Retry attempts | Child-run retry history (`OperationItemRetryAttempts`); original item preserved |

SDK write path: `IOperationItemWriter` / `OperationItemReporting.BuildAppendRequest`.  
Read/UI: Management query APIs + `RunOperationItemizationPanel` (“Itemized operation history”) with columns **# / Label / Key / Status / Error / Retryable / When / Retry**.

### 2.2 FileImport

- Fixtures: `customers-*.csv` with columns `externalId,firstName,lastName,email,category,status,effectiveDate` (e.g. `C-001` Ada Lovelace).
- Row outcome: `ImportRowOutcome(SourceKey = row.ExternalId, SafeRowSummary ≈ "row=N; id=…; email=…")`.
- Operation item mapping:
  - **SubjectKey** = `SourceKey` (`externalId`)
  - **DisplayLabel** = `SourceKey` or `row N` (**not** person name)
  - **ChangeSummary** = `{ summary: SafeRowSummary }` (no `changeKind`)
  - **Metadata** = `fixtureId`, `rowNumber`, `errorCode` (+ retry ids on retry)
- Module-local `IImportOutcomeStore` remains process-oriented; durable business history for operators is platform operation items.
- Retry: failed rows → child runs; attempt history exists.

### 2.3 ApiPolling

- Fake directory records: `DirectoryAccountRecord(ExternalId, DisplayName, Email, Status, Tier, UpdatedAtUtc, Version, IsDeleted)`.
- Snapshots today: **`A-100`…`A-400`** (Acme Retail, etc.) — **not** the FileImport `C-*` set.
- Classifications: new / updated / unchanged / deleted / failed (+ skipped), with `ChangeKind`, `ChangedFields`, `ChangeSummary`.
- Operation item mapping:
  - **SubjectKey** = `ExternalId`
  - **DisplayLabel** = `DisplayName` else `ExternalId`
  - **ChangeSummary** = `{ changeKind, summary, changedFields }`
  - **Metadata** = `snapshotId`, `sequence`, `includeDeleted`, `changeKind`, `externalId`, `version`, `status`, `tier`, `errorCode` (+ retry)
- **Durable current state (module-owned):** `ApiPollingKnownRecordEntity` / last-seen store — `ExternalId`, `DisplayName`, `Email`, `Status`, `Tier`, `Version`, `LastSeenAtUtc`, `IsDeleted`, `RecordHash`. This is the only sample **current-state** projection today.

### 2.4 Integration readiness (SDK-6D)

Configuration/readiness UX is operator-facing and integration-centered. Useful as **supporting drill-through** from a business record (“why didn’t this sync?”), not as the primary business surface.

### 2.5 Systems / roadmap / product shell

- **ADR-0034:** Systems = deferred future grouping; do not invent placeholder System entities now.
- **Roadmap Track 5:** Business Data & Cross-Integration Records (shared entity, projections, lookup, detail, provenance) — aspirational; Track 6 itemization is largely delivered via SDK-3/4.
- **Shell today:** platform-primary (Integrations, Runs, Schedules, Control Center, notifications). No Records nav. Run Detail already hosts itemization as a **Detail** layer section.

### 2.6 Proof / Unisa / CampusNexus (evidence only)

Real modules show module-private stores, external ids, and sync watermarks. They pressure a future shared record model but are **not** required sample-suite inputs for BDS. Do not couple BDS demos to CNS/SFTP.

---

## 3. Gap analysis

| Gap | Why it blocks “business data primary” |
| --- | --- |
| No current-state projection for FileImport | Import is event history; cannot answer “what customers do we know?” from items alone without aggregation rules |
| Demo key mismatch (`C-*` vs `A-*`) | Cross-integration match is unprovable in the sample suite |
| Uneven display semantics | FI Label = key; AP Label = display name → shared list UX inconsistent |
| Uneven metadata | FI lacks `changeKind`, `displayName`, `recordType`, source labels; AP richer but module-shaped |
| No query-by-business-key API | Platform reads are run/group/item oriented, not “customer C-001 across integrations” |
| Activity ≠ state | Operation items answer “what happened”; users also ask “what is true now?” and “where did that come from?” |
| Shell is platform-first | Records would be a new primary subject; panel framework / nav priority is later UIX |
| No conflict model | Multi-source disagreement not represented |
| Systems not available | OK to proceed with string `sourceSystem` / integration labels; do not wait |

**What is already enough to start:** durable items with subject keys, statuses, retryability, change summaries; AP last-seen; group-level integration/module linkage; Run Detail itemization UX.

---

## 4. Options comparison

| Option | Verdict | Notes |
| --- | --- | --- |
| **A — Operation Item Explorer** | Useful later, **not first primary surface** | Smallest data-wise; still activity/log-shaped; weak “current customer” story |
| **B — Affected Records on Run Detail** | **Do as thin polish in BDS-2**, not the track goal | Immediate value after jobs; remains run-centered; keep existing panel |
| **C — Demo Business Record Projection** | **Preferred path** | Business-first without platform entity commitment; proves cross-integration match once keys align |
| **D — Platform Business Entity precursor** | **Defer** | Premature abstraction; Systems/ownership/permissions still undefined |
| **E — Systems/Services first** | **Defer** | Matches ADR-0034; delays visible product shift |

**Hybrid:** B (thin) → C (core) → A as optional advanced “activity across runs” filter on record detail / explorer later.

---

## 5. Recommended model

### Smallest safe model

**Demo-scoped read projection** (name illustrative): `DemoBusinessRecordProjection`

- **Not** a platform `BusinessRecords` table claiming universality.
- **Not** Systems/Services.
- **Inputs:**
  - Platform `OperationItem` (+ group) history from FileImport + ApiPolling
  - ApiPolling durable last-seen rows as **current-state preference** for that source
  - Explicit demo correlation: `recordType` + `businessKey` (= normalized external id)
- **Outputs (read-only):** list/detail DTOs with display fields, per-source facets, provenance links, conflict flags.
- **Boundary wording:** “Sample / demo business records” in UI copy and docs so it cannot be mistaken for the final Track 5 entity model.

### Why this shape

- Items alone → history explorer (Option A), not business-primary UX.
- AP last-seen alone → one integration’s state; misses FileImport and provenance.
- Platform entity now → commits schema before Systems and multi-module ownership rules exist.
- Demo projection → proves UX + matching + provenance with FileImport/ApiPolling evidence; disposable or promotable later.

### Answers to key design questions (condensed)

| # | Answer |
| --- | --- |
| 1 | Items already carry subject key, label, status, errors, retry flags, change summary, metadata; groups carry integration/module/procedure |
| 2 | **Conceptually** yes (external id); **demo data/metadata** not yet aligned |
| 3 | Need `recordType`, stable `businessKey`, FI `displayName`, shared `changeKind`, source labels, overlapping demo keys; optional `observedAt` |
| 4 | **Hybrid projection** from items + AP sync state — not items-only, not platform entity |
| 5 | History **not** enough for first primary view; need current-state projection (AP last-seen + latest successful item/import observation rules) |
| 6 | **Yes — demo-only first** |
| 7 | **Business surface can proceed**; Systems later |
| 8 | **Customer** (`recordType = customer`); treat ApiPolling “account” as the same demo entity |
| 9 | Per-source facet: integration, module, last observed, latest item/run, change kind; AP known-record when present |
| 10 | Soft conflicts: differing displayName/email/status across sources → `Conflict` badge + field-level compare; no auto-merge |
| 11 | Record → sources → operation item → run (and integration readiness as side link) |
| 12 | Keep Runs/Schedules/Workers/Events/Notifications; subordinate on record pages; keep itemization on Run Detail |
| 13 | Full panel framework, nav primacy redesign, modal taxonomy → UIX |
| 14 | System entity, service browser, system ownership/permissions → SYS |
| 15 | **BDS-2** (metadata + key overlap + affected-records copy) then **BDS-3** projection — first visible product shift without architecture lock-in |

---

## 6. Metadata alignment plan

### Minimal shared fields (emit consistently)

Normalize via **item metadata JSON + existing columns** (avoid expanding platform schema in BDS-2 unless a field is already first-class).

| Field | Where | Required | Notes |
| --- | --- | --- | --- |
| `recordType` | metadata | **Yes** | Constant `customer` for both demos |
| `businessKey` | metadata (mirror of SubjectKey) | **Yes** | Same normalization rules; SubjectKey remains indexed key |
| `displayName` | metadata + improve DisplayLabel | **Yes** | FI: derive from first+last (or email fallback); AP: already has DisplayName |
| `changeKind` | change summary + metadata | **Yes** | FI: `imported` / `failed` / `skipped` (or map from status); AP: keep new/updated/… |
| `sourceIntegrationId` | group already has IntegrationId; optional metadata echo | Nice | Prefer join from group in projection |
| `sourceModuleId` | group ModuleId | Nice | Same |
| `sourceLabel` | group SourceLabel / metadata | Nice | Human “File import” / “Directory API” |
| `observedAt` | metadata or CompletedAtUtc | **Yes** | Prefer item `CompletedAtUtc`; allow metadata override |
| `externalId` | metadata (AP already) | Optional | Alias of businessKey for clarity |

### Explicitly **not** in BDS-2

`matchConfidence`, `sourceService`, full `provenance` blobs, `effectiveAt` (unless already in row data), `latestOperationItemId` on emit (projection computes), Systems ids, write-back fields.

### Demo data alignment (mandatory for BDS-2)

- Overlap at least a **shared key set** between FileImport CSV and ApiPolling snapshots (e.g. keep `C-001`… and add/replace ApiPolling ids, **or** dual-publish both id spaces with a documented `businessKey`).
- Prefer one public demo key: **`C-###` customer ids** in both modules.
- Document intentional conflicts (same `C-001`, different email/status) for conflict UI tests.

### FileImport DisplayLabel fix

Change DisplayLabel from raw external id to human name (keep SubjectKey = external id). Run Detail “Label / Key” columns then read as business-first without a new page.

---

## 7. First business surface concept

### Purpose

Let operators answer: **“Which customers have our integrations touched, what do we believe about them now, and how do I explain that from source → item → run?”**

### User questions

- Which customers appear across FileImport and/or ApiPolling?
- What is the best-known display name / email / status?
- Which sources contributed, and when last observed?
- Are sources in conflict?
- Which operations failed / are retryable?
- How do I open the supporting run and operation item?

### Data source

Read-only **demo projection** built from:

1. Operation items (+ groups) for FileImport & ApiPolling  
2. ApiPolling known-record / last-seen for current attributes  
3. Deterministic merge rules (documented in BDS-3)

### Primary fields (list)

- Display name  
- Business key (`externalId`)  
- Record type (Customer)  
- Sources (count / chips)  
- Last observed  
- Health (OK / Failed ops / Conflict)  
- Retryable failure indicator  

**Not primary:** run id, operation item id, worker host, event type, module CLR names.

### Detail layout (conceptual)

1. **Identity header** — name, key, type  
2. **Current attributes** — merged view + “according to” footnotes  
3. **Sources** — one card/row per integration/module observation  
4. **Recent operations** — last N items (status, change kind, link)  
5. **Supporting / advanced** — raw item ids, run ids, group ids, readiness link  

### Supporting drill-throughs

- Operation item → Run Detail (existing itemization panel, optionally scrolled/highlighted later)  
- Run → Schedule / Integration  
- Integration → SDK-6D configuration/readiness overview  
- Failed item → existing retry action (no new write path)

### Hidden / subordinate

Worker state, platform events stream, notification plumbing, SQL/diagnostics, procedure parameter dumps, secret values.

---

## 8. Navigation and panel implications

Design toward layered UI; **do not implement the panel framework in BDS**.

| Layer | Guidance |
| --- | --- |
| **Primary page** | `/records` or `/demo/customers` (prefer **`/records`** with “Demo customers” subtitle so the product direction is visible) |
| **List → Detail** | Full page detail (not modal) — records are a primary subject |
| **Sources / operations** | In-page sections first; later slide-over panels (UIX) |
| **Platform diagnostics** | Existing pages via links; optional collapsible “Platform links” |
| **Run Detail** | Keep itemization; retitle toward “Affected records”; remain run-scoped |
| **Breadcrumbs** | `Records → {DisplayName} → Run {id}` and `Runs → Run → Affected record → Record` |
| **Modal vs page** | Retry confirmations stay lightweight dialogs; record browsing is full page |
| **Nav** | Add Records entry when BDS-5 ships; do not reorder entire shell until UIX-1 |

Avoid designing APIs that assume System ids or panel-host contracts that do not exist yet.

---

## 9. Implementation sequence

### BDS-2 — Business Metadata Alignment for Sample Modules *(next)*

- Emit minimal shared metadata (`recordType`, `businessKey`, `displayName`, `changeKind`, `observedAt` conventions).
- Fix FileImport DisplayLabel to human name.
- Align demo fixtures/snapshots to overlapping `C-*` (or documented shared) keys; add intentional conflict fixture.
- Thin Run Detail copy/column polish (“Affected records”); no new projection table yet.
- Tests for emit shape parity.

### BDS-3 — Demo Business Record Projection

- Module-or-host demo projector (clear sample boundary): upsert/read by `recordType`+`businessKey`.
- Ingest rules from items + AP last-seen; provenance pointers; conflict detection.
- No platform-wide entity SDK; no Systems coupling.

### BDS-4 — Business Record Read APIs

- Management API list/get (+ optional activity slice) for demo projections.
- Authz consistent with other read surfaces; no writes.

### BDS-5 — Business Record Surface

- Records list/detail UI; drill-through to item/run/integration.
- Nav entry; keep Runs/itemization.

### Later (ordered)

- **BDS-6 (optional):** Operation Item Explorer as advanced cross-run activity filter (Option A), preferably scoped from a record or integration.
- **SYS-1 — Systems/Services Model Design** (audit/design only until revisit triggers fire).
- **UIX-1 — Layered Panel Navigation Design** (panel host, subordinate platform chrome, business-primary IA).
- **Track 5 promotion:** only after demo projection proves value — consider promoting patterns into a platform entity model.

### Explicitly out of sequence until justified

Platform `BusinessRecord` tables, cross-integration writes, live external systems, removing run/item UIs.

---

## 10. Tests needed

### Metadata alignment (BDS-2)

- FileImport and ApiPolling items include required metadata keys with expected values.
- SubjectKey == normalized businessKey.
- DisplayLabel is business-facing (name), SubjectKey remains external id.
- ChangeKind present for success/fail/skip (FI) and classifications (AP).
- Shared demo keys appear in both modules’ golden fixtures.
- Intentional conflict pair differs on at least one compared field.

### Projection (BDS-3+)

- Single-source FileImport-only and ApiPolling-only records project correctly.
- Overlapping key merges into one projection with two source facets.
- AP last-seen wins/loses per documented merge rules (explicit test matrix).
- Latest operation item/run ids on facets match durable stores.
- Failed/retryable items surface on record health without dropping the projection.
- Deleted AP records represented (status/tombstone), not silently omitted.
- Missing/blank external ids never become match keys.

### Provenance & drill-through

- API/UI links resolve to existing run and operation item ids.
- Child retry attempts remain attributable to parent item / business key.

### Conflicts

- Agreeing sources → no conflict.
- Disagreeing email/displayName/status → conflict flag + field diffs.
- One source missing a field → not a conflict.

### Non-coupling

- Arch/unit guards: demo projection code must not reference Systems types/namespaces.
- Platform Core must not require demo projection for operation itemization to function.
- Existing run detail itemization tests remain green.

---

## 11. Docs needed

| Doc | Update |
| --- | --- |
| This audit | Authoritative BDS-1 baseline |
| `docs/05-modules/module-authoring-guide.md` | Business metadata conventions for operation items (minimal field set) |
| Sample module READMEs / authoring notes | Shared `C-*` keys, customer recordType, conflict fixtures |
| `docs/00-overview/pre-release-product-roadmap.md` | Open Track 5 with BDS-2… sequence; note Systems remains deferred |
| New operator guide (BDS-5) | How to use Records vs Runs vs Integration readiness |
| ADR (optional, BDS-3) | “Demo business record projection is not the platform entity model” |
| ADR-0034 | No change required; cite as deferral authority for SYS |

---

## 12. Recommended next implementation prompt

```text
You are implementing BDS-2 — Business Metadata Alignment for Sample Modules.

Prerequisite: docs/audits/bds1-business-data-surface-design-audit.md (BDS-1 closed). Do not reopen SDK-6D or prior tracks unless you find a direct regression.

## Goal

Align Sample.FileImport and Sample.ApiPolling operation-item metadata so both demos can later feed a demo business-record projection. Deliver the smallest durable emit/contract improvements and demo-key overlap. Do not build the projection table, Records UI, Systems/Services, or a platform-wide BusinessRecord entity.

## Requirements

1. Emit minimal shared item metadata from both modules:
   - recordType = "customer"
   - businessKey = normalized external id (same value as SubjectKey)
   - displayName (FileImport: from firstName+lastName or email fallback; ApiPolling: existing DisplayName)
   - changeKind (FileImport: imported|failed|skipped or equivalent mapped from row status; ApiPolling: keep classification kinds)
   - observedAt convention (prefer CompletedAtUtc; document if also echoed in metadata)

2. FileImport DisplayLabel must be human-facing (name), not the raw external id. SubjectKey remains external id.

3. Align demo data so FileImport fixtures and ApiPolling snapshots share an overlapping C-* (or documented) businessKey set. Add at least one intentional conflict pair (same key, differing email or status).

4. Thin Run Detail polish only: reframe itemization section copy toward “Affected records” / business outcomes without redesigning the panel framework or removing platform columns.

5. Tests for metadata parity, DisplayLabel/SubjectKey split, shared keys, and conflict fixture presence. Keep existing itemization/retry tests green.

6. Docs: authoring-guide note for business metadata conventions; sample module notes for shared keys; short BDS-2 closure note under docs/audits/.

## Constraints

- No demo BusinessRecordProjection yet (that is BDS-3).
- No platform BusinessRecord / BusinessRecordSource tables.
- No Systems/Services model.
- No full UI overhaul / new Records nav (BDS-5).
- No cross-integration writes.
- No Azure/live external dependencies.
- Do not remove existing run/operation item surfaces.
- Prefer metadata JSON + existing columns over new platform schema columns unless unavoidable.

## Done when

- Both sample modules emit the minimal aligned metadata.
- Demo keys overlap and conflict fixture exists.
- Run Detail copy is slightly more business-facing.
- Tests + docs updated; closure note written.
```

---

## Appendix A — Evidence anchors

| Topic | Path |
| --- | --- |
| Operation item models | `src/Apperture.ModuleSdk/Apperture.ModuleSdk.Integration/OperationItems/OperationItemizationModels.cs` |
| Reporting helper | `…/OperationItemReporting.cs` |
| FileImport itemization | `src/Apperture.Modules/Sample.FileImport/…/FileImportOperationItemization.cs` |
| FileImport row processor / SourceKey | `…/FileImportRowProcessor.cs`, fixtures under `…/Fixtures/customers-*.csv` |
| ApiPolling itemization | `src/Apperture.Modules/Sample.ApiPolling/…/ApiPollingOperationItemization.cs` |
| ApiPolling models / last-seen | `…/ApiPollingModels.cs`, `…/ApiPollingKnownRecordEntity.cs` |
| ApiPolling snapshots | `…/Snapshots/snapshot-initial.json` (`A-*` today) |
| Run Detail panel | `…/Organisms/RunOperationItemizationPanel.razor` |
| UI helpers | `…/OperationItemizationUiHelpers.cs` (`Itemized operation history`) |
| Systems deferral | `docs/02-decisions/ADR-0034-systems-as-a-logical-grouping-layer.md` |
| Roadmap Track 5 | `docs/00-overview/pre-release-product-roadmap.md` |

## Appendix B — Direct answers checklist (1–15)

See §5 table and §§6–9 for full reasoning; summary in §1.
