# Pre-release product roadmap

**Purpose:** Major product tracks that remain **before** any Final Release Readiness phase.  
**Baseline:** Stabilization S1–S13 and SWM-1–SWM-12 are **closed**. See [`../audits/pfb1-product-foundation-baseline-audit.md`](../audits/pfb1-product-foundation-baseline-audit.md).

**Supersession (2026-10-03, RC1 program gate).** The sentence that this document "is not a release-candidate plan"
described its state before the post-IAM-8 reorientation. The **RC1 program** section below is now the current plan. Its
contract is [`../01-architecture/rc1-program-gate.md`](../01-architecture/rc1-program-gate.md). The older sections are
kept as the thematic map and as provenance; where they conflict with the RC1 program section, that section governs.
Completing a row in the thematic tracks still does not make Apperture release-ready; RC1 is reached only through RCV-1.

---

## Stage summary

```text
[Foundation complete]
  Stabilization S1–S13
  SWM-1–SWM-12
        ↓
[Pre-release product development complete through IAM-8]
  UI · Profile · Demos/SDK · Contexts · Components · Services · SEC-1 · LFC-1 · IAM-8
        ↓
[RC1 program]  ← current (gate approved 2026-10-03)
  Identity · Audit · Authorization · Navigation · Actions · Lists · Module runtime · Operability
        ↓
[RC1 validation (RCV-1)]
  Smoke automation · security review · docs · tag
        ↓
[Post-RC1 design gates and later product expansion]
```

---

## RC1 program (current, 2026-10-03)

**Contract:** [`rc1-program-gate.md`](../01-architecture/rc1-program-gate.md). It holds the RC1 definition, every
Decision, and what remains a Direction, a Candidate or Deferred. **Evidence:**
[`rc1-architecture-audit-and-program-plan.md`](../audits/rc1-architecture-audit-and-program-plan.md) and
[`rc1-supplemental-reconciliation-addendum.md`](../audits/rc1-supplemental-reconciliation-addendum.md) (working records;
the gate governs where they differ).

### Current / next

**RC-0 (program gate) is complete.** **IAM-9.0** (identity foundation design gate), **LST-1.0** (list and query
design gate) and **QW-1** (quick wins, implemented) are merged to `master` (PR #4).

**IAM-9** (identity foundation) is implemented: packages 9.1 to 9.5 are merged to `master` (PRs #5 to #10, last
merge `2b5bb52`). The [closure audit](../audits/iam9-identity-foundation-closure.md) finds IAM-9 **not ready to close**:
one approved contract row (the assigned username on Entra provisioning results) is unmet, and a bounded fix is
proposed. After that fix, closure awaits owner acceptance. The findings IAM-9 carries to IAM-10, AUD-1 and IAM-11 stay
open. In particular, the `role.manage` escalation path blocks RC1 requirement 4 until IAM-10 closes it.

The next Track A body is **AUD-1**, beginning with its design gate. LST-1.1, NAV-2 and SDK-8 have not started. Each
waits for an explicit start instruction.

IAM-8 is complete and **merged to `master`** (PR #3, `5cfa901`). Earlier statements in this document that IAM-8 is
"not merged" are superseded.

### What RC1 means

A real release-candidate milestone: feature-complete and upgrade-safe on one supported topology (Windows Server, SQL
Server, three hosts as Windows Services), with a security-reviewed identity and audit core, a coherent operator
experience, and a module SDK labelled preview. Azure and Docker or Linux production hosting are not RC1-supported
targets; they remain architectural goals. Full definition: gate section 2.

### Near-term core platform (Track A, security)

| Order | Epic | Outcome |
| ----- | ---- | ------- |
| A1 | **IAM-9** Identity foundation | Username for every user; user id as session and audit identity; one user reference; user search |
| A2 | **AUD-1** Audit hardening | Stable actor, target, category and severity, structured details; coverage of administrative and operator actions; noise separation |
| A3 | **IAM-10** Authorization hardening | Escalation paths closed; human-readable permission definitions; `operational_team.manage`; scope-composition design settled |
| A4 | **IAM-11** User lifecycle and credential recovery | Disable; decommission (tombstone); administrator-initiated reset; return to local access; sign-in throttling |

### UI and platform consistency (Track B, experience)

| Order | Epic | Outcome |
| ----- | ---- | ------- |
| B1 | **QW-1** Quick wins | Drawer toggle glyph; browser titles; chronological Runs; Notification Diagnostics clean-up |
| B2 | **NAV-2** Navigation registry and shell | One registry; Services domain; Runs and Schedules under Integrations; icons; rail behaviour; tree primitive; typed locators. URLs do not move. |
| B3 | **ACT-1** Entity actions | One toolbar model; pending and double-submit behaviour; content stays visible on refresh |
| B4 | **LST-1** Lists and query | Paging contracts; query lifecycle; list state and pager; selector; correctness fixes; team membership. Packages 1.0, 1.1, 1.2 and 1.6 run alongside NAV-2. |

### Platform and operability (Track C)

| Order | Epic | Outcome |
| ----- | ---- | ------- |
| C1 | **SDK-8** Module runtime correctness | Missing-handler failure; step timeout and lease renewal; registration validation; preview statement |
| C2 | **ID-1** Identifier hygiene | Ordered ids for new high-volume rows; no migration project |
| C3 | **OPS-1** Operability baseline | Windows Service hosting; health; explicit migration; backup and restore; first-admin hardening; release engineering |

### Pre-release and UAT

**RCV-1 Release-candidate validation** follows all three tracks: honest SQL tests, one real end-to-end run test,
functional browser smoke, accessibility baseline, upgrade test, the outstanding SEC-1 and IAM operator smoke runbooks,
a whole-product security review, and documentation regeneration. This is what Track 9 below now means.

### Data, query and search (post-RC1)

| Body | Kind | Depends on |
| ---- | ---- | ---------- |
| **LST-2** List presentation layer (columns, sortable headers, filter controls, row action, virtualization) | Design gate | LST-1 and ACT-1 shipped |
| **SRCH-1** Platform search | Design gate. **Direction only:** begin by evaluating federation over existing authorized reads; a projection for Business Data only once a record model exists. SQL full-text, a search-index abstraction and an external search engine are not selected. | POL-1, AUD-1 |

### Shared platform services (post-RC1)

| Body | Kind | Depends on |
| ---- | ---- | ---------- |
| **NAV-3** Navigation provenance (drill-through breadcrumbs) | Design before RC1; implementation after | NAV-2 |
| **POL-1** Policies and data authority, including platform notes | Design gate | IAM-10.0 |
| **MUI-1** Module UI panels and wizards | Design gate | SDK-8.0 |
| **MOD-1** Module installation and coordinated restart | Security design gate | SDK-8, OPS-1 |
| **CLI-2** Operator CLI and environment trust | Security design gate | A non-interactive API credential |

### Deferred architectural directions

| Item | Status | Trigger |
| ---- | ------ | ------- |
| Workspaces | **Direction; implementation deferred** | The preferred long-term direction for cross-context operational experiences ([RC1 gate](../01-architecture/rc1-program-gate.md) 4.1 and 4.3). Presentation, not a grant; ResourceContext stays ownership and view, not authorization. Name unsettled. Needs its own design gate and a product trigger. NAV-2 changes the rail now; a later Workspace design builds on the navigation registry and adds no second rail rewrite. |
| Cross-context organizational grouping (item 8 below) | **No longer an independently planned body** | The Workspace direction supersedes item 8 as the planned product approach to cross-context operational experiences. Grouping is kept only as a possible primitive the Workspace design may need, with its own design gate if so. |
| Context-scoped IAM (implementation) | **Deferred, retained** | Application-confined guest access. The design scope is settled in IAM-10.0. No context scope constant is added to code in RC1. |
| **PLK-1** Protected lookup | **Deferred, dormant** | A real customer requirement. Separate from search; needs a dedicated security gate. |
| **CAL-1** Platform calendar | **Deferred Candidate** | A concrete first consumer. Not ScheduleDefinitions. No RC1 seam. |
| Federation | **Withdrawn** | [ADR-0046](../02-decisions/ADR-0046-external-organization-access-and-federation-withdrawal.md) |
| Additional Entra tenants | Deferred | An external organization that needs its own tenant |
| Personal-data erasure | Deferred | A regulatory or privacy requirement |

### Later product expansion

Metrics, dashboards, Workspaces (a Direction; see Deferred architectural directions; name unsettled), a richer Service abstraction, a script runtime, Business Rules, a
workflow designer, a module authoring tool and AI capabilities. None has a design. Seams RC1 preserves for them are
listed in the audit's section Q and the addendum's section M.

---

## Near-term execution order (post-CTX roadmap reset, 2026-09-18)

CTX-1 through CTX-3B (ResourceContext ownership, Applications, Shared, resource movement, archive integrity)
are complete, manually smoke-tested, and merged to `master` — see
[`ctx3b-closure.md`](../01-architecture/transitions/resource-contexts/ctx3b-closure.md). The tracks below
remain the broad thematic map; this section is the concrete near-term execution order agreed in the post-CTX
reset, cutting across several of them.

**Supersession (2026-10-03):** this section is the historical 2026-09-18 sequence. Every item in it except item 8 is
complete. The Workspace direction supersedes item 8 as the planned product approach to cross-context operational
experiences. The current plan is the **RC1 program** section above. IAM-8 is merged to `master`.

**Current next (as written 2026-09-30, superseded above):** none selected. **IAM-8 — Custom Role Lifecycle** (item 7b) is **complete and
merge-ready**; not merged. This closeout does not select a next epic. LFC-1 is complete and merged to
`master`. SEC-1 engineering is merged to `master`. Items 0–7a of the 2026-09-18 sequence are complete as
engineering. **Navigation / cross-context grouping (item 8) is not selected.** Gate:
[`iam8-design-gate.md`](../01-architecture/transitions/custom-role-lifecycle/iam8-design-gate.md).
LFC-1.0–LFC-1.3 closed against
[`lfc1-design-gate.md`](../01-architecture/transitions/connection-schedule-lifecycle/lfc1-design-gate.md).

0. **Roadmap/documentation reset** — this reset (2026-09-18).
1. **Operator UX housekeeping** — four parts:
   - **1A** small polish (**HSK-A**) — **complete in current code (no separate operator-smoke closure;
     unlike 1C/1D this row is reconciled from repository evidence, not a new smoke claim):** View onboarding
     hint removed (the one-time selector hint is gone from `src/`; the CTX-3 design gate records
     [HSK-A removed it](../01-architecture/transitions/resource-contexts/ctx3-design-gate.md)); Module detail
     shows "Integrations using this module" via a reusable query
     (`ModuleDetail.razor`); live create-form validity for connector-required fields
     (`ConnectorConnectionFieldValidation`). Confirmed-trivial debt cleanup was in the same body.
   - **1B** Resource movement dialog UX (**HSK-B**) — **complete in current code (no separate operator-smoke
     closure; reconciled from repository evidence, not a new smoke claim):** structured sections (Will move /
     Used by / Blocked by / History) on `MoveOwnershipDialog.razor` / `MoveOwnershipPresenter`; Context
     display names instead of raw ids ([ADR-0041](../02-decisions/ADR-0041-human-readable-identity-policy.md#implementation-status)
     records that HSK-B moved the movement dialog to display names).
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
4. **Components design gate** — **complete and approved (2026-09-22).** Components is a **read-time
   projection**, not a persisted entity: no `Components` table, no universal registry. v1 covers exactly two
   families — Connector definitions, and Notification Definitions whose `Origin` is `PlatformBuiltIn` (System)
   or `ModuleBuiltIn` (Module); user-created definitions are operator content and are **not** catalog entries.
   Identity is `(Kind, NativeId)` routed as `/components/{kind}/{nativeId}`, reusing each family's existing
   readable native id — no new identifier, no slug subsystem, ADR-0041-compliant as-is. **No new permission**:
   the per-family read gates are reused and a family the operator cannot read is omitted, never denied.
   Component detail is a uniform wrapper that delegates every mutation to the authoritative specialized
   surface. Navigation lands at `Config → System → Components → { Catalog, Modules }` — **no new rail
   domain**. Product rulings D1–D4 are recorded as decided. Full gate, test matrix, implementation
   decomposition (CMP-1…CMP-6, plus the separate CMP-X) and the Services handoff:
   [`components-design-gate.md`](../01-architecture/transitions/components-services-catalog/components-design-gate.md).
   **Implemented** — see item 6.
5. **Services design gate** — **complete and approved (2026-09-23).** A **Service is the evolved operator
   workspace for one Connection — not an object**: no Service table, no Service GUID, no independent lifecycle,
   no `Service.ResourceContextId`. Identity is `ConnectionId`; cardinality is 1:1 by construction; effective
   Context always derives from `Connection.ResourceContextId`, so moving a Connection implicitly moves the
   experience. The workspace **is** `/connections/{id}` and the list **is** `/connections` — **no `/services`
   route and no `/services` list**. Integration Bindings remain Integration → Connection, and HSK-D continues
   listing one Connection Resource rather than a second Service row. The Connector relationship stays derived
   from `ConnectorDefinitionId` with no persisted edge. Runtime clients remain connector implementation details;
   `ClientDefinition` is **not** resurrected. **v1 scope is S-B**: the Connection-workspace foundation, the
   provider/tool seam, and a SQL Server **structure**-browser exemplar (platform-authored closed metadata
   operations, no user-authored SQL, no row/content preview, no DML/DDL, no remote mutation) — **an arbitrary
   SQL/query console is explicitly future work behind its own security/design gate, not a later tab**.
   Authorization adds exactly one action, **`connection.browse`**, additive to `connection.read`. The
   pre-existing Connection audit gap is closed as bounded Services work; a Connection Recent Activity panel is
   **not** in v1. "Service" stays the architectural/roadmap term and does **not** become an operator-facing noun.
   Product rulings D-S1–D-S4 are recorded as decided. The gate required **no architectural amendment to
   Components**. Full gate, test matrix and decomposition (SVC-1…SVC-8):
   [`services-design-gate.md`](../01-architecture/transitions/components-services-catalog/services-design-gate.md).
   **Implemented** — see item 7.
6. **Components implementation** — **complete (2026-09-23).** CMP-1…CMP-6 delivered as designed, with CMP-X
   (built-in Notification Definition platform defaults) complete alongside. Components is a read-time projection
   with **no** table, registry, `ComponentId` or new permission. Shipped: the authorized catalog API
   (`GET /api/components`, `GET /api/components/{kind}/{nativeId}`, read-only), the `/components` catalog page and
   the `Config → System → Components → { Catalog, Modules }` menu (no rail domain; the structural parent is
   ungated so it can never hide an authorized child), a delegating detail wrapper, detail-only current usage
   (Active Connections per connector; current subscriptions per definition), "Components provided by this
   module" on Module detail, and Connector-tab rows linked to Component detail. The `/connections` Connectors tab
   was **retained** at that point and **retired by Services (SVC-4)**. Decision record:
   [`ADR-0043`](../02-decisions/ADR-0043-component-catalog-read-model.md); closure and evidence:
   [`components-catalog-closure.md`](../audits/components-catalog-closure.md).
7. **Services implementation** — **complete (2026-09-24).** SVC-1…SVC-8 delivered as designed, with **no Service entity, route,
   permission or type**: the existing `/connections/{id}` page became the operational workspace (connector identity linked to its
   Component; connector-declared configuration with secrets as *Set/Not set*; health; used-in; TechnicalDetails; correct
   retired/unregistered/failed states). The pre-existing Connection **audit gap is closed** (create, update, secret rotation, retire,
   test-connect). The duplicate **Connectors tab is retired** in favour of the Components catalog, which now opens filtered from
   `/components?kind=connector`. One new action, **`connection.browse`** (additive to `connection.read`), gates a minimal typed tool
   seam and one exemplar — a SQL Server **structure** browser (closed, fixed, parameterized `INFORMATION_SCHEMA` statements; no
   dynamic SQL, no rows, no query console, no mutation; bounded, cancellable, audited fail-closed). Connection Recent Activity is
   **not** delivered (audit query model). Decision record: [`ADR-0044`](../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md);
   closure and evidence:
   [`services-connection-workspace-closure.md`](../audits/services-connection-workspace-closure.md). An arbitrary SQL/query console,
   row preview, SFTP/Campus Nexus tools and capability advertisement remain future work, each behind its own gate.
7a. **SEC-1 — Browser Session & Request-Forgery Hardening** — **design gate complete and approved (2026-09-24);
   engineering closure complete (SEC-1C, 2026-09-29); independent final review passed with only non-blocking findings;
   engineering merged to `master`.** Remaining environment-dependent operator smoke (real Entra, Safari, and the
   closure checklist) is **pre-release / UAT validation**, not active engineering, and is **not** claimed passed.
   Decision record: [`ADR-0045`](../02-decisions/ADR-0045-browser-session-recovery-and-request-trust.md);
   closure and evidence: [`sec1-browser-session-security-closure.md`](../audits/sec1-browser-session-security-closure.md).
   Rulings D1–D9, the approved architecture and the SEC-1.0…SEC-1C implementation slices
   are in
   [`sec1-browser-session-request-forgery-design-gate.md`](../01-architecture/transitions/security/sec1-browser-session-request-forgery-design-gate.md).
   **SEC-1.0 (the opt-in Playwright browser proof harness and the baseline reproduction of the browser findings) is
   delivered** (originally on branch `sec-1-browser-security`; now on `master`) with its evidence recorded in the gate (§0.1). **SEC-1A1 (centralized
   expired-session recovery) is implemented on that branch (gate §39), and **SEC-1A2a (the shared safe return-target policy,
   the normal sign-in and failed/cancelled Entra sign-in flow) is implemented on it (gate §40), and **SEC-1A2b (Entra step-up
   continuation across the cross-site OIDC callback) is implemented on it (gate §41; real Entra and Safari remain manual checks),
   SEC-1A3 (background requests do not renew the sliding session) is implemented on it (gate §43), and SEC-1B1 (the platform
   browser request-forgery policy) is implemented on it (gate §44), and SEC-1B2 (sign-out as a trusted POST, Rule N for the
   step-up and link navigations, the Entra directory searches as POST) is implemented on it (gate §45), and SEC-1B3 (Development-only
   CORS, Production `ManagementUiUrl` refusal, Secure cookies, Management.Api class library, Ui.Server debug cleanup) is implemented
   on it (gate §46), and SEC-1B4 (the blocking `browser-security` CI job for I-2) is implemented on it (gate §47).
   The first Ubuntu `browser-security` run failed at Management.Host startup on the Windows-only DPAPI secret default
   (gate §47.6). The second Ubuntu `browser-security` run passed. The same workflow's `build-and-test` job failed:
   IntegrationTests' Management Host still started without a non-Windows protector, so all 205 host cases failed and
   the other 77 passed (gate §47.7). The third `build-and-test` started Management Host; the remaining failures were
   Entra-linking local-user setup returning 403 because the test client authenticated as the first local username
   rather than a platform administrator (gate §47.8). The fourth `build-and-test` threw because that catalog had
   ordinary users and no platform administrator, so first-admin bootstrap was already closed (gate §47.9).
   The fifth `build-and-test` reached the SQL structure-browser unreachable proof, which returned Failed
   instead of RemoteUnavailable on Ubuntu (gate §47.10). The sixth `build-and-test` reached six
   toast-delta failures where a partial composite cursor hit `SqlNotificationUserInboxStore`
   (gate §47.11). The seventh `build-and-test` reports six IntegrationTests failures that Copilot
   attributed to an SA password mismatch; the checked-in workflow is not truncated, 283 tests
   connected to SQL, and the six failures are governance Launch/Start 403s plus the unreachable
   SQL outcome (gate §47.12). The thirteen Ui.Shared link/breadcrumb failures in that same job are a Unix
   file-URI parse of rooted app paths (gate §47.13). The eighth `build-and-test` proved that URI fix
   (Ui.Shared 1395/1395) and passed `browser-security`. The same six IntegrationTests failures remained.
   A fresh governance catalog reproduces them: HelloWorld is enabled after the permission catalog snapshot,
   so `workflow.execute` is unknown and launch is `workflow_start_forbidden`. The Ubuntu closed-port SQL
   shape is Number 0 with `SocketError.Success` and is not classified as unreachable (gate §47.14).
   The ninth `build-and-test` passed. `browser-security` failed one Account Security recovery fact
   because the trace counted that page's own `auth/me` refresh together with the one coordinator probe
   (gate §47.15). The assertion is still a single probe. The final pull-request workflow then passed both
   `build-and-test` and `browser-security` on Ubuntu, so **SEC-1B4 is complete** and I-2 is closed (gate §47.16).
   The `master` ruleset is configured to require `build-and-test` and `browser-security`. GitHub does not
   currently enforce rulesets for this private organization repository under the current plan, so both
   checks are procedurally required before merge until automatic enforcement becomes available.
   SEC-1C (ADR-0045 and the closure audit) is complete (gate §48): every binding requirement is closed, residuals and
   deferrals are recorded, and the operator smoke checklist is prepared but not executed. N1 (`context=all` in captured
   return targets) is a closed design clarification, not a security finding: it is benign on-origin application state
   and is preserved. **Engineering is merged.** Remaining operator smoke is pre-release/UAT and is not claimed passed.**
   Sequenced after Services (item 7) in the 2026-09-18 order, numbered
   7a so existing references to item 8 stay valid. IAM-8 (7b) and grouping (8) were written next after SEC-1.
   Grouping remains not started. IAM-8 is now the selected design-gated body (see **Current next**). It is one architecture/security body with two related concerns, both
   surfaced during Services operator smoke and deliberately **kept out of the Services branch**. Its design gate must
   **audit the existing Entra/local sign-in and return-URL behaviour first and reuse/finish that infrastructure where
   possible** before designing anything new. The gate, not this entry, chooses the mechanisms.
   - **SEC-1A — session expiry / authentication recovery.** Observed: when an authenticated browser session expires,
     refreshing or continuing to use some Management UI pages does not always start authentication; the SPA can stay on the
     requested page and surface an Unauthorized error. The gate must audit and make consistent: expired-session behaviour;
     hard-refresh behaviour with an expired session; 401 handling for Management API calls; how authentication redirects are
     initiated; return to the original page after successful sign-in; preservation of the local path and query (and
     fragment state where appropriate); safe, local-only return URLs with no open redirect; the **401 vs 403** distinction (a
     401 may trigger authentication recovery; a **403 is an authorization result and must not redirect to sign-in**);
     simultaneous 401s producing a single authentication transition; background polling (for example Notification/Toast
     polling) not causing redirect storms; sign-in/authentication routes never entering redirect loops; and refreshing
     authorization/capability state after re-authentication.
   - **SEC-1B — platform request-forgery protection.** The Services security pass established that the repository has **no
     general antiforgery middleware, token or Origin validation**. The SQL structure-browse endpoints are POST with a required
     JSON body under a `SameSite=Lax` session cookie ([ADR-0044](../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md));
     that is a narrowly sufficient measure for those endpoints and **must not become an endpoint-by-endpoint security
     pattern**. The gate must audit cookie-authenticated mutation and side-effect endpoints and select a platform-wide
     approach, reviewing: `ManagementClient` integration; Blazor hosting behaviour; Origin / `Sec-Fetch-*` checks and conventional
     antiforgery-token options; same-origin and sibling-origin threat boundaries; compatibility with Entra and local session
     authentication; which APIs should and should not require protection; and the existing **`UiDev` CORS policy** — in particular
     whether credentialed `localhost` origin access can ever be active outside the intended development environment.
   Neither concern is implemented on the Services branch, and neither is a prerequisite for merging it.
7b. **IAM-8 — Custom Role Lifecycle** — **selected next core-platform body (2026-09-29).** IAM-8.0 design
   gate is written; implementation has not started. The 2026-09-18 sequence placed this after SEC-1 and before
   navigation / cross-context grouping; grouping is **not** selected by this choice. Bounded scope:
   create user-defined roles; edit their permission grants; delete user-created roles; built-in/system roles are protected
   from edit and deletion; show a role's assignments/usage before deletion; refuse a deletion that would leave active
   assignments invalid or orphaned; audit the create/update/delete lifecycle. It is **not** Context-scoped IAM (that stays
   deferred, below) and does not change the authorization evaluation model. Contract:
   [`iam8-design-gate.md`](../01-architecture/transitions/custom-role-lifecycle/iam8-design-gate.md).
   **Closure (2026-09-30):** IAM-8.0–IAM-8.4 satisfy that gate. IAM-8 is complete and merge-ready. This closeout
   does not select a next epic. Grouping remains not started.
8. **Navigation / cross-context grouping design** — **superseded (2026-10-03); not a planned body.**
   **Supersession (2026-10-03):** the Workspace direction supersedes item 8 as the planned product approach to
   cross-context operational experiences ([RC1 gate](../01-architecture/rc1-program-gate.md) 4.1). Cross-context
   grouping is kept only as a possible underlying primitive that the Workspace design may discover it needs, with its own
   design gate if so. Departments, responsibilities and operational areas are expected to be addressed through
   Workspaces, not through a new ResourceContext Kind. The text below is the 2026-09-22 direction, kept as provenance.
   Its ownership and authorization constraints remain binding on any later design: not ResourceContext ownership, not
   authorization, not resource identity; `/connections` stays stable; `connection.browse` is never derived from
   grouping or Workspace membership.
   Distinct from **NAV-1**, the
   completed Management UI **rail** overhaul (`docs/01-architecture/transitions/nav-overhaul/`; Control Center /
   Integrations / Business Data / Config). Item 8 is a later, undesigned **cross-context organizational grouping**
   layer — not a second rail rewrite and not NAV-1 leftover work. The 2026-09-18 sequence placed it after Components
   and Services (and after SEC-1, item 7a, and IAM-8, item 7b) so the rail would not move twice; that remains the
   constraint if/when this body runs. **Direction updated 2026-09-22:** this is *not* a plan to replace the ResourceContext/
   View selector with an Application-centric one. The existing ResourceContext/View dropdown is **retained
   broadly as-is**, and ResourceContext remains the ownership/scoping boundary for Applications and Shared.
   This gate will instead explore a **separate cross-context organizational grouping layer** — departments,
   responsibilities, operational areas, areas of concern, or other user-defined groupings — which may contain
   or surface resources spanning **multiple** ResourceContexts, and which may later support cross-context
   operational experiences, workflow capabilities over the relevant resources, and platform-generated reports
   organized by those groupings. **Architectural constraint:** the grouping layer must not be conflated with
   ResourceContext ownership, authorization, or resource identity. ResourceContext answers *"who/what owns
   this resource, and what Application/Shared scope is it in?"*; the grouping layer would answer *"what
   organizational area, responsibility, department, or concern should surface this resource?"* The model,
   membership semantics and the **product term are all undecided** — "domain" is a working label only, and
   deliberately not a candidate, since it already carries technical meanings in this codebase (shell nav
   domains, DDD domain layer). Nothing about this is designed or scheduled yet; it is recorded here only so
   future sessions do not assume the older "Application-centric navigation redesign" framing.
   **Remains after Components and Services implementation** (items 6 and 7), unchanged by the Services gate.
   Constraints it inherits from that gate: `/connections` and `/connections/{id}` are stable identity and must
   not be renamed or made Context-specific; `DomainMenuCapabilityTokens.ViewConnections` already exists and is
   already resolved, so attaching a Connections leaf later needs no new permission; Services stores no
   grouping/domain membership; and `connection.browse` authorization derives from Connection authorization,
   never from future grouping membership. The gate also records a standing finding for this one to own:
   **`/connections` is currently reachable from no rail item at all** (ADR-0040 rule 3 keeps it off-rail).

Full rulings on the Components/Resource-Catalog/Services boundary:
[`components-services-catalog/README.md`](../01-architecture/transitions/components-services-catalog/README.md).
**Both design gates supersede that planning note wherever they differ** (they are grounded in code audits; the
note is pre-audit):
[`components-design-gate.md`](../01-architecture/transitions/components-services-catalog/components-design-gate.md)
and [`services-design-gate.md`](../01-architecture/transitions/components-services-catalog/services-design-gate.md).

**Re-ranked below this sequence (2026-09-18):** Connection/Schedule lifecycle cleanup (one combined item; the Services
design gate has now clarified Connection's future operator role — Connection detail becomes the operator workspace, and
`ConnectionState` remains Active/Retired with no un-retire);
Business Data final architecture (Track 5, gated — closed, no second entity type currently proposed);
Dashboard Panels (indefinitely parked — no chart library exists anywhere in the repo, no owner); Shared
Wizard foundation (Track 8, deferred, legitimate future value, not prioritized); Context-scoped IAM
(deferred until a concrete security/product need appears — not merely because ResourceContexts now exist).

**Supersession (2026-09-29):** of those re-ranked items, **LFC-1 — Connection/Schedule lifecycle cleanup** was the
**selected next core-platform body**. That choice does **not** rewrite the 2026-09-18 ranking as if LFC-1 had
already been first; it is a later, explicit prioritization of unfinished core platform functionality over IAM-8
(item 7b) and future navigation/grouping (item 8), both of which remain not started. Delete and un-retire
rulings are recorded in the [LFC-1.0 design gate](../01-architecture/transitions/connection-schedule-lifecycle/lfc1-design-gate.md),
not in this roadmap. LFC-1.1, LFC-1.2, and LFC-1.3 are implemented. **Supersession (2026-09-29, LFC-1
closure):** LFC-1 is complete and merge-ready; this closeout does not select IAM-8 or grouping. The other
re-ranked items (Track 5 remainder, Dashboard Panels, Shared Wizard, Context-scoped IAM) stay deferred/parked
as written.

**Supersession (post-LFC-1 merge):** LFC-1 engineering is on `master`. That closeout did not select a next epic.

**Supersession (2026-09-29, IAM-8 selection):** **IAM-8** is the selected next core-platform body. Only the
design gate is written. Navigation / cross-context grouping (item 8) stays unselected.

**Supersession (2026-09-30, IAM-8 closure):** IAM-8 is complete and merge-ready. This closeout does not select
a next epic. Navigation / cross-context grouping (item 8) stays unselected. Context-scoped IAM stays deferred.

**The visual workflow designer is not an active roadmap item.** It has only ever appeared as an
engineering-side non-goal (ADR-0032, Epic-57, CTX-3B closures) — never as approved product scope. It stays
out of this roadmap unless someone deliberately proposes it as real scope; the historical ADRs that name it
as something they did *not* build remain accurate as written.

---

## Track 1 — Product Experience & UI Overhaul

- Navigation / product shell — **NAV-1 rail overhaul is complete** (Control Center / Integrations / Business Data / Config). That shipped rail is **not** near-term item 8 (cross-context grouping), which the post-RC1 Workspace direction now supersedes.
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
superseded as "next" by the 2026-09-18 post-CTX execution order, and as of 2026-09-29 by **LFC-1** (see
**Current next** above). Historical UIX-7 text that names **INT-1** as next is a later-superseded handoff, not current scheduling.

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
execution order, and as of 2026-09-29 by **LFC-1** (see **Current next** above).

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
- **Service abstraction / Service explorers / Metadata browsing** — **complete** (design gate 2026-09-23;
  implementation SVC-1…SVC-8, 2026-09-24). Reframed 2026-09-18:
  a Service is an operator-facing abstraction over a *configured Connection* (not a Systems-style grouping
  of many Integrations); the live-browse/metadata-exploration idea folds into this, not into Components. See
  the near-term execution order above and
  [`components-services-catalog/README.md`](../01-architecture/transitions/components-services-catalog/README.md)
  for the full hypothesis. **~~still not started~~** was the 2026-09-18 sentence; it is superseded by near-term
  items 5–7 (both design gates and both implementations complete).
- **Connection grouping** — not currently an independent planned deliverable; reconsider during the
  Services design gate if a concrete grouping requirement emerges. Distinct from the **Workspace**
  direction, which supersedes near-term item 8, and from any grouping primitive a Workspace design may later need.
  Neither is about grouping Connections.
- **Resource Catalog** (referenced by ADR-0040 as a separate future concept, never actually a bullet in this
  track) is **retired as a standalone roadmap concept** — its metadata/discoverability responsibility moves
  into Components (which may adapt and aggregate relevant operator-visible definitions behind a catalog
  abstraction; specialized registries such as permission descriptors, setting definitions, and connector
  definitions may remain specialized rather than being physically collapsed into one universal registry —
  no such collapse is approved), and its live-Connection-browsing responsibility moves into the Service
  abstraction above. See the planning note for the full split. **Confirmed by the Components design gate
  (2026-09-22):** no universal registry was created — Components is a read-time projection over the existing
  specialized registries, which remain authoritative for their own domains.

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

**RC1 ruling (2026-10-03):** Azure is **outside RC1 support**, and remains an architectural goal. The RC1-supported
production topology is Windows Server, SQL Server and three hosts as Windows Services, delivered by **OPS-1**.

---

## Track 8 — Module UI / configuration / template foundations

- Module-supplied UI patterns — closely related to the Components design gate (near-term order above),
  since Module is expected to become the install/distribution boundary subordinate to Components.
  **Confirmed by that gate (2026-09-22):** Modules becomes a child of `Config → System → Components`, keeps
  `/modules` and `/modules/{id}` unchanged, and gains a "Components provided by this module" section.
  Module remains the package/install boundary; catalog membership does not depend on install/enable state.
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

**RC1 ruling (2026-10-03):** this track is now defined. Packaging and installers are **OPS-1**. Smoke automation,
security review, docs completion and release-candidate validation are **RCV-1**. Security work is no longer left to the
end: the identity, audit and authorization epics run first. The RC1 exit criteria are in
[`rc1-program-gate.md`](../01-architecture/rc1-program-gate.md) section 2.

---

## Explicitly out of immediate scope

| Item | Why |
| ---- | --- |
| Management-owned WorkerHost start/stop | SWM-9/12 deferred; externally managed |
| Claiming Windows Service / Azure as shipped | Scaffold / future tracks only. Windows Service hosting becomes real in OPS-1; Azure stays outside RC1. |
| Reopening S1–S13 or SWM-1–12 | Closed unless regression |
| Premature RC tagging | Product work incomplete |

---

## Related docs

| Doc | Role |
| --- | ---- |
| [`roadmap.md`](roadmap.md) | Stage overview |
| [`rc1-program-gate.md`](../01-architecture/rc1-program-gate.md) | RC1 program contract: decisions, deferrals, epic sequence |
| [`pfb1-product-foundation-baseline-audit.md`](../audits/pfb1-product-foundation-baseline-audit.md) | Foundation baseline + next-track recommendation |
| [`stabilization-closure-check.md`](../audits/stabilization-closure-check.md) | S1–S13 closed |
| SWM ADRs under `docs/02-decisions/` | Scheduling & worker foundation closed |
| [`Worker-Hosting-Guidance-v1.md`](../03-operations/Worker-Hosting-Guidance-v1.md) | External worker hosting |
