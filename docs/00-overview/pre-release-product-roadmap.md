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

## Near-term execution order (post-CTX roadmap reset, 2026-09-18)

CTX-1 through CTX-3B (ResourceContext ownership, Applications, Shared, resource movement, archive integrity)
are complete, manually smoke-tested, and merged to `master` — see
[`ctx3b-closure.md`](../01-architecture/transitions/resource-contexts/ctx3b-closure.md). The tracks below
remain the broad thematic map; this section is the concrete near-term execution order agreed in the post-CTX
reset, cutting across several of them.

0. **Roadmap/documentation reset** — this reset (2026-09-18).
1. **Operator UX housekeeping** — four parts:
   - **1A** small polish: remove the View onboarding hint, Module-detail "Integrations using this Module"
     (via a reusable query, not throwaway plumbing), live create-form validity, confirmed-trivial debt
     cleanup.
   - **1B** Resource movement dialog UX: structured sections (Will move / Used by / Blocked by / History),
     target-select theming fix, Context display names instead of raw ids in operator-facing copy.
   - **1C** Context slug/human-readable routing, first stage (`?context=` URLs) — **complete (HSK-C,
     2026-09-20; operator smoke passed)**: new links emit the slug, id URLs stay valid and
     canonicalize to the slug, the API and localStorage keep the canonical id, no alias history. See
     [`ADR-0041`](../02-decisions/ADR-0041-human-readable-identity-policy.md#implementation-status).
     Not a system-wide id migration.
   - **1D** Application Resource Explorer (**HSK-D**) — **complete (2026-09-21); operator browser smoke
     passed for the exercised scenarios (restricted-operator authorization was not manually exercised and is
     covered by automated tests).** Carry-in from HSK-C smoke: Application detail showed only resource *counts*, with links to
     the scoped lists solely inside the archive-blocked warning. The always-visible Resources section that
     [CTX-3 design gate §6.4](../01-architecture/transitions/resource-contexts/ctx3-design-gate.md) intended
     never shipped; HSK-D **supersedes/completes that intent** (the historical gate is not rewritten).
     Delivered: an always-visible Resources section on Application detail (and, via the same explorer, on the
     Shared context's page) listing the Integrations,
     Connections and Schedules the Application currently **owns** (every state, archived Applications
     included), with one debounced search, a resource-type filter, human-readable names, direct links, each
     family's own state, and overview counts that link into the list. Shared resources merely *referenced* by
     Application-owned resources are not owned and are not listed. It reuses Config Inventory's normalized
     resource read model behind its own endpoint; it is not Config Inventory and does not embed it. State
     filtering is deferred (family lifecycles differ). Details, semantics and findings:
     [`hsk-d-application-resources.md`](../01-architecture/transitions/resource-contexts/hsk-d-application-resources.md).
     Also carried forward: many Resources are manifestations of, or are enabled/enhanced by, Components, but
     there is **no approved one-to-one Component→Resource invariant** and HSK-D introduced none (evidence
     recorded in the
     [Components/Services/Catalog planning note](../01-architecture/transitions/components-services-catalog/README.md)).
2. **Toast UX foundation** (**Toast-A**) — **implemented and merged (2026-09-21).** The required operator
   re-smoke on a rebuilt, restarted app passed functionally; the two visual issues it surfaced (a luminous
   shadow halo in dark theme, and the bottom-most toast's shadow clipping flat) were fixed in that same pass
   and are the branch's last commit. (The *first*, pre-rebuild real-app smoke had failed only because the
   running stack held pre-change compiled code alongside new live-served CSS/JS — not a defect in the
   branch's own markup/CSS/JS; cause, fix and guards are in the audit.) Presentation rewrite of the existing, already well-centralized toast system;
   **no call-site changes**. Delivered: a bottom-centred, responsive, upward-growing stack capped to a
   fraction of the viewport height that scrolls when it overflows; enter/exit motion with a timer-driven
   `Visible → Exiting → removed` lifecycle; countdowns that pause stack-wide while the operator interacts
   (hover, touch, focus, swipe, scroll, hidden tab) and resume with the remaining time; mouse/touch swipe
   dismissal with an accessible close button; single-shot screen-reader announcements; reduced-motion
   support; and a generous queue bound. Details, decisions and findings:
   [`toast-a-toast-ux-foundation.md`](../audits/toast-a-toast-ux-foundation.md); behaviour reference:
   [interaction patterns](../design-system/interaction-patterns.md#toast-presentation-and-interaction).
3. **Notification → Toast integration** (**Toast-B**) — **complete.** Incoming platform Notifications can now
   surface as Toasts, a presentation of the existing persistent in-app inbox rather than a new delivery
   channel: a per-user Toast preference per notification (independent of the always-on in-app inbox and the
   separately switchable Email preference), a Definition-level Toast default administrators set so a new
   notification doesn't default to silent, a session-scoped delta-polling watcher (30 s cadence) that turns
   newly-delivered, Toast-eligible inbox items into Toasts, de-duplication by inbox item identity (not message
   content), and severity mapping onto the existing Toast severities (Critical/Error get an explicit 10 s
   duration so they don't linger like ordinary feature-code error toasts; Warning/Info use the normal
   default). Details, phase-by-phase summary, known accepted gaps and manual smoke checklist:
   [`toast-b-notification-toast-integration.md`](../audits/toast-b-notification-toast-integration.md); decision
   record: [`ADR-0042`](../02-decisions/ADR-0042-notification-to-toast-channel-model.md); behaviour reference:
   [interaction patterns](../design-system/interaction-patterns.md#toast-presentation-and-interaction).
4. **Components design gate**
5. **Services design gate** — immediately follows the Components design gate, before either
   implementation begins. Services may reveal catalog/capability requirements Components needs to account
   for; the two gates run back-to-back so the Component catalog isn't shipped before the Services contract
   is decided.
6. **Components implementation**
7. **Services implementation**
8. **Application-centric navigation redesign** — after Components and Services stabilize, so the rail
   doesn't move twice.

Full rulings on the Components/Resource-Catalog/Services boundary:
[`components-services-catalog/README.md`](../01-architecture/transitions/components-services-catalog/README.md).

**Re-ranked below this sequence:** Connection/Schedule lifecycle cleanup (one combined item, scheduled
*after* the Services design gate — Services will clarify Connection's future operator role first);
Business Data final architecture (Track 5, gated — closed, no second entity type currently proposed);
Dashboard Panels (indefinitely parked — no chart library exists anywhere in the repo, no owner); Shared
Wizard foundation (Track 8, deferred, legitimate future value, not prioritized); Context-scoped IAM
(deferred until a concrete security/product need appears — not merely because ResourceContexts now exist).

**The visual workflow designer is not an active roadmap item.** It has only ever appeared as an
engineering-side non-goal (ADR-0032, Epic-57, CTX-3B closures) — never as approved product scope. It stays
out of this roadmap unless someone deliberately proposes it as real scope; the historical ADRs that name it
as something they did *not* build remain accurate as written.

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
~~Next product handoff: **UIX-7 — Visual Language and Theme Overhaul**, with the account area as its
flagship reference surface.~~ — **delivered** (`docs/audits/uix7-visual-language-theme-overhaul.md`);
superseded as "next" by the 2026-09-18 post-CTX execution order above.

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
~~**Next product epic:** **UIX-7 — Visual Language and Theme Overhaul**.~~ — **delivered**
(`docs/audits/uix7-visual-language-theme-overhaul.md`); superseded as "next" by the 2026-09-18 post-CTX
execution order above.

~~**Next epic:** **SDK-3A Operation Itemization Persistence & Writer**~~ — **superseded:** SDK-3A–3D, SDK-4A–4C, SDK-5*, SDK-6*, SDK-7 delivered; JOB-1 closed Track 6.

---

## Track 4 — Systems / Applications / Services Model

**Application ownership/view is delivered.** Reframed 2026-09-18 — this track originally bundled several
concepts that the post-CTX reset now separates:

- ~~Application / system abstraction~~ — **delivered**, via CTX-1 through CTX-3B: ResourceContext ownership,
  Shared + Applications, operator View, resource movement, archive integrity. See
  [`ctx3b-closure.md`](../01-architecture/transitions/resource-contexts/ctx3b-closure.md). Delivered as an
  ownership/organizational-view layer, not the generic "system" grouping abstraction ADR-0034 separately
  describes (see next line) — those are different shapes and CTX only delivered the former.
- **Systems** (ADR-0034's "logical grouping layer above Integrations") — still deferred, no forcing
  evidence, unaffected by CTX.
- **Service abstraction / Service explorers / Metadata browsing** — still not started. Reframed 2026-09-18:
  a Service is an operator-facing abstraction over a *configured Connection* (not a Systems-style grouping
  of many Integrations); the live-browse/metadata-exploration idea folds into this, not into Components. See
  the near-term execution order above and
  [`components-services-catalog/README.md`](../01-architecture/transitions/components-services-catalog/README.md)
  for the full hypothesis. Scheduled for its own design gate immediately after Components'.
- **Connection grouping** — not currently an independent planned deliverable; reconsider during the
  Services design gate if a concrete grouping requirement emerges.
- **Resource Catalog** (referenced by ADR-0040 as a separate future concept, never actually a bullet in this
  track) is **retired as a standalone roadmap concept** — its metadata/discoverability responsibility moves
  into Components (which may adapt and aggregate relevant operator-visible definitions behind a catalog
  abstraction; specialized registries such as permission descriptors, setting definitions, and connector
  definitions may remain specialized rather than being physically collapsed into one universal registry —
  no such collapse is approved), and its live-Connection-browsing responsibility moves into the Service
  abstraction above. See the planning note for the full split.

---

## Track 5 — Business Data & Cross-Integration Records

### Complete

- **BDS-1** Design audit — complete (`docs/audits/bds1-business-data-surface-design-audit.md`)
- **BDS-2** Business metadata alignment for sample modules — complete (`docs/audits/bds2-closure.md`)
- **BDS-3** Demo business record projection — complete (`docs/audits/bds3-demo-business-record-projection.md`)
- **BDS-4** Demo business record client & API polish — complete (`docs/audits/bds4-demo-business-record-client-api-polish.md`)
- **BDS-5** Demo Customers business record surface — complete (`docs/audits/bds5-demo-customers-surface.md`)
- **BDS-6** Cross-run business-record activity and lineage — complete (`docs/audits/bds6-cross-run-business-record-activity-lineage.md`); customer-first Demo Customer activity (not a generic Operation Item Explorer). Operator smoke complete (2026-08). Historical “Operation Item Explorer” wording in older notes refers to a deferred product idea, not the shipped BDS-6 surface.

### Still open — gated on a second entity type proving reuse

**2026-09-18: gate remains closed.** No second entity type has been proposed to trigger platform
promotion; these stay deferred, not prioritized.

- Shared entity / record model *(deferred — demo projection first)*
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

- Module-supplied UI patterns — closely related to the Components design gate (near-term order above),
  since Module is expected to become the install/distribution boundary subordinate to Components.
- Configuration / template foundations for integrations
- Progressive wizard frameworks beyond current progressive steps — the "shared platform Wizard subsystem"
  named in CTX-3B's closure debt. Deferred, legitimate future value, not currently prioritized.

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
