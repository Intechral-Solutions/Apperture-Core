# Pre-release product roadmap

**Purpose:** Major product tracks that remain **before** any Final Release Readiness phase.  
**Baseline:** Stabilization S1–S13 and SWM-1–SWM-12 are **closed**. See [`../audits/pfb1-product-foundation-baseline-audit.md`](../audits/pfb1-product-foundation-baseline-audit.md).

This is **not** a release-candidate plan. Completing a row does not make Apperture “v1 ready” until **Final Release Readiness** is intentionally run at the end.

---

## Stage summary

```text
[Foundation complete]
  Stabilization S1–S13
  SWM-1–SWM-12
        ↓
[Pre-release product development]  ← current planning horizon
  UI · Profile · Demos/SDK · Systems · Business data · Job ops · Azure · Module UI
        ↓
[Final Release Readiness]  ← deferred
  Packaging · smoke automation · security · docs · RC validation
```

---

## Track 1 — Product Experience & UI Overhaul

- Navigation / product shell
- Layout system
- Progressive disclosure
- Settings panels coherence
- Operator / admin / developer separation
- Final visual / UI pass (after major surfaces exist)

**Depends on:** Foundation shell (done). Benefits from demo modules for realistic content.

### UIX arc (navigation / drill-through)

```text
UIX-1  Layered navigation and drill-through design audit
UIX-2  Breadcrumb and shell rendering stabilization
UIX-3  Affected-record anchors and return context
UIX-4  Contextual breadcrumbs and link integrity
UIX-5  Supporting panel and overlay pattern audit
```

| Epic | Status | Notes |
|------|--------|--------|
| **UIX-1** | Design audit (complete) | Layered navigation / drill-through intent; route-derived breadcrumbs as the normal spine. See BDS-1 §9 and related UI audits. |
| **UIX-2** | Implemented | Breadcrumb/shell renderer stabilization — [`../audits/uix2-breadcrumb-shell-stabilization.md`](../audits/uix2-breadcrumb-shell-stabilization.md). |
| **UIX-3** | Planned | Affected-record anchors and return context (URL params). |
| **UIX-4** | Planned | Contextual breadcrumbs and link integrity. |
| **UIX-5** | Planned | Supporting panel / overlay pattern audit. |

---

## Track 2 — User Profile & Preferences

- Profile editing
- Avatar / profile image
- Display timezone preference UI (runtime TZ semantics already exist in SWM)
- Notification preferences refinement
- Account / user settings consolidation

---

## Track 3 — SDK & Demo Module Validation

- Additional demo modules beyond HelloWorld
- Import-style demo
- File-processing demo
- API polling demo
- Event / notification demo
- Schedule-driven demo
- Cross-integration demo
- SDK contract validation against durable workflow + settings + scheduling interfaces

**PFB-1 recommendation:** prioritize this track next (see baseline audit §12).

**SDK-1 (closed):** strategy + capability map — [`docs/audits/sdk1-demo-module-strategy-and-sdk-contract-audit.md`](../audits/sdk1-demo-module-strategy-and-sdk-contract-audit.md).  
**Next epic:** **SDK-3A Operation Itemization Persistence & Writer** (then 3B read APIs → 3C FileImport adapter → 3D run-detail panel → SDK-4 per-item retry). See [`sdk3-itemized-operation-model-design-audit.md`](../audits/sdk3-itemized-operation-model-design-audit.md).

---

## Track 4 — Systems / Applications / Services Model

- Application / system abstraction
- Service abstraction
- Integration relationships to systems / services
- Service explorers
- Metadata browsing
- Connection grouping

---

## Track 5 — Business Data & Cross-Integration Records

- Shared entity / record model *(deferred — demo projection first)*
- **BDS-1** Design audit — complete (`docs/audits/bds1-business-data-surface-design-audit.md`)
- **BDS-2** Business metadata alignment for sample modules — complete (`docs/audits/bds2-closure.md`)
- **BDS-3** Demo business record projection — complete (`docs/audits/bds3-demo-business-record-projection.md`)
- **BDS-4** Demo business record client & API polish — complete (`docs/audits/bds4-demo-business-record-client-api-polish.md`)
- **BDS-5** Demo Customers business record surface — complete (`docs/audits/bds5-demo-customers-surface.md`)
- **BDS-6 (optional)** Operation Item Explorer / finer Affected-record anchors *(next)*
- Integration-owned projections
- Cross-integration lookup
- Record detail pages
- Provenance / source tracking

---

## Track 6 — Job Operation Itemization & Retry

- Itemized run / job operations
- Import row outcomes
- Failed item diagnostics
- Retry individual failed operations
- Skip / ignore / resolve flows

**Note:** Strongly informed by import / file demos from Track 3.

---

## Track 7 — Azure / Deployment Foundation

- Azure hosting model
- Deployment pipeline
- SQL / secrets / logging
- Worker hosting on Azure
- Production runbooks

Do **not** start this track as a substitute for product completeness. On-prem/dev Aspire + Manage.ps1 remains the foundation path.

---

## Track 8 — Module UI / configuration / template foundations

- Module-supplied UI patterns
- Configuration / template foundations for integrations
- Progressive wizard frameworks beyond current progressive steps

(May interleave with Tracks 1 and 3.)

---

## Track 9 — Final Release Readiness (last)

Only after major product tracks above are materially complete:

- Packaging / installers
- Smoke automation
- Security review
- Docs completion
- Release candidate validation

**Reserve the phrases “release readiness,” “release candidate,” and “v1 ready” for this track.**

---

## Explicitly out of immediate scope

| Item | Why |
| ---- | --- |
| Management-owned WorkerHost start/stop | SWM-9/12 deferred; externally managed |
| Claiming Windows Service / Azure as shipped | Scaffold / future tracks only |
| Reopening S1–S13 or SWM-1–12 | Closed unless regression |
| Premature RC tagging | Product work incomplete |

---

## Related docs

| Doc | Role |
| --- | ---- |
| [`roadmap.md`](roadmap.md) | Stage overview |
| [`pfb1-product-foundation-baseline-audit.md`](../audits/pfb1-product-foundation-baseline-audit.md) | Foundation baseline + next-track recommendation |
| [`stabilization-closure-check.md`](../audits/stabilization-closure-check.md) | S1–S13 closed |
| SWM ADRs under `docs/02-decisions/` | Scheduling & worker foundation closed |
| [`Worker-Hosting-Guidance-v1.md`](../03-operations/Worker-Hosting-Guidance-v1.md) | External worker hosting |
