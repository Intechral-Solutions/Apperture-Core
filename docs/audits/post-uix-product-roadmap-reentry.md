# Post-UIX Product Roadmap Re-entry

**Status:** **Accepted (2026-08-05).** Roadmap reconciliation applied with JOB-1 closure.
**Date:** 2026-08-05
**Related:** [pre-release roadmap](../00-overview/pre-release-product-roadmap.md), [PFB-1](./pfb1-product-foundation-baseline-audit.md), [BDS-1](./bds1-business-data-surface-design-audit.md), [BDS-6](./bds6-cross-run-business-record-activity-lineage.md), [UIX-5](./uix5-supporting-surface-pattern-audit.md), [UIX-6](./uix6-interaction-progressive-disclosure-system.md), [SDK-3](./sdk3-itemized-operation-model-design-audit.md), [SDK-4](./sdk4-per-item-retry-design-audit.md), [SDK-6D closure](./sdk6d-closure.md), [JOB-1 closure](./job1-failed-work-resolution-bulk-retry.md)

> **Supersession:** JOB-1 is implemented and Track 6 is marked complete. Historical recommendation text below is preserved.

---

## 1. Executive recommendation

> **Next epic: JOB-1 — Failed-Work Resolution and Bulk Retry.**

Close the last open bullets of **Track 6 — Job Operation Itemization & Retry**: bulk/selective retry of
failed operation items, an explicit operator **resolve / ignore** acknowledgement for items that will
never be retried, and operation blocked-work handling that is no longer inspection-only.

This is the only candidate that is simultaneously (a) a concrete operator-visible gap reachable in
today's UI, (b) architecturally ready with every prerequisite already delivered, and (c) the
completion of a **named roadmap track** rather than the opening of a new one.

---

## 2. Current roadmap reconciliation

### Complete (verified in repository)

| Area | Evidence |
| --- | --- |
| **BDS-1 … BDS-6** | Closure docs present; Demo Customer list/detail/activity routes live |
| **UIX-1 … UIX-6** | Closure docs present; `Disclosure`/`TechnicalDetails`/`ManagementDialog`/`ConfirmActionDialog` shipped |
| **Shared-record reverse links** | `SharedRecordLinkCatalog` + itemization panel link |
| **SDK-1 … SDK-6D** | Every SDK epic has a closure or "Implemented" status; SDK-6D closure: *"SDK-6D can be marked closed."* |
| **Stabilization S1–S13** | *"Stabilization S1–S13 is closed."* |
| **SWM-1 … SWM-12** | Scheduling/worker foundation closed; SWM-9a and SWM-12 delivered |
| **Track 6 (partially)** | Itemized operations, row outcomes, failed-item diagnostics, **per-item** retry — all delivered via SDK-3A–3D and SDK-4A–4C |

### Stale — says pending, is actually complete

| Location | Stale text | Reality |
| --- | --- | --- |
| `pre-release-product-roadmap.md` Track 3, line ~87 | *"**Next epic:** **SDK-3A Operation Itemization Persistence & Writer**"* | SDK-3A–3D, 4A–4C, 5A–5C, 6A–6D all closed |
| `pre-release-product-roadmap.md` Track 5, line ~116 | *"UIX-1–UIX-5 are delivered … next UI epic is **UIX-6**"* | UIX-6 implemented |
| `pre-release-product-roadmap.md` line ~55 | UIX-6 *"(operator smoke pending)"* | Operator smoke for the new dialogs **succeeded** (user-confirmed this session) |
| `pre-release-product-roadmap.md` line ~110 | BDS-6 *"Operator smoke pending"* | Unconfirmed — see §7 |
| `pre-release-product-roadmap.md` lines ~57–59 | *"Next UI step is the combined BDS-6 + UIX-6 operator smoke"* | Partly satisfied |
| `bds1…md` line ~299, `bds5…md` line ~88, `uix3-affected-record…md` line ~107 | *"UIX-6 — Technical disclosure and confirm-action consistency **(next)**"* | Delivered, and broadened beyond that title |
| `sdk3-itemized-operation-model-design-audit.md` footer | *"Next implementation epic: **SDK-3A**"* | Contradicted by that doc's own 2026-07-15 status update |
| `pfb1…md` §12 | *"Recommended next track: SDK & Demo Module Validation"* | That track is materially complete through SDK-6D |
| `pre-release-product-roadmap.md` Track 6 | No status at all | Mostly delivered by SDK-3/SDK-4; only resolution + bulk retry remain |

### Superseded / historical (do **not** rewrite)

- **BDS-6 as "Operation Item Explorer"** — historical framing in `bds1…md` §Option A, `bds2-closure.md` line 62, and `uix3-readiness-bds6-decision.md`. The accepted redefinition is already recorded in `bds1`, `bds4`, `bds5`, `uix3-*`; `bds2-closure.md` line 62 is the one remaining unannotated stale reference.
- **SDK-1 numbering** — original SDK-4/5/6/7 plan was renumbered during delivery (API Polling shipped as SDK-5A–5C; Integration SDK consolidation as SDK-6A–6D, which did not exist in the SDK-1 plan). Preserve as history.
- **RR-1** — already marked *"Superseded by PFB-1."*

### Deferred and should stay deferred

| Item | Why |
| --- | --- |
| Inspection panel / drawer / popover / accordion | UIX-5 trigger criteria unmet |
| Management-owned WorkerHost start/stop | SWM-9 recommends deferral; API quarantined at 410 |
| Systems / Services model (Track 4, SYS-1) | ADR-0034 defers; no forcing evidence |
| Platform `BusinessRecord` entity model | BDS-1: promote only after a **second** entity type proves reuse |
| Azure / deployment (Track 7) | Explicitly *"Do not start this track as a substitute for product completeness"* |
| Dashboard management pages | Marked *"Deferred — not part of the operator shell"* |
| Calendar / date-period scheduling | Deferred in `SchedulesList.razor` |
| Final Release Readiness (Track 9) | Product work incomplete |
| Remaining raw `<details>` migrations | Opportunistic, per UIX-6 §12 — not an epic |

### Prerequisites newly satisfied by the BDS/UIX arc

| Now unblocked | Unblocked by |
| --- | --- |
| Safe **bulk consequential actions** in the UI | UIX-6 `ConfirmActionDialog` (accessible, busy-guarded, failure-preserving) |
| Verifying a bulk retry across runs from a business record | BDS-6 cross-run Activity + UIX-3/4 drill-through |
| Dense per-item diagnostics without page bloat | UIX-6 `TechnicalDetails` |
| Cross-integration shared-key evidence (for a later entity decision) | BDS-2…BDS-6 + reverse links |

### Open defects vs new capability

No open **stabilization defect** was found that blocks product work. The gaps below are missing
**capability**, not regressions:

- `POST api/control/recovery/blocked-work/{runId}/retry` returns **400** for operations:
  *"Operation retry not yet supported."* with `inspectionOnly: true`
- `OperationItemizationUiHelpers`: *"Retry all / batch retry is not available yet."*
- `DELETE` for schedules exists in neither UI nor `SchedulesController`
- `IntegrationDetail.razor`: *"Module-specific schema support is deferred"* (legacy key-value section only;
  declaration-driven settings shipped in SDK-6C/6D)

---

## 3. Candidate comparison

| | **A. JOB-1 — Failed-work resolution & bulk retry** | **B. SDK-7 — Cross-integration records & shared-key lookup** | **C. INT-1 — Integration authoring depth (module-defined settings schema)** |
| --- | --- | --- | --- |
| **Operator value** | **High** — today a 200-row failed import needs 200 individual retries; no way to close out items that will never succeed | Medium — mostly a *platform* capability; operator-visible payoff arrives one epic later | Medium — improves authoring/config quality; SDK-6C/6D already covers declared settings |
| **Product completeness** | **High** — closes the last bullets of named Track 6 | Medium — opens Track 5's deferred entity model | Low–Medium — narrows an already-narrow legacy gap |
| **Architectural readiness** | **High** — retry dispatch, reconciliation, idempotency keys, child runs all exist (SDK-4A–4C) | Medium — needs new ownership/provenance and lookup contracts | Medium — needs a schema contract in the module SDK |
| **Dependency readiness** | **All satisfied** — SDK-3/4 + UIX-6 dialog + BDS-6 verification | Satisfied for *evidence*; BDS-1 still says wait for a second entity type | Satisfied |
| **Risk** | Moderate, bounded — dispatch storms, idempotency collisions, partial-success reporting | **High** — risks inventing the platform entity model prematurely, which BDS-1 explicitly warns against | Low–Moderate — SDK contract change ripples to module authors |
| **Scope clarity** | **High** — statuses, batch endpoint, selection UI, confirmation | Low — "shared lookup contract" is not yet specified | Medium |
| **Advances core purpose** | **Understand outcomes / recover from failure** — directly | Investigate business records — indirectly | Configure integrations — partially |
| **Verdict** | **Recommended** | Then (see §5) | Fold opportunistically |

Also considered and rejected as *next*: schedule delete (defect-sized, not an epic); notification
"live send from UI" (narrow, diagnostics already exist); worker lifecycle adapters (explicitly
deferred post-v1); another UIX epic (UIX-6 §12 migrations are opportunistic by design).

---

## 4. Recommended epic definition

### JOB-1 — Failed-Work Resolution and Bulk Retry

**Operator problem**

> An import or sync produced many failed rows. I can see each one and retry them one at a time,
> but I cannot retry them together, and I cannot mark the ones that will never succeed as handled —
> so the run looks permanently broken and my attention list never clears.

**Product outcome**

From Run Detail (and, where identifiers allow, from Demo Customer Activity), an operator can select
failed operation items, retry them as one bounded, idempotent, confirmed action, watch the batch
resolve, and explicitly **resolve** or **ignore** items that will not be retried — so runs and
attention state reach a truthful terminal condition.

**Why now**

1. It is the only remaining scope of a **named roadmap track** (Track 6).
2. Every prerequisite is delivered: per-item retry dispatch and reconciliation (SDK-4A–4C), durable
   item model (SDK-3A–3D), cross-run verification (BDS-6), and an accessible confirmation pattern for
   a consequential bulk action (UIX-6) — the last of which did not exist before this arc.
3. It needs no new architectural concept: batch is a bounded application of an existing, proven
   single-item operation.
4. It is operator-visible product work, not infrastructure or cleanup.

**Prerequisites already complete**

Operation item persistence and writer · read APIs · FileImport/ApiPolling adapters · Run Detail
itemization panel · per-item retry dispatch, reconciliation, and UI · idempotency keys and child-run
model · `ConfirmActionDialog` · cross-run activity for verification.

**Explicit scope**

1. **Bulk retry**
   - Select failed, retryable items within an operation group (select-all-visible and explicit
     selection; server-enforced bound on batch size).
   - One batch endpoint that reuses the existing per-item dispatch semantics and idempotency keys.
   - Deterministic per-item results: dispatched / skipped-not-retryable / failed-to-dispatch.
   - Confirmation via `ConfirmActionDialog` with the affected count; failure keeps the dialog open.
2. **Resolution acknowledgement**
   - Add the deferred operator-acknowledgement states — SDK-3 §"Do **not** add `Resolved` / `Ignored`
     in v1 storage enums unless needed for explicit operator ack later (defer)". That condition is now met.
   - Persist who resolved, when, and an optional reason; surface in item history and audit.
   - Resolved/ignored items leave the "needs attention" population without falsifying the run outcome.
3. **Operation blocked-work retry**
   - Replace the `inspectionOnly: true` 400 in `RecoveryController` with real operation retry, or
     record an explicit architectural decision that it stays inspection-only and remove the
     "not yet supported" framing.
4. **Attention/outcome truthfulness**
   - Ensure group/run rollups and attention counts reflect bulk retry and resolution.

**Explicit exclusions**

Retry *policies* and automatic/scheduled retry · cross-run "retry everything for this customer" ·
retry from Demo Customer Activity as a bulk surface (link-through only) · run-level redesign ·
scheduler or WorkerHost lifecycle changes · Systems/Services · platform `BusinessRecord` model ·
inspection panel or drawer · new business-data APIs · module-contributed UI · Azure work ·
batch retry across integrations.

**Likely projects and surfaces affected**

| Project | Change |
| --- | --- |
| `Apperture.Core.Application` (`OperationItems`) | Batch retry service; resolution states and transitions; validation |
| `Apperture.Persistence.SqlServer` | Migration for resolution columns/status values; indexes if selection queries need them |
| `Apperture.Management.Api` (`RunsController`, `RecoveryController`) | Batch retry endpoint; resolve/ignore endpoint; operation blocked-work decision |
| `Apperture.Management.Client` | DTOs + client methods |
| `Apperture.Management.Ui.Client` (`RunOperationItemizationPanel`, `BlockedWork`) | Selection UI, bulk action, confirmation, resolution controls, result summary |
| `Apperture.Management.Ui.Shared` | Selection/labels/status helpers (testable) |
| `Apperture.ModuleSdk.Integration` | Only if status vocabulary is shared with modules — prefer **platform-only** states to avoid an SDK contract change |

**Key architectural risks**

1. **Dispatch storm** — a large batch could flood the runtime plane. Mitigate with a server-enforced
   maximum batch size and sequential/throttled dispatch.
2. **Idempotency collisions** — batch must not reuse a single key across items; derive per item and
   keep the existing collision semantics.
3. **Partial success semantics** — the response and UI must express "12 dispatched, 3 skipped, 1 failed"
   without implying wholesale success.
4. **Status truthfulness** — `Resolved`/`Ignored` must not be confusable with `Succeeded`; run and group
   rollups must stay honest, and BDS-6 activity outcomes must not be silently rewritten.
5. **Authorization** — bulk retry and resolution must enforce the same permissions as single-item retry;
   resolution must be audited.
6. **Module contract creep** — keep operator acknowledgement a platform concern; do not require modules
   to emit new statuses.

**Test requirements**

- Unit: batch selection/bounding, per-item result mapping, idempotency key derivation, status transition
  legality (including illegal transitions), rollup recomputation, authorization.
- SQL integration: migration correctness, resolution persistence, concurrent batch safety, no
  cross-scope leakage.
- API: batch endpoint success/partial/forbidden/not-found, batch-size rejection, resolve/ignore
  validation, blocked-work operation path per the chosen decision, stable response model.
- UI (source/helper conventions in this repo): selection state, confirm dialog wiring, partial-result
  summary, disabled states for non-retryable items, resolution controls, operator-readable labels,
  no planning terminology.
- Regression: keep `Sdk3D*`, `Sdk4C*`, `Bds5*`, `Bds6*`, `Uix2*`–`Uix6*`, `AuthShell*`, `Epic101*` green.

**Runtime smoke flow**

1. Run a FileImport fixture producing many failures.
2. Open Run Detail → Affected records; select multiple failed items; confirm bulk retry.
3. Verify bounded dispatch, per-item outcome, and no duplicate child runs.
4. Verify a failing batch keeps the dialog open with the reason.
5. Mark remaining items resolved/ignored with a reason; verify attention counts clear and audit records exist.
6. Open the affected Demo Customer's Activity and confirm the new attempts appear with correct outcomes.
7. Exercise operation blocked-work per the chosen decision.
8. Console clean (no `parentNode`, `Symbol()`, unhandled render exceptions, duplicate submissions).

**Definition of done**

Bulk retry works with server-enforced bounds and per-item results · resolution states persist with
actor, time, and reason · attention and rollups are truthful · operation blocked-work is either
supported or explicitly decided and relabeled · confirmation reuses UIX-6 · authorization and audit
preserved · tests pass · smoke documented · Track 6 marked complete in the roadmap.

---

## 5. Following sequence

```text
[next]     JOB-1 — Failed-Work Resolution and Bulk Retry (closes Track 6)
[then]     SDK-7 — Cross-Integration Records and Shared-Key Lookup Contract
           (two Sample modules already share keys; produces the evidence and the
            contract needed to decide the platform BusinessRecord model — decision
            only, not the platform entity model itself)
[deferred] Inspection panel / drawer / popover / accordion (UIX-5 triggers unmet)
[deferred] Platform BusinessRecord entity model (needs a second entity type)
[deferred] Systems / Services model — Track 4 / SYS-1 (ADR-0034)
[deferred] Management-owned WorkerHost lifecycle + hosting adapters (SWM-9, post-v1)
[deferred] Dashboard management, calendar scheduling, module-contributed UI
[deferred] Azure / deployment — Track 7
[deferred] Final Release Readiness — Track 9
[opportunistic] Remaining raw <details> migrations (UIX-6 §12); schedule delete;
                integration legacy key-value settings section
```

---

## 6. Roadmap updates to apply **after architectural acceptance**

Not applied in this session.

1. `docs/00-overview/pre-release-product-roadmap.md`
   - Track 3: replace *"Next epic: SDK-3A…"* with "SDK-1–SDK-6D complete; remaining SDK-6E items are
     polish, listed in `sdk6d-closure.md`."
   - Track 5 note: update to "BDS-1–BDS-6 and UIX-1–UIX-6 delivered."
   - UIX-6 row: drop *"(operator smoke pending)"* — dialogs smoke succeeded.
   - BDS-6 bullet: update smoke status once confirmed (see §7).
   - Track 6: add a status block — itemization, row outcomes, diagnostics, and per-item retry delivered
     via SDK-3/SDK-4; **JOB-1** carries the remaining resolution + bulk retry scope.
   - Add JOB-1 as the accepted next epic.
2. `docs/audits/bds2-closure.md` line 62 — annotate the "Operation Item Explorer" reference with the
   accepted BDS-6 redefinition (preserve the original wording).
3. `docs/audits/sdk3-itemized-operation-model-design-audit.md` footer — annotate that the "Next
   implementation epic: SDK-3A" line is superseded by the doc's own status update.
4. `docs/audits/bds1…md`, `bds5…md`, `uix3-affected-record…md` — change UIX-6 from *(next)* to
   implemented, linking `uix6-interaction-progressive-disclosure-system.md`.
5. `docs/audits/pfb1…md` §12 — add a dated note that the recommended SDK track is now materially complete.
6. On JOB-1 acceptance — create `docs/audits/job1-*.md` at closure per existing convention.

---

## 7. Uncertainties

1. **Unit and Architecture test status could not be confirmed.** Both runs failed at
   `MSB3027`/`MSB3021` while copying module assemblies; `Apperture.AppHost` (101512),
   `Apperture.Management.Host` (67748), and `Apperture.RuntimeHost.Windows.WorkerHost` (78528) were
   running from the smoke session. No compilation error was reported for any changed file.
   `Apperture.Management.Ui.Shared.Tests` passed **572/572** and `Apperture.Management.Ui.Client` built
   cleanly. Re-run both suites once the Aspire session is stopped before treating UIX-6 as fully verified.
2. **BDS-6 operator smoke** — the user confirmed smoke for the new dialogs; the BDS-6 activity/filters/
   paging checklist is not recorded as executed anywhere in the repository.
3. **Bulk retry scale** is unmeasured. No evidence exists for how many failed items a realistic import
   produces, so the batch bound is a design decision, not a measurement.
4. **Operation blocked-work retry** — the repository states it is unsupported but records no decision on
   whether it *should* be supported. JOB-1 must resolve this explicitly rather than assume.
5. **Resolution semantics** were deliberately deferred in SDK-3 without a specified model; "resolved"
   vs "ignored" vs "won't fix" needs product wording before implementation.
6. **Schedule delete absence** could be intentional (audit/retention) or an oversight; no ADR was found
   either way.
7. **SDK-7 readiness** — the shared-key evidence exists via BDS-2…BDS-6, but no doc states that BDS
   delivery satisfies SDK-7's original "two Samples with shared keys" precondition. Confirm before
   scheduling it as "[then]".
