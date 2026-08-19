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
UIX-5  Supporting surface and investigation pattern audit
UIX-6  Interaction and progressive disclosure system
```

| Epic | Status | Notes |
|------|--------|--------|
| **UIX-1** | Design audit (complete) | Layered navigation / drill-through intent; route-derived breadcrumbs as the normal spine. See BDS-1 §9 and related UI audits. |
| **UIX-2** | Implemented | Breadcrumb/shell renderer stabilization — [`../audits/uix2-breadcrumb-shell-stabilization.md`](../audits/uix2-breadcrumb-shell-stabilization.md). |
| **UIX-3** | Implemented | Affected-record anchors and typed return context — [`../audits/uix3-affected-record-anchors-return-context.md`](../audits/uix3-affected-record-anchors-return-context.md). |
| **UIX-4** | Implemented | Contextual breadcrumbs and link integrity — [`../audits/uix4-contextual-breadcrumbs-link-integrity.md`](../audits/uix4-contextual-breadcrumbs-link-integrity.md). |
| **UIX-5** | Audit complete | Supporting surface / investigation pattern audit — [`../audits/uix5-supporting-surface-pattern-audit.md`](../audits/uix5-supporting-surface-pattern-audit.md). **No shared panel yet**. |
| **UIX-6** | Implemented (operator smoke complete) | Interaction and progressive disclosure system — [`../audits/uix6-interaction-progressive-disclosure-system.md`](../audits/uix6-interaction-progressive-disclosure-system.md). `Disclosure`, `TechnicalDetails`, `ManagementDialog`, `ConfirmActionDialog`, interaction tokens; native `window.confirm` removed. Guide: [`../design-system/interaction-patterns.md`](../design-system/interaction-patterns.md). |

**UIX arc note (2026-08):** the inspection panel, drawer, popover, and accordion remain deferred
until the UIX-5 trigger criteria are met. **Supersession (2026-08-05):** UIX-6 and BDS-6 operator
smoke are complete; Track 6 closed via JOB-1 — see [`../audits/job1-failed-work-resolution-bulk-retry.md`](../audits/job1-failed-work-resolution-bulk-retry.md).
**Supersession (2026-08-18):** **AUP-1** delivered Track 2 — see [`../audits/aup1-account-profile-personal-preferences.md`](../audits/aup1-account-profile-personal-preferences.md).
Next product handoff: **UIX-7 — Visual Language and Theme Overhaul**, with the account area as its
flagship reference surface.

---

## Track 2 — User Profile & Preferences

**Status: complete (2026-08-18) — [`AUP-1`](../audits/aup1-account-profile-personal-preferences.md).**

| Item | Outcome |
|---|---|
| Profile editing | Display name is platform-owned and editable; identity-provider fields are read-only |
| Avatar / profile image | Four curated inline-SVG illustrations plus securely normalized photo upload (SkiaSharp, EXIF stripped, square PNG at 64/128/256) |
| Display timezone preference UI | Writes the existing `Users.DisplayTimeZoneId`; IANA-canonicalized searchable selector with browser detection. UTC remains execution truth |
| Notification preferences refinement | Per-notification email toggle enforced by `SqlNotificationRecipientResolver`; the in-app channel is stated as required because it has no per-user switch |
| Account / user settings consolidation | `/account/{profile,notifications,preferences,security}`, with `/account` and `/preferences` redirecting in |

Added beyond the original list: appearance preference persisted per account with documented precedence
(account → browser → platform default), and a 12/24-hour clock preference.

Deferred: Entra profile photo synchronization (would require new delegated Graph scopes), digest,
quiet hours, and non-email channels — none of which the delivery pipeline implements.

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

**PFB-1 recommendation (historical):** prioritize Track 3 next (see baseline audit §12).  
**Supersession (2026-08-05):** SDK-1 through SDK-6D are complete. Track 6 closed by JOB-1.  
**Supersession (2026-08-06):** **SDK-7** shared-record identity + lookup delivered — [`sdk7-cross-integration-shared-record-identity-lookup.md`](../audits/sdk7-cross-integration-shared-record-identity-lookup.md). Platform `BusinessRecord` remains deferred.  
~~**Next product epic:** **AUP-1 — Account, Profile, and Personal Preferences**.~~ — **delivered 2026-08-18**, see [`aup1-account-profile-personal-preferences.md`](../audits/aup1-account-profile-personal-preferences.md).  
**Next product epic:** **UIX-7 — Visual Language and Theme Overhaul**.

~~**Next epic:** **SDK-3A Operation Itemization Persistence & Writer**~~ — **superseded:** SDK-3A–3D, SDK-4A–4C, SDK-5*, SDK-6*, SDK-7 delivered; JOB-1 closed Track 6.

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
- **BDS-6** Cross-run business-record activity and lineage — complete (`docs/audits/bds6-cross-run-business-record-activity-lineage.md`); customer-first Demo Customer activity (not a generic Operation Item Explorer). Operator smoke complete (2026-08). Historical “Operation Item Explorer” wording in older notes refers to a deferred product idea, not the shipped BDS-6 surface.
- Integration-owned projections
- Cross-integration lookup
- Record detail pages
- Provenance / source tracking

**Track 5 sequencing note (2026-08):** BDS-1–BDS-6 and UIX-1–UIX-5 are delivered for the Demo Customer investigation arc. UIX-5 concluded **no shared inspection panel yet**.  
**Supersession (2026-08-05):** UIX-6 is implemented with operator smoke complete. Panel/drawer remain deferred.

---

## Track 6 — Job Operation Itemization & Retry

**Status: complete (2026-08-05)** via SDK-3/4 + JOB-1.

- Itemized run / job operations — delivered (SDK-3)
- Import row outcomes — delivered
- Failed item diagnostics — delivered
- Retry individual failed operations — delivered (SDK-4)
- Selective / bulk retry within one operation group — delivered (JOB-1)
- Acknowledge / reopen attention (resolve without rewriting outcomes) — delivered (JOB-1)
- Operation blocked-work final decision (inspection-only) — delivered (JOB-1)

**Closure:** [`../audits/job1-failed-work-resolution-bulk-retry.md`](../audits/job1-failed-work-resolution-bulk-retry.md)  
**ADR:** [`../02-decisions/adr-operation-blocked-work-inspection-only.md`](../02-decisions/adr-operation-blocked-work-inspection-only.md)

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
