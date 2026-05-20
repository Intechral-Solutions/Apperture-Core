# Epic 91 — Operation History and Action Feedback Groundwork

## Status

Accepted — May 2026 (documentation only).

## 1. Purpose

After [Epic 88](Epic88-Cooperative-Action-Feedback-Shell-Pilot.md) introduced **`ActionFeedback`** and [Epic 90](Epic90-Management-UI-Action-Feedback-Corrective-Pass.md) fixed persistence and adoption gaps, operators can see **the latest cooperative command result** on Run, Schedule, Blocked Work, and Module detail pages.

That banner answers “what just happened?” but not:

- “What did I (or others) do recently on this entity?”
- “Where is the durable evidence for this click?”
- “Which actions have no durable trail beyond a toast?”

This epic **defines** what “operation history” means in Apperture, inventories **existing sources of truth**, classifies **durability**, sketches a **future backend contract**, and recommends the **next implementation slice**.

This is **groundwork/design only** — no operation registry, no persistence layer, no global drawer, no production UI or API changes.

Canonical context: [Epic 87](Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md), [Epic 88](Epic88-Cooperative-Action-Feedback-Shell-Pilot.md), [Epic 89](Epic89-Management-UI-Modernization-Progress-Audit.md), [Epic 90](Epic90-Management-UI-Action-Feedback-Corrective-Pass.md), [management UI design system](../01-architecture/management-ui-design-system.md).

## 2. Current state

### Presentation layer (Epics 87–90)

| Mechanism | Role | Where used |
|-----------|------|------------|
| **`ActionFeedback`** | Page-local cooperative result banner (defaults: Title “Action”, Kind Info) | Run Detail, Schedule Detail, Blocked Work, Module Detail |
| **`PageMessage` Error** | Inline/section load or action failure | Forms, notification/integration diagnostic pages (Epic 90 COR-003) |
| **`PageMessage` Warning** | Static/section binding warnings | Integration Detail bindings, notification authoring |
| **`IToastService`** | Transient success/error on dense hubs | Integration Detail, governance, Entra, connections list |
| **`ActionButton` states** | In-button working/success/error | Connection test/create |
| **Epic 86 state wrappers** | Load/forbidden/empty/error branches | Lists, hubs, config pages |

**ActionFeedback semantics today:** page-local `_actionMessage` string set by handlers; preserved across post-action reload (`preserveActionFeedback: true`); cleared on explicit Refresh or initial load; Run Detail polling clears banner on poll tick.

### Durable record layers (already in platform)

| Layer | What it stores | Query surfaces |
|-------|----------------|----------------|
| **Runs** (`OperationRunId`) | Execution lifecycle, outcome, retry linkage, external control state | Runs list/detail, run timeline API, blocked-work projection |
| **Blocked / deferred work** | Recovery-eligible runs with reasons | Blocked Work page, Control Center cards |
| **Audit log** (`AuditRecord`) | Append-only operator/system actions with `RunId`, `ScheduleId`, `ModuleId`, outcome, actor, correlation | Audit Timeline (`/control/audit`), filtered drill-ins |
| **Run timeline** | Evidence-backed execution events (lifecycle, external control, retry link) | `GET /api/runs/{id}/timeline` (not yet a first-class UI panel on Run Detail) |
| **Schedule runs / dispatch** | Scheduler claim/dispatch outcomes | Schedule detail last-outcome fields; scheduler audit actions |
| **Module registry state** | Installed/enabled lifecycle | Module detail state panel |
| **Integration / connection / notification config** | Persisted configuration entities | Entity detail pages (configuration is durable; **the click** may not be) |
| **Cutover sign-off store** | Expected-run-missing sign-off records | Integration notifications, notification diagnostics |

There is **no** unified `ManagementOperationRecord` or global operation registry today.

## 3. Operation categories

Categories describe **operator intent** and **expected durability**, not UI components.

| Category | Definition | Typical feedback today | Durable trail |
|----------|------------|------------------------|---------------|
| **Run-backed operation** | Command whose primary outcome is a run or run state change | `ActionFeedback`, sometimes new-run drill-in | **Run record** + often **run timeline** events |
| **Audit-backed operation** | Command explicitly written to platform audit | Toast or `ActionFeedback`; audit is evidence | **`AuditRecord`** with entity refs |
| **Blocked-work-backed operation** | Recovery action on a blocked/deferred run | `ActionFeedback` + table refresh | **New run** and/or **recovery audit** entries |
| **Transient command** | API succeeds/fails; no first-class operation record | `ActionFeedback`, toast, or `ActionButton` | **None** (UI only) unless side effect elsewhere |
| **Form / configuration save** | Persist edited fields | `PageMessage` Error or navigate on success | **Entity state** (integration, schedule, definition, contact) |
| **Lifecycle command** | Enable/disable/install/activate/deactivate/delete | `ActionFeedback` or toast | **Entity state**; **module** and **notification cutover** also audit |
| **Diagnostic command** | Read-only or read-mostly evaluation | `PageMessage` Error, muted diagnostics | Usually **none**; parity/sign-off may audit |
| **External verification command** | Test connection, validate OIDC | `ActionButton` or toast | **Weak** — connection health fields may update; often no audit |

### What counts as a user-triggered management operation?

For future history purposes, include:

1. **Mutating API calls** initiated from Management UI (POST/PUT/PATCH/DELETE), including cooperative execution controls.
2. **Recovery and scheduler operator commands** (retry, run cycle trigger, schedule enable/disable/trigger).
3. **Governance and auth configuration mutations** (role assign, permission save, Entra tenant changes).
4. **Integration/connection/binding/schedule-create** mutations on Integration Detail and related routes.

Exclude:

- Navigation, refresh/reload, local UI toggles (edit mode, poll checkbox, wizard step).
- Pure diagnostic drill-ins (opening audit timeline, run forensics sections).
- Background scheduler/system dispatch (unless surfaced as operator-relevant evidence on entity history).

## 4. Existing sources of truth

| Source | What it records | Examples | Strengths | Gaps | UI suitability |
|--------|-----------------|----------|-----------|------|------------------|
| **Run record** | Execution identity, status, outcome, definition/module/integration linkage, retry-of | Procedure launch → run; retry → new run; cancel/park/resume state | Authoritative for execution; list/filter/drill-in mature | Does not label *operator intent* (“Retry clicked” vs “Started by schedule”); no actor on all paths | Run Detail summary; Runs list; drill-in from blocked work |
| **Run timeline** | Ordered evidence-backed events on one run | `externalControlRequested`, `retryLinked`, lifecycle transitions | Explains *what happened to this run* without conflating audit categories | Not shown as operator “recent actions” panel; workflow vs operation event sets differ | Advanced/history section on Run Detail (future) |
| **Audit log** | Append-only actions with actor, outcome, optional RunId/ScheduleId/ModuleId | `recovery.blocked_work.retry_dispatched`, `module.lifecycle.enable`, `governance.role.assignment.created`, `scheduler.schedule.dispatched` | Cross-entity timeline; filterable; actor attribution | **Incomplete** for many UI mutations (schedule enable/disable, integration activate, connection test, run cancel/park/resume success paths); heterogeneous action naming | Audit Timeline; entity-filtered “recent actions” (future) |
| **Blocked work projection** | Runs needing recovery with eligibility | Failed/blocked workflows and operations | Action-oriented lens for operators | Not a history of commands; snapshot not log | Blocked Work table |
| **Recovery audit prefix** | Recovery-specific audit subset | Retry requested/dispatched/refused, run cycle requested/denied | Links UI retry to evidence | Only recovery/scheduler operator commands | `Epic33UiDisplayHelpers.GetRecoveryAuditLink()` |
| **Module registry + audit** | Module state transitions | Install/enable/disable/uninstall | State panel + `module.lifecycle.*` audit when service succeeds | UI banner is transient; audit actor may be system vs operator depending on path | Module Detail state + audit drill-in |
| **Schedule definition state** | Enabled flag, last outcome, stale claim | Enable/disable from Schedule Detail | Entity truth for schedule | Enable/disable **not** consistently audited as operator actions | Schedule Detail summary |
| **Integration/connection persisted config** | Bindings, settings, lifecycle state | Save settings, add binding, retire connection | Configuration is durable | Individual clicks mostly **toast-only** | Integration Detail sections |
| **Notification cutover sign-off store** | Sign-off/revoke/replace records | Integration notifications sign-off | Durable domain artifact | Narrow scope | Integration notifications |
| **Transient UI state** | Last handler message | `_actionMessage`, toasts, `ActionButton` state | Immediate operator feedback | **Lost on refresh** (except ActionFeedback until Refresh) | `ActionFeedback` banner |

## 5. Operation durability matrix

“Durable record today?” means an operator can find evidence **without relying on the last banner/toast**.

| Action / surface | Current feedback | Durable record today? | Current drill-in target | Future history readiness | Recommended next step |
|------------------|------------------|----------------------|---------------------------|--------------------------|------------------------|
| Run cancel/retry/park/resume | `ActionFeedback` | **Partial** — run state / new run / timeline events; **no** dedicated audit action for success | Same run; new run on retry; run timeline API | **High** for entity-local timeline panel | Show run timeline + audit filtered by `runId` on Run Detail |
| Schedule enable/disable/trigger | `ActionFeedback` | **Partial** — schedule state / last outcome; trigger → recovery audit `schedules.run_cycle_requested` | Schedule detail; recovery audit for trigger | **Medium** — audit gap on enable/disable | Entity-local audit panel filtered by `scheduleId`; consider audit on enable/disable |
| Blocked work retry | `ActionFeedback` + new-run link | **Yes** — recovery audit + new run when dispatched | New run; recovery audit | **High** | Link banner to recovery audit row; optional recent recovery actions on page |
| Blocked work trigger run cycle | `ActionFeedback` | **Yes** — `recovery.schedules.run_cycle_requested` audit | Recovery audit | **High** | Same as above |
| Module install/enable/disable/uninstall | `ActionFeedback` | **Yes** — module state + `module.lifecycle.*` audit | Module detail; audit `module.lifecycle.*` | **High** | Entity-local audit strip filtered by `moduleId` |
| Integration procedure launch | Toast | **Yes** — new **run** | Run detail / runs list | **High** (run-backed) | Toast + run drill-in pattern; optional “last launch” from runs filter |
| Integration activate/deactivate/settings/bindings | Toast / section warnings | **Partial** — integration state persists; **weak** operator audit | Integration detail | **Low–medium** | Audit or operation record for integration mutations |
| Connection test | `ActionButton` | **Weak** — may update connection health metadata | Connection detail | **Low** | Keep ActionButton; defer history until verifier audit exists |
| Connection create/retire | Toast / navigate | **Partial** — connection entity | Connection detail | **Medium** | Config change audit (future) |
| Notification definition save | `PageMessage` Error / navigate | **Yes** — definition entity | Definition detail | **Medium** (config not command) | Stay form-local |
| Notification subscription add/remove | `PageMessage` Error | **Partial** — subscription rows | Integration notifications | **Medium** | Section-local recent changes or audit |
| Notification cutover sign-off | `PageMessage` Error | **Yes** — sign-off store + notification audit actions | Sign-off record on page | **High** | Already durable; improve visibility not registry |
| Notification diagnostics actions | `PageMessage` Error | **Mostly no** (read/evaluate) | Diagnostics output | **Low** | Not operation history candidates |
| Governance role/permission save | Toast | **Yes** — `governance.*` audit | Audit timeline `governance.` prefix | **High** | Governance detail → filtered audit link |
| Entra tenant / sign-in save | Toast / `PageMessage` | **Partial** — persisted config; auth audit on some paths | Config pages | **Medium** | Audit coverage review (out of UI epic scope) |
| Form validation failure | Inline / `PageMessage` | **No** | — | **N/A** | Never history |

**Future operation-instance candidate:** actions that are **transient in UI** but **high operator value** and **incomplete in audit** — integration lifecycle/bindings, schedule enable/disable, connection test, run cooperative controls (for actor attribution).

## 6. UI placement model

### Layer A — Page-local `ActionFeedback` banner (implemented)

- **Purpose:** “Result of the command I just issued.”
- **Placement:** Below `PageHeader`, above entity content.
- **Lifetime:** Session-local until Refresh, navigation away, or poll (Run Detail).
- **Not:** history, registry, or substitute for audit.

### Layer B — Section-local messages (implemented)

- **Purpose:** Errors/warnings tied to one panel (bindings, subscriptions, forms).
- **Examples:** Integration binding warning, notification `PageMessage` Error.
- **Not:** chronological history.

### Layer C — Entity-local operation history (recommended first implementation)

- **Purpose:** “Recent operator/system actions affecting **this** run, schedule, module, or integration.”
- **Placement:** `ManagementAdvancedSection` or dedicated “Recent activity” on entity detail pages.
- **Data:** Prefer **existing audit query** (`runId`, `scheduleId`, `moduleId`, action prefix) plus **run timeline** on Run Detail.
- **Drill-in:** Audit Timeline with filters pre-filled; run timeline events; related runs.

### Layer D — Domain evidence panels (partially implemented)

- **Purpose:** Diagnostic truth, not command log.
- **Examples:** Run forensics, notification diagnostics parity, integration cutover readiness.
- **Rule:** Do not mislabel diagnostics as “operations.”

### Layer E — Global recent operations drawer (deferred)

- **Purpose:** Cross-entity “what happened recently in the management plane?”
- **Requires:** Unified operation model or normalized audit view; permission model for cross-entity listing.
- **Risk:** Duplicates Audit Timeline without clear semantics — defer until entity-local panels prove value.

### Audit / run drill-in links (implemented)

- `Epic33UiDisplayHelpers` audit/run/module links from pilots and Blocked Work.
- Audit Timeline supports `actionPrefix`, outcome, actor, time range, and reference filters (Epic 37 columns).

## 7. Future backend contract (sketch only)

Introduce a **`ManagementOperationRecord`** (or **`OperationInstance`**) **only if** entity-local audit/timeline panels expose gaps operators care about (actor, status progression, unified type key).

### Proposed shape (illustrative)

```csharp
// NOT IMPLEMENTED — design sketch for a future epic
public sealed record ManagementOperationRecord(
    ManagementOperationId Id,
    string OperationType,           // e.g. "run.retry", "schedule.enable", "integration.binding.add"
    ManagementOperationScope Scope, // EntityType + EntityId (Run, Schedule, Module, Integration, Instance)
    ActorId ActorId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    ManagementOperationStatus Status, // Requested | Succeeded | Failed | Blocked | Denied
    string DisplayMessage,            // Operator-facing summary (stable for UI)
    CorrelationId CorrelationId,
    string? RelatedRunId,
    string? RelatedAuditRecordId,
    string? RecoveryDrillInPath);     // Optional UI route
```

### Design rules

1. **Append-only or status-updated rows** — no replacement for audit log; either audit remains source of truth with optional materialized view, or operation record **references** audit/run ids.
2. **Operation type taxonomy** — stable string keys mapped from existing audit actions where possible (`recovery.blocked_work.retry_dispatched` → `recovery.blocked_work.retry`).
3. **Scope-first queries** — `ListRecentOperations(scope, limit)` for entity panels; global drawer is a later consumer.
4. **Do not duplicate run lifecycle** — run timeline and run record remain authoritative for step-level execution; operation record covers **management plane commands**.
5. **Idempotency / correlation** — reuse existing correlation from recovery retry and scheduler dispatch where present.

### API sketch (future)

- `GET /api/control/operations?runId=&scheduleId=&moduleId=&limit=`
- Optional: `GET /api/control/operations/recent` for global drawer (later)

### Migration path

1. **Phase 0 (no new tables):** Entity-local panels from audit + run timeline (Epic 92 recommendation).
2. **Phase 1:** Audit gap closure for high-value mutations (schedule enable/disable, integration lifecycle).
3. **Phase 2:** Optional `ManagementOperations` table/materialized view if audit normalization is insufficient.
4. **Phase 3:** Global drawer and cross-session “my recent operations.”

## 8. Non-goals

Explicitly **not** in Epic 91 or implied follow-ups without a new epic:

- Global **operation registry** service in UI
- **Operation persistence** tables or migrations
- **Operation history drawer** component
- **Backend/API** implementation
- **Polling** or refresh redesign
- **Toast normalization** or replacing Integration Detail patterns
- **Action handler** or permission changes
- **Workflow/user-task** integration
- Stats/charts, schema-driven forms, module dashboards, calendar/temporal UX

## 9. Recommended next epic

**Epic 92 (tentative): Entity-local recent operator activity panel (audit- and timeline-backed)**

Safest first implementation slice:

1. **Run Detail** — `ManagementAdvancedSection` “Recent activity” combining **`GetRunTimelineAsync`** events and audit rows filtered by `runId` (includes external control and retry link evidence).
2. **Schedule Detail** — audit panel filtered by `scheduleId` + link to scheduler/recovery prefixes.
3. **Module Detail** — audit panel filtered by `moduleId` (`module.lifecycle.*`).
4. **Blocked Work** — compact link strip to recovery audit (already present) plus optional last-N recovery audit entries for the visible window.

**Why this over a new table first:** proves UI placement, drill-in conventions, and operator value using **existing** sources; exposes audit gaps empirically before schema design.

**Alternatives (defer unless blocked):**

| Option | When to choose |
|--------|----------------|
| **Backend `ManagementOperationRecord` foundation** | After Epic 92 panels show repeated audit gaps or need actor/status on run controls |
| **Continued `ActionFeedback` adoption** (Integration Detail) | After toast vs banner UX review (ADV-003); not a substitute for history |
| **Visual/density pass** | Independent; does not unblock history |

## 10. Validation

Documentation-only epic:

- Epic 91 document created with categories, source inventory, durability matrix, UI model, backend sketch, and next-epic recommendation.
- Cross-links updated in decisions index and design system.
- **No production code changes** — build not required.

Manual review checklist for doc accuracy:

- [ ] Audit action prefixes match `AuditTimeline.razor` category filter options
- [ ] Recovery audit names match `RecoveryAuditActionNames`
- [ ] ActionFeedback surfaces match Epic 90 implementation
- [ ] Run timeline event kinds match `RunTimeline` constants

## Related documents

- [Epic 87 — Action surface groundwork](Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md)
- [Epic 88 — Cooperative action feedback shell](Epic88-Cooperative-Action-Feedback-Shell-Pilot.md)
- [Epic 90 — Action feedback corrective pass](Epic90-Management-UI-Action-Feedback-Corrective-Pass.md)
- [Management UI design system](../01-architecture/management-ui-design-system.md)
