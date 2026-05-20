# Management UI design system

Epic 79 introduces the first **practical** design-system foundation for the Blazor WASM Management UI (`Apperture.Management.Ui.Client`). It supports the layering model and archetypes defined in [Epic 78](../02-decisions/Epic78-Management-UI-Modernization-Groundwork.md) without redesigning major operator pages.

Related shell documentation: [control-panel-ui.md](control-panel-ui.md).

## Purpose

- Give implementers **shared primitives** for summary, detail, and advanced presentation layers.
- Align **status and attention** visuals with platform semantics (health, dashboard cards, permissions).
- Document **page-state** conventions (loading, empty, error, forbidden) on top of existing `PageMessage`.
- Preserve **backend truth**, permission omission, and drill-in query contracts from prior epics.

## Relationship to Epic 78

| Epic 78 deliverable | Epic 79 implementation |
|---------------------|-------------------------|
| Page archetypes | Components map to layers (see below); no archetype framework |
| Summary / detail / advanced layers | `ManagementSection`, `ManagementSummaryCard`, `ManagementAdvancedSection` |
| Permission / empty / error states | `PageMessage` + thin wrappers (`LoadingState`, `EmptyState`, …) |
| Drill-in rules | `DrillInLink`; URLs still built with `Epic33UiDisplayHelpers` |
| Pilot pages | `/config` hub proof; Integration Detail ([Epic 80](../../02-decisions/Epic80-Integration-Detail-Redesign-Pilot.md)); Run Detail ([Epic 81](../../02-decisions/Epic81-Run-Detail-Redesign-Pilot.md)); Control Center ([Epic 82](../../02-decisions/Epic82-Control-Center-Redesign-Pilot.md)); Schedules ([Epic 83](../../02-decisions/Epic83-Schedules-List-Detail-Redesign-Pilot.md)); Notification Definition Authoring ([Epic 84](../../02-decisions/Epic84-Notification-Definition-Authoring-Redesign-Pilot.md)); consolidation guidance ([Epic 85](../../02-decisions/Epic85-Management-UI-Pilot-Consolidation.md)); state-branch consistency sweep ([Epic 86](../../02-decisions/Epic86-Management-UI-State-Branch-Consistency-Sweep.md)); action surface groundwork ([Epic 87](../../02-decisions/Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md)); cooperative action feedback shell ([Epic 88](../../02-decisions/Epic88-Cooperative-Action-Feedback-Shell-Pilot.md)); other pilots per Epic 78 |

## Design principles

1. **Backend truth** — Components display API fields and capability gates; no inferred state.
2. **Permission omission** — Hide sections or cards when forbidden; do not show locked placeholders on dashboard cards (Epic 45).
3. **Composable, not generic** — Small Razor components with explicit parameters; no templating engine.
4. **Incremental adoption** — New and touched pages use primitives; legacy markup remains valid until pilot epics.
5. **Semantic tokens** — Visual roles use CSS variables in `wwwroot/css/app.css`; layout/status helpers in `management-ui-primitives.css`.

## Component categories

| Category | Location | Role |
|----------|----------|------|
| **Atoms** | `Atoms/` | `StatusPill`, `AttentionPill`, `DrillInLink`, `ActionButton`, logo |
| **Molecules** | `Molecules/` | `ManagementSummaryCard`, `ManagementAdvancedSection`, page-state wrappers, `ActionFeedback` |
| **Organisms** | `Organisms/` | `PageHeader`, `PageMessage`, `ManagementSection`, shell, notification forms |
| **Semantics** | `Apperture.Management.Ui.Shared` | `ManagementUiSemantics`, `Epic33UiDisplayHelpers` (truth/links) |

### Existing components (retained)

- **`PageHeader`** — Page title, optional **Eyebrow** (domain breadcrumb), subtitle, toolbar slot.
- **`PageMessage`** — Canonical inline state banner (`PageMessageKind`: Loading, Empty, Info, Warning, Error, Forbidden).
- **`ActionButton`** — Async action feedback (working/success/error); use for cooperative execution actions, not navigation.

### Epic 79 primitives

| Component | Layer | Use when |
|-----------|-------|----------|
| `ManagementSection` | Detail (default) | Bordered panel with title, subtitle, help, toolbar; set `Layer` for `data-ui-layer` |
| `ManagementSummaryCard` | Summary | Hub tiles, KPI-style entry points with optional `Href` |
| `ManagementAdvancedSection` | Advanced | Collapsible `<details>` for forensic/technical blocks |
| `StatusPill` | Summary / detail | Health or lifecycle status chips |
| `AttentionPill` | Summary | Dashboard attention tokens (`Critical`, `Warning`, `Info`) |
| `DrillInLink` | Detail | Underlined drill-in with `ui-drill-in-link` class |
| `LoadingState` / `EmptyState` / `ForbiddenState` / `ErrorState` | Any | Thin aliases over `PageMessage` for consistent defaults |
| `ActionFeedback` | Detail (below header) | Cooperative/user-triggered action result banner (Epic 88); defaults Title “Action”, Kind Info |

## Layout primitives and Epic 78 layers

| Epic 78 layer | Components / patterns |
|---------------|------------------------|
| **1 — Summary / orientation** | `PageHeader`, `ManagementSummaryCard`, `StatusPill`, `AttentionPill`, top actions in toolbar |
| **2 — Operational detail** | `ManagementSection` (default), tables, `DrillInLink`, routine forms |
| **3 — Advanced / diagnostic** | `ManagementAdvancedSection`, separate diagnostics routes |
| **4 — Contextual help** | `ManagementSection.Help`, subtitle on `PageHeader`, `text-subtle` copy |
| **5 — Guided action / procedure** | *Deferred* — preserve `ProcedureParameterForm`, `ActionButton`, wizard services; no new wizard shell |

**Page stack:** wrap page body in `ui-page-stack` (vertical rhythm) inside layout content padding (`p-4`).

## Status semantics

`ManagementUiSemantics` maps semantic roles to pill CSS classes (`ui-status-pill--*`).

| Semantic | Typical use | Visual role |
|----------|-------------|-------------|
| Success | Healthy, completed | Green mix |
| Warning | Degraded, advisory | Amber mix |
| Error / Blocking | Failed, blocked | Red mix |
| Pending | In progress, queued | Info mix |
| Neutral | Disabled, idle | Muted surface |
| Unknown | Missing data | Muted dashed |
| Forbidden | Not available to role | Muted + dashed border |

**Runtime health strings:** `Healthy` → Success; `Degraded` → Warning; other → Error. Legacy inline text may still use `Epic33UiDisplayHelpers.GetStatusCssClass`; new work should prefer `StatusPill`.

## Attention / health / readiness

| Source | Component | Notes |
|--------|-----------|-------|
| Dashboard card `Attention` | `AttentionPill` + `MapDashboardAttention` | Matches Epic 45–47 card tokens |
| Platform health overall status | `StatusPill` + `RuntimeHealthStatus` | Same mapping as control center |
| Attention routing / notifications | Copy + `PageMessage` Warning | Do not invent severity without API token |

## Empty / loading / error / forbidden states

Use **`PageMessage`** directly or the wrappers:

| Situation | Component | Copy guidance |
|-----------|-----------|---------------|
| Loading | `LoadingState` | Neutral; block forbidden flash until capabilities load |
| Authorized, no rows | `EmptyState` | Neutral; optional create action in `ChildContent` if permitted |
| Forbidden | `ForbiddenState` | Never label as “empty” |
| HTTP / unexpected failure | `ErrorState` | Distinct from empty; optional retry in `ChildContent` |
| Dashboard all cards omitted | `PageMessage` / dashboard copy | Permissions, not failure (Epic 45) |
| Dashboard provider failure | Dashboard home placeholder | Partial load card |
| Deferred feature | Plain text + menu “deferred” label | No fake data |

## Form and control conventions (high level)

- Prefer **metadata-driven** inputs (`ProcedureParameterForm`) for execution starts; no new schema framework in Epic 79.
- Primary actions: `ActionButton` or `btn btn-primary` / `btn-secondary` utilities from `app.css`.
- Destructive: `ActionButtonVariant.Danger` or `btn-danger` / `bg-destructive` toolbar buttons.
- Section labels: `text-xs text-muted` for field labels; `text-heading` for section titles inside `ManagementSection`.
- See **Action surface and feedback** (Epic 87) below for placement and result messaging rules.

## Action surface and feedback (Epic 87)

Epic 87 inventories action and operation-feedback patterns across pilot and representative pages. It does **not** introduce an async operation shell or operation registry.

### Action placement

| Surface | Use for | Examples |
|---------|---------|----------|
| **`PageHeader` / `Toolbar`** | Page-scoped navigation, refresh, lifecycle commands, cooperative execution controls | Run Detail cancel/retry/park; Integration Detail activate/delete/refresh; Schedule Detail enable/disable/trigger |
| **`ManagementSection` header / body** | Section-scoped routine operations tied to one panel | Notification definition subscriptions Add/Remove; authoring Validate/Preview |
| **Inline form footer** | Save/create/submit for a single form | `NotificationDefinitionAuthoringForm` Save; Entra tenant edit; Sign-in policy save |
| **Table row / cell** | Row open, per-item retry, toggle | Connections list Open; blocked-work Retry; definition list enable checkbox |
| **`ManagementSummaryCard` / `DrillInLink`** | Navigation and remediation entry points | Control Center cards; schedule drill-ins to runs/audit |

**Rule:** Put **action-critical** controls in summary or detail layers (Epic 78). Reserve **`ManagementAdvancedSection`** for diagnostic drill-ins that are not required to complete the operator’s immediate task.

### Action categories

| Category | Operator intent | Feedback expectation | Current examples |
|----------|-----------------|----------------------|------------------|
| **Navigation** | Go to related page/detail | None (navigation is the outcome) | Integration Detail → Execute/Notifications; Control Center summary cards; `DrillInLink` on Run/Schedule detail |
| **Local UI** | Change view state only | Immediate control state (expanded, editing, wizard step) | Schedule Detail Edit mode; Integration wizard step select; Run Detail Poll checkbox |
| **Form submit/save** | Persist configuration | **Inline** `PageMessage` Error on failure; navigate or stay on success per existing flow | Sign-in policy save (`_saveError`); Entra tenant forms (`_formError`); notification authoring Save |
| **Immediate command** | Short API call, result known quickly | Toast **or** inline `PageMessage` **or** `ActionButton` success/error state | Connection test/create (`ActionButton`); governance role assign (toast) |
| **Async/cooperative command** | Work may continue after click; eligibility matters | **`ActionFeedback`** at page top (below header) **or** toast on dense hubs; preserve domain copy | Run Detail cancel/retry/park/resume; Schedule Detail enable/disable/trigger; Blocked Work (deferred) |
| **Destructive command** | Irreversible or high-impact change | Existing **confirm** + toast or reload; warning styling on control | Integration Delete (`ConfirmDeleteAsync` + toast); Connection Retire; role permission replace warning |
| **Refresh/reload** | Reload page data | Usually none; loading state on page/section | Nearly all pilot `PageHeader` Refresh buttons |
| **Remediation / action-needed** | Jump to fix or diagnostic | Link visible in summary when blocking | Integration readiness blockers; notification “no deliverable recipients” attention |
| **Diagnostic drill-in** | Open evidence (audit, runs, diagnostics) | None | Schedule Detail audit/runs links; notification diagnostics routes |

### Feedback placement rules

| Feedback type | Preferred surface | Do not use for |
|---------------|-------------------|----------------|
| **Form validation** | Field labels, validation lists under form, `ErrorState` **ChildContent** for server validation lists (Schedule Detail) | Whole-page `ErrorState` for field-level validation |
| **Form save failure** | `PageMessage` Error **adjacent to form actions** | `ForbiddenState` |
| **Cooperative command result** | **`ActionFeedback`** at **page top** (below header), optional `ChildContent` / `DrillInLink` | Toasts alone when operator must correlate with run/schedule state; `ErrorState` for load failures |
| **Transient success on dense forms** | `IToastService` | Replacing persistent action banners on Run/Schedule detail |
| **Section binding / in-flow errors** | `PageMessage` Warning in the **same section** as the control | Page-level error that hides the form |
| **Load failure** | `ErrorState` / `LoadingState` / `ForbiddenState` / `EmptyState` (Epic 86) | Action result messaging |
| **Future operation history shell** | Extend **`ActionFeedback`** or add history UI when backend contracts exist | Operation IDs, persistence, global registry |

### `ActionFeedback` (Epic 88)

Thin presentation shell over **`PageMessage`** for **user-triggered cooperative action results**. Not an operation tracker, registry, or global service.

| Parameter | Default | Use |
|-----------|---------|-----|
| `Kind` | `PageMessageKind.Info` | Map to Info/Success/Warning/Error when page semantics require distinct styling; Run/Schedule pilots keep **Info** for all outcomes including 403/409 |
| `Title` | `"Action"` | Banner heading |
| `Message` | (required) | Operator-facing result copy from page-local `_actionMessage` |
| `ChildContent` | — | Optional drill-in (e.g. new run link after retry) |

**Use `ActionFeedback` for:** run cancel/retry/park/resume results; schedule enable/disable/trigger results; blocked-work retry/run-cycle; module lifecycle results.

**Use `PageMessage` directly for:** static warnings, form save errors, section binding errors, load/empty/forbidden branches (Epic 86 wrappers), diagnostic notices.

**Proof surfaces:** [Run Detail](../../02-decisions/Epic81-Run-Detail-Redesign-Pilot.md) (`/runs/{RunId}`), [Schedule Detail](../../02-decisions/Epic83-Schedules-List-Detail-Redesign-Pilot.md) (`/schedules/{ScheduleId}`), Blocked Work, Module Detail ([Epic 90](../../02-decisions/Epic90-Management-UI-Action-Feedback-Corrective-Pass.md)).

See [Epic 88 — Cooperative action feedback shell pilot](../02-decisions/Epic88-Cooperative-Action-Feedback-Shell-Pilot.md). Operation history planning: [Epic 91](../02-decisions/Epic91-Operation-History-and-Action-Feedback-Groundwork.md).

### `ActionButton` usage

Use **`ActionButton`** when the operator needs **in-button** working/success/error feedback on a **single** cooperative or immediate command:

- Connection **Test** and **Create** (`ConnectionDetail`, `ConnectionNew`) — caller drives `SetStateAsync(Active|Success|Error)`.
- Optional **cancel-on-hover** when `OnCancel` is wired.

Do **not** replace Run Detail execution controls, Schedule Detail lifecycle buttons, or Integration Detail toolbar commands with `ActionButton` in this phase — those pages use domain-specific eligibility, confirmation, and `_actionMessage` / toast patterns that must remain unchanged.

### Destructive actions

- Keep existing **`confirm()`** / `ConfirmDeleteAsync` flows and permission gates.
- Style: `ActionButtonVariant.Danger`, `btn-danger`, or `bg-destructive` in toolbar.
- Show **pre-save warnings** in place (e.g. role permission replace banner) — do not collapse into a toast only.

### Async/cooperative actions — future shell candidates

Document for a **future async operation shell** epic (not implemented here):

| Area | Actions | Current feedback |
|------|---------|------------------|
| **Run Detail** | Cancel, retry, park, resume | `ActionFeedback` + poll; new-run `DrillInLink` in ChildContent on retry |
| **Schedule Detail** | Enable, disable, trigger run cycle | `ActionFeedback` |
| **Blocked Work** | Trigger run cycle, per-run retry | `ActionFeedback` + table actions |
| **Integration Detail** | Launch procedure, activate/deactivate, binding CRUD, schedule create | Toasts + section warnings |
| **Connection Detail** | Test connection | `ActionButton` states |
| **Governance** | Role assign/revoke, permission save | Toasts |
| **Entra tenants** | Validate OIDC, activate, CRUD | Toasts |

### What not to abstract yet (actions)

- No global **operation registry**, **procedure abstraction**, or **operation history** UI.
- No replacement for Run Detail cooperative semantics or capability-driven `CanInvoke` / `ActionReason`.
- No mandatory `ActionButton` on all toolbars.
- No new toast vs `PageMessage` routing layer — follow tables above per page archetype.

### Future operation shell readiness

Epic 88 introduced **`ActionFeedback`** as the first reusable **presentation** layer for cooperative results. [Epic 91](../02-decisions/Epic91-Operation-History-and-Action-Feedback-Groundwork.md) defines operation history semantics, existing sources of truth, and UI placement before any global shell. Until then:

1. Keep action copy and eligibility logic **page-local** (`_actionMessage`, handlers unchanged).
2. Reuse **`ActionFeedback`** + **`ActionButton`** prior art before adding global services.
3. Prefer **entity-local recent activity** from audit/run timeline before a new operation table.
4. Preserve **403/409** messaging contracts from Run Detail and Schedule Detail.

See [Epic 87](../02-decisions/Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md), [Epic 88](../02-decisions/Epic88-Cooperative-Action-Feedback-Shell-Pilot.md), and [Epic 91](../02-decisions/Epic91-Operation-History-and-Action-Feedback-Groundwork.md).

## Drill-in conventions

- Build URLs with **`Epic33UiDisplayHelpers`** (audit, runs, modules) or documented query params (Epic 47, Epic 78 §7).
- Present links with **`DrillInLink`** or `class="ui-drill-in-link"`.
- Label destination explicitly (“View blocked work”, not “Click here”).
- Omit link when filter unsupported or permission missing.
- For clickable table rows, keep inner “Open” links as plain anchors with `class="ui-drill-in-link"` and `@onclick:stopPropagation`; `DrillInLink` does not expose event-modifier attributes.

## Proven archetypes from pilots

| Archetype | Pilot proof | Recommended pattern |
|-----------|-------------|---------------------|
| **Entity hub** | Integration Detail (Epic 80) | `PageHeader` + summary pills/cards + grouped `ManagementSection` blocks + one advanced section for ids/lineage/diagnostics |
| **Execution detail** | Run Detail (Epic 81) | Summary band + actionability-first detail sections + advanced technical records/protocol payloads |
| **Operational console** | Control Center (Epic 82) | Summary section with posture pills/cards + separate operational concerns and activity sections + advanced evidence |
| **List/detail pair** | Schedules (Epic 83) | List summary + operational table + advanced caveats; detail summary + routine config + advanced identifiers |
| **Dense authoring surface** | Notification definitions (Epic 84) | Summary orientation + one operational authoring section + advanced raw payloads |

## Consolidated pilot patterns (Epic 85)

- **Summary card layout:** use `ui-summary-card-grid` for repeated 3-up summary card regions; add `ui-summary-card-grid--wide` only when four cards are required.
- **Status semantics:** keep shared status mapping in `ManagementUiSemantics` for cross-page lifecycle strings; keep domain-only interpretations local to the page.
- **State wrappers:** use `LoadingState` / `EmptyState` / `ForbiddenState` / `ErrorState` for standard page states; use `PageMessage` directly for contextual informational/warning banners inside a loaded page.
- **Advanced posture:** collapse raw ids, verbose payloads, and low-frequency diagnostics in `ManagementAdvancedSection`; keep actionable operational blockers visible in detail layers.

## What not to abstract yet

- Do not introduce a generic “summary band” component spanning integrations, runs, control center, schedules, and notifications.
- Do not centralize page-specific status/token interpretation that depends on route/domain semantics.
- Do not create a schema-driven authoring abstraction for notification rule/template surfaces in this phase.
- Do not add a generic operation-history shell or wizard framework without a dedicated epic.

## Future-epic readiness

The pilot stack is now ready for these follow-on epics, but none are implemented by the design-system itself:

- **Async operation shell:** `ActionFeedback` (Epic 88) on Run/Schedule/Blocked Work/Module detail (Epic 90); entity-local history from audit/run timeline (Epic 91 groundwork); operation **registry** and backend tracking remain deferred — see [Epic 88](../../02-decisions/Epic88-Cooperative-Action-Feedback-Shell-Pilot.md), [Epic 90](../../02-decisions/Epic90-Management-UI-Action-Feedback-Corrective-Pass.md), [Epic 91](../../02-decisions/Epic91-Operation-History-and-Action-Feedback-Groundwork.md).
- **Schema-driven forms:** evolve `ProcedureParameterForm` only when execution-start and dense authoring surfaces share a stable metadata contract.
- **Module dashboards:** reuse `ManagementSummaryCard` posture and permission omission from dashboard/card patterns; keep module-owned UI in module assemblies.
- **Operational stats/charts:** add as summary-layer artifacts only when APIs provide truthful aggregates and drill-in contracts.
- **Calendar/temporal context:** extend list/detail filtering conventions only when schedule/audit APIs expose explicit temporal query contracts.

## Accessibility and responsiveness

- `PageMessage` uses `role="alert"` for errors and `role="status"` otherwise.
- `ManagementAdvancedSection` uses native `<details>` / `<summary>` for keyboard-expandable advanced blocks.
- Layout grids: `grid-cols-1 md:grid-cols-2` for summary cards; sections stack on narrow viewports.
- Focus: use `focus-ring` on interactive controls (buttons in `app.css`).
- Theme: `ThemeService` + semantic CSS variables; primitives respect light/dark via `app.css` theme blocks.

## CSS and tokens

| File | Contents |
|------|----------|
| `wwwroot/css/app.css` | Tailwind v4 build + semantic theme tokens (`--color-surface`, `--color-success`, message callouts, buttons) |
| `wwwroot/css/management-ui-primitives.css` | Epic 79 panel/card/pill/link/stack classes referencing theme variables |

Do not add one-off hex colors in Razor; extend semantic tokens in `app.css` when a new **role** is needed.

## Future extension seams

- **`IDashboardCardProvider`** — Server-side summary cards; client renders with `ManagementSummaryCard`-like layout in pilots.
- **Module UI projects** — Keep module-specific UI in module `.Ui` assemblies; shared primitives stay in Management.Ui.Client.
- **`IOperatorCapabilitiesService`** — Gate sections before rendering forbidden states.
- **Toasts** — Transient feedback on long forms (`IToastService`); not a substitute for `PageMessage`.
- **Epic 80+ pilots** — control center ([Epic 82](../../02-decisions/Epic82-Control-Center-Redesign-Pilot.md)), run detail ([Epic 81](../../02-decisions/Epic81-Run-Detail-Redesign-Pilot.md)), integration detail ([Epic 80](../../02-decisions/Epic80-Integration-Detail-Redesign-Pilot.md)), schedules ([Epic 83](../../02-decisions/Epic83-Schedules-List-Detail-Redesign-Pilot.md)), notification definition authoring ([Epic 84](../../02-decisions/Epic84-Notification-Definition-Authoring-Redesign-Pilot.md)), consolidation ([Epic 85](../../02-decisions/Epic85-Management-UI-Pilot-Consolidation.md)), state sweep ([Epic 86](../../02-decisions/Epic86-Management-UI-State-Branch-Consistency-Sweep.md)), action groundwork ([Epic 87](../../02-decisions/Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md)), action feedback shell ([Epic 88](../../02-decisions/Epic88-Cooperative-Action-Feedback-Shell-Pilot.md)), progress audit ([Epic 89](../../02-decisions/Epic89-Management-UI-Modernization-Progress-Audit.md)), action feedback corrective pass ([Epic 90](../../02-decisions/Epic90-Management-UI-Action-Feedback-Corrective-Pass.md)), operation history groundwork ([Epic 91](../../02-decisions/Epic91-Operation-History-and-Action-Feedback-Groundwork.md)).

## Non-goals (Epic 79)

- Full UI redesign or pilot pages (except `/config` hub proof).
- Storybook, NuGet design package, or CSS-in-JS framework.
- Schema-driven form system, async operation shell, charts, module dashboards, wizards, calendar UX.
- Backend, permission, or route/menu behavior changes.

## Proof usage

The `/config` landing page demonstrates `PageHeader`, `ManagementSection`, and `ManagementSummaryCard` (hub landing archetype). **Epics 80–84** applied layered primitives to the five redesign pilots. **Epics 85–88** consolidated guidance, swept state branches on non-pilot pages, documented action categories, and introduced `ActionFeedback` on Run/Schedule detail. **Epic 89** audited coherence; **Epic 90** fixed action-feedback persistence and adoption; **Epic 91** defined operation history groundwork; **Epic 92** QA/stabilization pass fixed not-found vs loading and notification detail error branches (see [Epic 92](../../02-decisions/Epic92-UI-Modernization-QA-and-Corrective-Stabilization.md)). Other pages may continue using legacy layout until a scoped epic touches them.
