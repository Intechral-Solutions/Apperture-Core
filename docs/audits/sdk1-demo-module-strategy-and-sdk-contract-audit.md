# SDK-1 — Demo Module Strategy & SDK Contract Audit

**Date:** 2026-07-15  
**Type:** Design / evidence audit (no module implementation)  
**Prior closed tracks:** S1–S13; SWM-1–SWM-12; PFB-1  
**Framing:** Platform is **foundation-ready for product development**, not release-ready. This report designs the **SDK & Demo Module Validation** track so later UI, Systems/Services, business data, and job itemization work sits on proven contracts.

---

## 1. Executive summary

HelloWorld proves the **thin happy path**: workflow + operation + procedures, schedule targeting (platform), platform event emit, notification seed/pipeline, and light integration settings. It does **not** prove file/API connectors as authorable demos, itemized operations, shared business records, cross-integration lookup, rich config UI metadata, or loaded module UI.

**Existing CampusNexus / Unisa / Proof code is useful evidence of real shapes** (SFTP slots, OData sync, module-owned DbContexts, watermarks) but is **not** a portable Sample.* validation suite — credentials, customer coupling, and incomplete “scaffold” steps make them poor module-author references.

**Recommended strategy**

1. Build a small set of **Sample.*** demos that are self-contained (embedded files / local fake API / optional stub “file service”), each designed to **stress one cluster of SDK gaps**.
2. Treat **Unisa/Proof as pattern museums**, not the demo roadmap.
3. Separate **demo module epics** from **platform product epics** (itemization model, shared records, Systems/Services): demos should **force the shape** and store module-local outcomes first; promote to platform only when contracts stabilize.
4. Prefer **realistic row/payload shapes** with **fake transports** (no live CampusNexus / customer SFTP required for Sample demos).

**Recommended first implementation epic: SDK-2 — File Import Demo**

Highest leverage: unlocks run-detail / partial-success / row-outcome pressure for UI overhaul and **Job Operation Itemization & Retry**, without requiring Systems/Services or Azure. Self-contained CSV/JSON; no external credentials.

---

## 2. Current SDK capability map

| Capability | Proven by current code? | Evidence | Gap |
| ---------- | ----------------------- | -------- | --- |
| Procedure execution | **Yes** | HelloWorld procedures (`greet`, `opPing`); `IntegrationProcedureService`; Execute UI | Richer parameter widgets / progressive forms still thin |
| Workflow step pipeline | **Yes** | HelloWorld Validate → DoWork → Complete; step instance recording | No multi-item step outcome model |
| Operation execution | **Yes** | `Sample.HelloWorld.DoWork` via `IOperationDefinitionProvider` | Operations are single-handler; not batch/itemized |
| Scheduled execution | **Yes (platform + target)** | SWM dispatch to HelloWorld workflow id; schedules **not** declared by module | Modules cannot declare schedule templates / readiness hints |
| Platform events (module signals) | **Yes** | Manifest `demo.completed` → `IModulePlatformEventEmitter`; catalog/validators | Few signal variety demos; elevated telemetry rules future |
| Notifications | **Partial** | Platform seed `HelloWorldDemoNotificationDefinitionSeed`; S13 E2E | Modules do not author notification defs; digests/plugins deferred; multi-event demo thin |
| Module integration settings | **Partial** | `IModuleIntegrationSettingDefinitionContributor` (HelloWorld emit + greeting) | WorkerHost consumption of integration settings still deferred; secrets vs non-secrets UX thin |
| Connector configuration / slots | **Yes (production connectors); weak as Sample demo** | CampusNexus + SFTP connectors; Unisa/Proof slot bootstrap; bindings | No Sample demo that teaches slot+connection without CNS/SFTP deps |
| Secret handling | **Partial (platform connections)** | Connection secret refs; settings secret model | Demos must not put secrets in parameters/events (documented); Sample needs secret-vs-plain teaching settings |
| Itemized operations | **No** | Roadmap/PFB only; Entra import item results are identity-only | No platform job-item entity/API/UI |
| Row-level retry | **No** | Run-level cancel/retry/park only | Needs itemization model first |
| Business data projections | **Module-private only** | Proof snapshots; Unisa arrangements/watermarks | No shared entity/projection SDK |
| Cross-integration lookup | **No** | Docs/epics defer cross-integration settings/data | Needs shared record + ownership rules |
| Module UI metadata / navigation | **Hook exists, unused** | `IModuleNavigationProvider`; `.Ui` placeholder not loaded | Authoring story incomplete |
| Service / system modeling | **No** | Pre-release track only | Demo must not invent fake Systems API early |
| Run history / run detail | **Yes (run/step level)** | Runs list/detail; timeline; captured output | No business-record or itemized-child panels |
| ConnectorDefinition in manifest | **Docs-only** | `ConnectorDefinitionDeclaration` comment: not runtime registry | Authors confusable vs `IConnectorDefinitionRegistry` contributors |
| ModuleTestHarness | **Yes** | `Apperture.ModuleSdk.Testing` | Every Sample should use it |
| Capability slots (`IDbConnection`, etc.) | **Yes** | HelloWorld Db slot; Unisa Sftp/Cns/Db; Proof CnsApi | Demo strategies for unbound vs bound slots needed |

### Answers to audit questions (compact)

1. **Proven by HelloWorld:** workflows, operations, procedures + parameter schema, step handlers, signal emit, notification consume path (seeded), light integration settings, optional Db slot, schedule *targeting*.
2. **Unproven (for Samples):** file ingest shapes, API polling/incremental sync, SFTP-like watch, itemized outcomes/retry, shared/cross-integration records, multi-signal notification suites, rich setting/secret metadata, loaded module UI, Systems/Services.
3. **Most revealing demos:** File Import (itemization pressure); Cross-Integration (shared entity pressure); Config/UI Metadata (settings panel pressure); API Poll (connector + incremental); File Drop (service slot + late-file signals).
4. **Before UI overhaul:** File Import + Config Metadata (concrete run-detail and settings surfaces); optional Notification variety for diagnostics UX.
5. **Before job itemization/retry:** File Import that **produces durable per-row outcomes** (module-local OK) so SDK-3 can promote a platform model.
6. **Before cross-integration data access:** Two Sample modules + a deliberate **shared key** (e.g. `externalStudentId`) with module-owned stores; then a platform lookup contract epic.
7. **Fake vs realistic:** Realistic **payload schemas** (student/order CSV columns); fake **transports** (embedded/local files, in-process fake HTTP, stub file client). Avoid live CNS/customer SFTP in Sample suite. Unisa/Proof remain optional “real connector” proofs.
8. **Explicit vs implicit:** Make explicit: item outcome contract, integration-settings runtime read story, connector-binding demo path, signal→notification authoring story, schedule-as-platform-binding. Keep Systems/Services implicit until that track (do not fake `ISystemRegistry` in demos).
9. **Tests per demo:** harness emit/log; step handler unit tests; partial failure fixtures; schedule smoke (platform); setting contribution tests; later integration SQL if module DbContext.
10. **Docs after demos:** extend `module-authoring-guide.md` with Sample catalog; fill `module-types.md`; how-to for slots/settings/signals/file ingest; “what platform owns vs module owns” for items/records.

---

## 3. Demo module candidates

### 3.1 File Import Demo (`Sample.FileImport` — recommended first)

| | |
|--|--|
| **Purpose** | Ingest small CSV/JSON; report per-row success/failure; import summary; partial-success events |
| **SDK capabilities tested** | Multi-step workflow; captured/module-local outcomes; signals for complete/partial/failed; procedure params (file selection or embedded fixture id); settings (strict vs skip-bad-rows) |
| **Product surfaces unlocked** | Run detail item lists; Job Itemization & Retry; notification on partial failure; UI progressive disclosure for import results |
| **Complexity** | **M** |
| **Dependencies** | None external; embedded fixtures under module resources |
| **Priority** | **P0 — first** |

### 3.2 API Polling Demo (`Sample.ApiSync`)

| | |
|--|--|
| **Purpose** | Poll fake/local API; incremental watermark; new/changed/deleted; emit sync-completed |
| **SDK capabilities tested** | Schedule-driven command workflow; watermark persistence (module DbContext pattern from Proof); external IDs; connector-slot *or* in-process fake client |
| **Product surfaces unlocked** | Incremental sync UX; business record freshness; Systems/Services “API service” later |
| **Complexity** | **M–L** |
| **Dependencies** | Prefer in-process `ISampleDirectoryApi` host; optional later real HTTP |
| **Priority** | **P1** (after File Import; can parallel after itemization sketch) |

**Note:** `Proof.CampusNexus.StudentReview` already shows OData sync + snapshots — extract **patterns**, do not require CampusNexus for Sample.

### 3.3 File Drop / SFTP-like Demo (`Sample.FileDrop`)

| | |
|--|--|
| **Purpose** | Watch/pull files via abstract file client; missing/late file signals; schedule cadence |
| **SDK capabilities tested** | Slot resolution (`ISftpFileClient` or demo `IFileDropClient`); unbound-slot graceful path (Unisa today soft-succeeds); late-file notifications |
| **Product surfaces unlocked** | Service connection config UI; schedule + expected-file SLAs; hosting of file services under Systems |
| **Complexity** | **M** |
| **Dependencies** | Local folder or stub client in Aspire; optional PlatformSftp connection for optional “advanced” path |
| **Priority** | **P2** |

**Note:** Unisa PaymentPlans is a partial real SFTP+file workflow — too coupled and scaffolded for authoring demos.

### 3.4 Cross-Integration Demo (`Sample.Records` + `Sample.RecordsLink`)

| | |
|--|--|
| **Purpose** | Two integrations/modules share logical entity keys (e.g. student id) with provenance |
| **SDK capabilities tested** | Module-owned stores; **pressure** for shared lookup API; ownership/provenance fields |
| **Product surfaces unlocked** | Business Data & Cross-Integration; Systems/Services linking |
| **Complexity** | **L** |
| **Dependencies** | Prefer after File Import and/or ApiSync produce durable rows |
| **Priority** | **P2–P3** — **do not invent platform Systems API inside this demo** |

### 3.5 Notification / Event Demo (`Sample.NotifyLab` or HelloWorld expansion)

| | |
|--|--|
| **Purpose** | Multiple signal severities; subscription/diagnostics stress |
| **SDK capabilities tested** | Manifest signal catalog; evaluation; templates; operator diagnostics |
| **Product surfaces unlocked** | Notification prefs / Priority later |
| **Complexity** | **S–M** |
| **Dependencies** | HelloWorld already covers one path — expand only if gaps remain after File Import partial-failure signals |
| **Priority** | **P3** (fold into File Import/ApiSync events where possible) |

### 3.6 Configuration / UI Metadata Demo (`Sample.ConfigLab`)

| | |
|--|--|
| **Purpose** | Richer settings: required/optional, secrets, defaults, restart hints, option providers |
| **SDK capabilities tested** | `IModuleIntegrationSettingDefinitionContributor`; secret vs plain; option providers; WorkerHost read path if ready |
| **Product surfaces unlocked** | Settings panels in UI overhaul; module config/template foundations |
| **Complexity** | **S–M** |
| **Dependencies** | Can run early; strongest after at least one “real” module needs rich config |
| **Priority** | **P1** (often interleaved with File Import as its settings, or as SDK-5) |

---

## 4. Recommended implementation sequence

| Epic | Name | Intent |
| ---- | ---- | ------ |
| **SDK-1** | Demo Module Strategy & SDK Contract Audit | This document — **closed when accepted** |
| **SDK-2** | File Import Demo | Sample module + fixtures + per-row local outcomes + partial-success signals |
| **SDK-3** | Itemized Operation Model (platform) | Promote proven row/item shape to platform persistence/API; **not** full retry UX yet unless thin |
| **SDK-4** | API Polling Demo | Incremental sync Sample with watermark + external IDs |
| **SDK-5** | Configuration Metadata Demo | Rich settings/secrets teaching Sample (or deepen File Import/ApiSync settings) |
| **SDK-6** | File Drop Demo | Abstract file-service + late/missing file signals |
| **SDK-7** | Cross-Integration Demo | Two Samples + shared keys; document platform lookup contract for next product track |
| *(later product)* | Systems/Services; Business Data surfaces; per-item retry UI; Module UI load; Azure | Driven by gaps demos expose |

**Why this order**

- Itemization is the largest unproven **operator** concept and blocks a named roadmap track → File Import first.
- Avoid Cross-Integration / Systems before two modules produce durable keyed records.
- Avoid Azure and full UI overhaul as prerequisites.
- Notification-only demo is low value given HelloWorld + import partial-failure events.
- Unisa/Proof stay as optional advanced references, not sequence blockers.

### Sequencing vs other product tracks

| Product track | Wait for |
| ------------- | -------- |
| Job Itemization & Retry | SDK-2 (outcomes), then SDK-3 (platform model) |
| Business Data / Cross-Integration | SDK-4 and/or SDK-7 |
| Systems / Applications / Services | SDK-6 + SDK-7 pressure (do not start Systems mid-SDK-2) |
| UI overhaul | Can start shell work anytime; **import run-detail** and **settings** panels should wait for SDK-2/SDK-5 fixtures |
| Final Release Readiness | After major product tracks (PFB-1) |

---

## 5. First implementation epic — SDK-2 File Import Demo

### Goal

Ship a portable **`Sample.FileImport`** module that ingests a small CSV (and optional JSON), records **per-row outcomes in a module-owned store**, emits **summary + partial-failure platform signals**, and is runnable from procedure + schedule — **without** implementing platform itemization APIs or per-item retry.

### Scope

- New Sample module projects (`Module` / `Contracts` / optional Persistence; `.Ui` placeholder only if required by template).
- Embedded fixtures (e.g. students or simple orders) with intentional bad rows.
- Workflow: Validate → Parse → ProcessRows → Complete (names flexible).
- Module-local tables: ImportBatch / ImportRowOutcome (or equivalent) keyed by run/correlation.
- Integration settings: e.g. `strictMode`, `greeting`-style non-secret flags; document secret pattern if any path needs one (**prefer no secrets** in SDK-2).
- Manifest signals: e.g. `import.completed`, `import.completed_with_errors`, `import.failed` with required summary properties.
- Platform notification **seed** for at least one signal (HelloWorld pattern) — optional but recommended for partial failure.
- Procedure to start import against a fixture id (and/or uploaded path **out of scope** if storage complexity high — fixtures-first).
- Unit tests + harness tests; WorkerHost/Management registration alongside HelloWorld.
- Authoring-guide section: “Sample.FileImport — file ingest pattern” + explicit **gap list** for SDK-3.

### Out of scope

- Platform `JobOperationItem` tables/APIs / per-item retry
- Cross-integration lookup / Systems/Services model
- Live SFTP / CampusNexus
- UI overhaul / module `.Ui` composition into Management shell
- Azure deployment
- Replacing Unisa/Proof

### Files / areas likely affected

- `src/Apperture.Modules/Sample.FileImport/**` (new)
- WorkerHost + Management Host module registration (`Program.cs` / `AddKnownModules`)
- Optional notification seed in Core.Application (HelloWorld precedent)
- `docs/05-modules/module-authoring-guide.md` (+ short ADR under `docs/02-decisions/` if contracts are contested)
- `tests/Apperture.UnitTests/Sample/FileImport/**`

### Acceptance criteria

1. Operator can launch File Import via procedure and see a run complete with summary in captured/module results.
2. Fixture with mixed good/bad rows yields durable per-row outcomes (queryable via module store or documented diagnostic path).
3. Partial-success signal emitted and catalog-valid; optional notification seed evaluates for that type.
4. Schedule can target the import workflow (platform schedule) without module declaring its own scheduler.
5. Docs state: outcomes are **module-local**; platform itemization is **SDK-3**.
6. No dependency on CampusNexus/SFTP for the happy Sample path.
7. Unit/harness tests green for parse, row outcomes, emit guarantees.

### Tests

- ModuleTestHarness: signal emit contracts
- Handler tests: parse failures, all-fail vs partial vs all-success
- Settings contribution registration tests
- Optional SQL integration test if Persistence project exists (Aspire SQL)

### Docs

- Authoring guide Sample catalog entry
- SDK-2 ADR or section in this audit’s companion ADR (contracts: row outcome fields suggested for promotion)
- Update `pre-release-product-roadmap.md` SDK track with SDK-2…SDK-7 links when implementing

---

## 6. Risks

| Risk | Mitigation |
| ---- | ---------- |
| Overfitting demos to one campus domain | Keep Sample domains generic (students *or* orders); reserve CNS for Proof |
| Adding product features too early (platform itemization inside SDK-2) | Hard out-of-scope; module-local store only |
| Fake abstractions (`ISystem`, shared records) before product track | Cross-integration demo stores **local** copies + shared **key convention** only |
| Delaying UI overhaul forever | Allow shell/layout work in parallel; block only import-detail/settings micro-UX on SDK-2/5 |
| Building itemization before shape is clear | SDK-2 produces real rows; SDK-3 promotes fields from observed outcomes |
| Confusing Unisa/Proof with Sample demos | Docs: Proof = connector evidence; Sample = authorable suite |
| Soft-success on unbound slots masking bugs | File Drop later: explicit “unbound = blocked/attention” policy vs Unisa soft-success |
| Notification seed sprawl | Prefer one seed per critical signal; reuse platform diagnostics |

---

## 7. Recommended next prompt

```text
You are continuing the Apperture SDK & Demo Module Validation track after SDK-1.

SDK-1 (Demo Module Strategy & SDK Contract Audit) is closed — see
docs/audits/sdk1-demo-module-strategy-and-sdk-contract-audit.md.

Do not reopen S1–S13, SWM-1–SWM-12, or PFB-1 unless you find a direct regression.
Do not implement platform itemized retry, Systems/Services, cross-integration lookup,
UI overhaul, or Azure work in this epic.

# SDK-2 — File Import Demo

## Goal

Implement a portable Sample.FileImport demo module that ingests embedded CSV (and optional JSON)
fixtures, records per-row outcomes in a module-owned store, emits import summary /
partial-failure platform signals, and is launchable via procedure and platform schedule —
without building the platform itemization API.

## Implement

1. Create Sample.FileImport module projects following Sample.HelloWorld layout (Module + Contracts;
   Persistence if needed for ImportBatch / ImportRowOutcome).
2. Workflow with clear steps (Validate → Parse → ProcessRows → Complete or equivalent).
3. Embedded fixtures including intentional bad rows; procedure parameter to select fixture id.
4. Module-local durable row outcomes keyed to run/correlation; import summary on complete.
5. Manifest signals (e.g. import.completed, import.completed_with_errors, import.failed) with
   required summary properties; emit from Complete with HelloWorld-quality fail-loud guarantees.
6. Optional platform notification definition seed for partial-failure signal (HelloWorld pattern).
7. Light integration settings (e.g. strictMode) via IModuleIntegrationSettingDefinitionContributor.
8. Register module in WorkerHost and Management Host known modules.
9. Unit + harness tests; keep CampusNexus/SFTP out of the Sample happy path.
10. Update docs/05-modules/module-authoring-guide.md with Sample.FileImport pattern and explicit
    gap list pointing to SDK-3 (Itemized Operation Model).

## Out of scope

- Platform JobOperationItem tables/APIs / per-item retry UI
- Cross-integration shared entity store
- Systems/Applications/Services model
- Module .Ui composition into Management shell
- Live SFTP / CampusNexus
- Azure / packaging / release readiness

## Validation

- Unit + harness tests green
- Solution build
- Manual smoke optional: procedure launch → run detail shows summary; signal visible in platform events

## Completion report

Summarize module paths, signals, settings, tests, docs, and remaining gaps for SDK-3.
Recommend SDK-3 (Itemized Operation Model) or SDK-4 (API Polling Demo) next based on evidence.
```

---

## Appendix A — Evidence anchors

| Topic | Path |
| ----- | ---- |
| HelloWorld module | `src/Apperture.Modules/Sample.HelloWorld/` |
| Module SDK abstractions | `src/Apperture.ModuleSdk/Apperture.ModuleSdk.Abstractions/` |
| Authoring guide | `docs/05-modules/module-authoring-guide.md` |
| CampusNexus connector | `src/Apperture.Connectors/Apperture.Connectors.CampusNexus/` |
| SFTP connector | `src/Apperture.Connectors/Apperture.Connectors.Sftp/` |
| Unisa file workflow | `src/Apperture.Modules/Unisa.CampusNexus.PaymentPlans/` |
| Proof OData sync | `src/Apperture.Modules/Proof.CampusNexus.StudentReview/` |
| Pre-release roadmap | `docs/00-overview/pre-release-product-roadmap.md` |
| PFB-1 baseline | `docs/audits/pfb1-product-foundation-baseline-audit.md` |

## Appendix B — What should become explicit in the SDK (watchlist for later epics)

1. **Job item / operation item** contract (status, external key, error, retryability) — SDK-3  
2. **Integration settings runtime reader** on WorkerHost (if still deferred) — with ConfigLab  
3. **Manifest vs runtime connector definitions** clarification for authors  
4. **Module navigation + future UI contribution** lifecycle (hooks exist; shell load does not)  
5. **Shared business record / provenance** API — after SDK-7 pressure, not before  

---

**SDK-1 status:** Complete (audit/design only).
