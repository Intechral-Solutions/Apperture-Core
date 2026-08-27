# Management UI design system

Epic 79 introduces the first **practical** design-system foundation for the Blazor WASM Management UI (`Apperture.Management.Ui.Client`). It supports the layering model and archetypes defined in [Epic 78](../02-decisions/Epic78-Management-UI-Modernization-Groundwork.md) without redesigning major operator pages.

Related shell documentation: [control-panel-ui.md](control-panel-ui.md).

## Purpose

- Give implementers **shared primitives** for summary, detail, and advanced presentation layers.
- Align **status and attention** visuals with platform semantics (health, dashboard cards, permissions).
- Document **page-state** conventions (loading, empty, error, forbidden) on top of existing `PageMessage`.
- Preserve **backend truth**, permission omission, and drill-in query contracts from prior epics.

## Living document and working catalog

This file is the **canonical** design system guide for the Management UI. During the site-wide theme and layout overhaul, a working inventory lives in **[`.planning/management-ui/`](../../.planning/management-ui/README.md)**:

| Planning artifact | Role |
|-------------------|------|
| [pattern-catalog.md](../../.planning/management-ui/pattern-catalog.md) | Instances, maturity, and promotion actions |
| [catalog-schema.md](../../.planning/management-ui/catalog-schema.md) | Field definitions and 3+ route rule |
| [disclosure-audit.md](../../.planning/management-ui/disclosure-audit.md) | Progressive-disclosure candidates |

**Workflow:** catalog while touching pages → promote stable patterns into this document → face-lift instances. Do not maintain two normative spec documents.

## System layers

Implementers work top-down; tokens are the floor.

| Layer | Where | Examples |
|-------|--------|----------|
| **Tokens** | `app.css` (`:root`, `.dark`) | `--color-surface`, `--color-input-bg`, tint families, segmented active |
| **Primitives** | `management-ui-primitives.css` | `.ui-input`, `.ui-settings-card`, `.ui-status-pill--*` |
| **Components** | `Atoms/`, `Molecules/`, `Organisms/` | `PageHeader`, `SegmentedControl`, `ThemeCardGroup` |
| **Patterns** | Composed markup + primitives | Settings shell, hub summary grid, cooperative toolbar |
| **Page recipes** | Full routes | Account settings, entity hub, list+detail, dense authoring |

New work should **reuse** an existing layer before adding below it. Promote to the next layer only when the same composition appears on **three or more routes** (see catalog schema).

## Pattern maturity

| Maturity | Meaning | Implementer action |
|----------|---------|-------------------|
| **Reference** | Signed off for theme overhaul | Copy implementation |
| **Pilot** | Epic 79–97 structure | Harmonize tokens/primitives when touched |
| **Legacy** | Pre-pilot ad-hoc markup | Migrate when batch schedules the route |
| **Candidate** | Proposed abstraction | Document in catalog; do not spread until promoted |

**Current references:** `/account/preferences` (Appearance & regional), `/account/profile`, auth shell (`/login`), chrome primitives (toast, menus). See catalog for full matrix.

## Relationship to Epic 78

| Epic 78 deliverable | Epic 79 implementation |
|---------------------|-------------------------|
| Page archetypes | Components map to layers (see below); no archetype framework |
| Summary / detail / advanced layers | `ManagementSection`, `ManagementSummaryCard`, `ManagementAdvancedSection` |
| Permission / empty / error states | `PageMessage` + thin wrappers (`LoadingState`, `EmptyState`, …) |
| Drill-in rules | `DrillInLink`; URLs still built with `Epic33UiDisplayHelpers` |
| Pilot pages | `/config` hub proof; Integration Detail ([Epic 80](../../02-decisions/Epic80-Integration-Detail-Redesign-Pilot.md)); Run Detail ([Epic 81](../../02-decisions/Epic81-Run-Detail-Redesign-Pilot.md)); Control Center ([Epic 82](../../02-decisions/Epic82-Control-Center-Redesign-Pilot.md)); Schedules ([Epic 83](../../02-decisions/Epic83-Schedules-List-Detail-Redesign-Pilot.md)); Notification Definition Authoring ([Epic 84](../../02-decisions/Epic84-Notification-Definition-Authoring-Redesign-Pilot.md)); consolidation guidance ([Epic 85](../../02-decisions/Epic85-Management-UI-Pilot-Consolidation.md)); state-branch consistency sweep ([Epic 86](../../02-decisions/Epic86-Management-UI-State-Branch-Consistency-Sweep.md)); action surface groundwork ([Epic 87](../../02-decisions/Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md)); cooperative action feedback shell ([Epic 88](../../02-decisions/Epic88-Cooperative-Action-Feedback-Shell-Pilot.md)); other pilots per Epic 78 |

**Navigation / flow (Epic 98–101):** Route and menu audit, hub/detail model, shell domain labels (Epic 100), top-bar breadcrumbs (Epic 101) — [Epic 98](../02-decisions/Epic98-Management-UI-Navigation-and-Information-Flow-Audit.md), [Epic 101](../02-decisions/Epic101-Top-Bar-Breadcrumbs-and-Parent-Context-Links.md); shell contract in [control-panel-ui.md](control-panel-ui.md).

## Design principles

1. **Backend truth** — Components display API fields and capability gates; no inferred state.
2. **Permission omission** — Hide sections or cards when forbidden; do not show locked placeholders on dashboard cards (Epic 45).
3. **Composable, not generic** — Small Razor components with explicit parameters; no templating engine.
4. **Incremental adoption** — New and touched pages use primitives; legacy markup remains valid until pilot epics.
5. **Semantic tokens** — Visual roles use CSS variables in `wwwroot/css/app.css` (`:root` for light greige, `.dark` on `html` for cool black). Both themes are siblings: the same markup and `ui-*` classes; only token values change. Load `management-ui-primitives.css` after `app.css` in **Ui.Server** `_Layout.cshtml` (dev host) and WASM `wwwroot/index.html`.
6. **No theme branching in markup** — Never add a `light` class, `dark:` utilities, or C# theme switches. `ThemeService` toggles `html.dark` only.
7. **Catalog-driven evolution** — Record new or divergent UX in `.planning/management-ui/pattern-catalog.md`; fold stable rules back into this document.

## Theme (light and dark siblings)

| Concern | Light (`:root`) | Dark (`html.dark`) |
|---------|-----------------|---------------------|
| Page floor | `--color-bg` (warm greige) | `--color-bg` (cool black) |
| Cards | `--color-surface` | `--color-surface` |
| Inputs, menus, toasts | `--color-input-bg` → elevated white | `--color-input-bg` → page sunken |
| Segmented active chip | Raised white + accent inset | Accent wash + inset hairline |
| Status / callout fills | Tint families (sage, sand, teal) | Tint mixes from accent/status |
| Borders | Ink alpha `rgb(30 34 41 / …)` | White alpha on dark surfaces |

Pure `#FFFFFF` in light is reserved for **elevated** surfaces (inputs, nested list panels, menus, active segmented chip)—not page or card backgrounds.

Pre-boot: inline script in `index.html` / `_Layout.cshtml` reads `localStorage` and toggles `dark` before paint. `ThemeService` re-applies on OS `prefers-color-scheme` when preference is `System`.

## Component categories

| Category | Location | Role |
|----------|----------|------|
| **Atoms** | `Atoms/` | `StatusPill`, `AttentionPill`, `DrillInLink`, `ActionButton`, logo |
| **Molecules** | `Molecules/` | `ManagementSummaryCard`, `ManagementAdvancedSection`, page-state wrappers, `ActionFeedback`, `ManagementBreadcrumbs` |
| **Organisms** | `Organisms/` | `PageHeader`, `PageMessage`, `ManagementSection`, shell (`TopBar`), notification forms |
| **Semantics** | `Apperture.Management.Ui.Shared` | `ManagementUiSemantics`, `Epic33UiDisplayHelpers` (truth/links) |

### Existing components (retained)

- **`PageHeader`** — Page title, optional **Eyebrow** (page-level domain hint), subtitle, toolbar slot. Shell **top-bar breadcrumbs** ([Epic 101](../02-decisions/Epic101-Top-Bar-Breadcrumbs-and-Parent-Context-Links.md)) provide cross-page parent context; do not duplicate the page title in the trail.
- **`ManagementBreadcrumbs`** — Top-bar trail from `ManagementBreadcrumbResolver`; parent crumbs are links, current crumb is plain text (`ui-breadcrumbs*`).
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
| `ActionFeedback` | Detail (below header / summary) | Cooperative/user-triggered action result banner (Epic 88; Integration Detail Epic 96); uses `ui-feedback-banner` (Epic 97); defaults Title “Action”, Kind Info |
| `MetadataGrid` / `MetadataItem` | Detail / summary | Repeated label/value facts (Epic 93); optional `ThreeColumn` on summary bands |
| `SectionActions` | Section body | Save/cancel or routine button groups inside a `ManagementSection` (Epic 93) |

### Account area components (AUP-1)

Personal account surfaces. Routes and section metadata live in `AccountUiRoutes`.

| Component | Tier | Use when |
|-----------|------|----------|
| `AccountSectionNav` | Molecule | Tab-style navigation across the four account sections. Real links, `aria-current="page"` on the active one. Currently the only tab pattern in the shell |
| `AccountAvatar` | Atom | The signed-in operator's avatar. Renders the normalized photo, the chosen illustration, or initials while the session resolves. Size via `SizeClass` |
| `BuiltInAvatarGlyph` | Atom | Platform-owned avatar artwork as inline SVG, themed through `currentColor`. Decorative unless given a `Title` |
| `AvatarPicker` | Organism | Illustration `radiogroup` plus photo upload, preview, and confirmed removal |
| `TimeZoneSelector` | Molecule | Searchable timezone picker built from zones the host recognizes, with browser detection |

Conventions these establish, worth keeping when restyling:

- **Selection state is carried by border, background, accent fill/wash, and an assistive text marker.** Prefer `data-state="on"` with segment/theme-card styles plus an `sr-only` "Selected" label (or `aria-checked`/`aria-selected`). Theme swatch cards may also show a compact visible `Selected` badge that matches the design language. Do not use a `· Selected` prose suffix.
- **An image is never the only label.** Every avatar option has visible text beside it.
- **Avatar previews are decorative** (`alt=""`, `aria-hidden`) because the operator's name is always
  rendered next to them; alt text would be noise.
- **A channel or capability with no real control is stated in prose**, not rendered as a disabled input.
  A disabled checkbox implies an editable preference that exists.
- Validation stays inline and associated with its field (`aria-describedby`, `aria-invalid`,
  `role="alert"`); toasts are for successful saves.
- **Shared choosers:** use `ThemeCardGroup` for appearance, `SegmentedControl` for small exclusive sets
  (e.g. clock format), and `SearchableList` instead of native listbox/`<select size>`. These include
  keyboard and ARIA behavior.
- **No raw Tailwind palettes in `.razor`.** Prefer semantic utilities (`text-success`, `bg-message-warning`,
  `text-heading`) and `ui-*` classes. `NoRawColorUtilitiesTests` enforces this.

### Interaction CSS utilities (Epic 93)

Defined in `management-ui-primitives.css`:

| Class | Use |
|-------|-----|
| `ui-page-header__toolbar` | Applied by `PageHeader` toolbar slot — flex wrap, gap |
| `ui-toolbar-btn` | Page/section toolbar links and buttons (neutral) |
| `ui-inbox-list` | Personal notification inbox rows; pair with `SegmentedControl` filter toolbar |
| `ui-inline-code` | Inline monospace snippets in help text |
| `ui-code-block` | Scrollable preformatted JSON / payload display |
| `ui-toolbar-btn--danger` | Destructive toolbar actions (e.g. Integration Delete) — **after** positive/neutral actions |
| `ui-action-row` | Horizontal drill-ins, filters + refresh, link groups |
| `ui-section-actions` | Section-local button row (edit save/cancel, lifecycle actions) |
| `ui-metadata-grid` / `ui-metadata-grid--3` | Responsive metadata layout (used by `MetadataGrid`) |

### Settings and chooser primitives (theme overhaul)

Account surfaces established the reference patterns below. Use on personal settings and anywhere a similar **exclusive choice** or **searchable list** is needed.

| Primitive / component | CSS / component | Use when |
|---------------------|-----------------|----------|
| Settings page shell | `ui-settings-page`, `ui-settings-grid` | Multi-section personal or admin settings with optional aside |
| Settings card | `ui-settings-card`, `__header`, `__body`, `__footer` | Bordered section with title, help, actions |
| Saved / dirty chip | `ui-settings-status` + `data-state` | Toolbar or footer sync state (sage saved, sand dirty) |
| Info callout | `ui-settings-callout` | Non-blocking context (teal tint) |
| Text field | `ui-input` or `TextInput` | Single-line text; also `select.ui-input`, `textarea.ui-input`; `ui-input--inline` / `ui-input--narrow` for compact rows |
| Primary / secondary | `btn btn-primary`, `btn btn-secondary` | Form submit, Reset, empty-state CTAs |
| Theme cards | `ThemeCardGroup` + `ui-theme-card` | Few visual options with preview swatch |
| Segmented control | `SegmentedControl` + `ui-segmented` | 2–5 exclusive options (clock format, etc.) |
| Searchable list | `SearchableList` + `ui-searchable-list` | Timezone-scale lists; prefer over `<select size>` |
| Section nav tabs | `AccountSectionNav` | Real links + `aria-current`; only tab pattern in shell today |

### Chrome primitives (theme overhaul)

| Surface | Classes / components | Notes |
|---------|---------------------|-------|
| Toast | `ui-toast`, `toast-strip-*` | Elevated panel; assertive vs polite regions in `ToastHost` |
| Menus | `ui-menu-panel`, `ui-menu-item` | Theme, account, notification footers |
| Notification panel | `ui-menu-panel` + `ui-personal-notification-menu__*` | Anchored to bell |
| Auth layout | `AuthLayout` (`bg-default`) + `ui-section-panel` on login card | Greige floor, white panel |
| Rail / topbar | `ui-nav-rail`, `ui-topbar`, `--color-rail-bg`, `--color-topbar-bg` | Badge cutout uses topbar bg |

## Progressive disclosure

Hide **secondary, forensic, or rare** information—not primary actions or permission-gated work.

| Level | Mechanism | Examples |
|-------|-----------|----------|
| **Always visible** | Page header, summary band, blocking status | Integration readiness blockers, run actionability |
| **Section** | `ManagementSection`, settings cards | Editable config, subscription lists |
| **Advanced** | `ManagementAdvancedSection`, `TechnicalDetails`, `Disclosure` | Raw ids, payloads, roles/links on profile |
| **Drill-in route** | Separate page | Schedule diagnostics, notification diagnostics |

Audit candidates: [disclosure-audit.md](../../.planning/management-ui/disclosure-audit.md). Prefer restructuring overloaded pages into sections before adding collapses everywhere.

## Anti-patterns

| Do not | Use instead |
|--------|-------------|
| `bg-body`, `text-surface` ad-hoc buttons | `btn btn-primary`, `ui-toolbar-btn` |
| `border-default bg-surface` on inputs | `ui-input`, `TextInput` |
| Raw Tailwind palettes, `dark:`, hex in Razor | Semantic tokens; `NoRawColorUtilitiesTests` |
| `rgba(255,255,255,…)` in component CSS | Theme tokens in `:root` / `.dark` only |
| `· Selected` prose suffix | `data-state="on"`, visible badge, `sr-only` |
| Disabled checkbox for non-existent preference | Prose explaining unavailable capability |
| Toast for field validation errors | Inline `aria-describedby` + `role="alert"` |

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
| Success | Healthy, completed | Sage tint fill + line + text |
| Warning | Degraded, advisory | Sand tint fill + line + text |
| Error / Blocking | Failed, blocked | Error-light fill + error-dark text |
| Pending | In progress, queued | Teal tint fill + line + text |
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
- **Text inputs:** `ui-input` or `TextInput`; **select/textarea:** add class `ui-input` to native elements.
- Primary actions: `btn btn-primary` / `btn-secondary` or `ActionButton` for single-shot cooperative feedback.
- Destructive: `ActionButtonVariant.Danger`, `btn-danger`, or `ui-toolbar-btn--danger` (toolbar, last position).
- Section labels: `text-xs text-muted` for field labels; `ui-settings-card__title` or `text-heading` for section titles.
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
| **Async/cooperative command** | Work may continue after click; eligibility matters | **`ActionFeedback`** at page top (below header) **or** toast on dense hubs; preserve domain copy | Run Detail cancel/retry/park/resume; Schedule Detail enable/disable/trigger; Blocked Work; Module Detail |
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

- Keep existing **`confirm()`** / `ConfirmDeleteAsync` flows and permission gates unchanged.
- **Placement:** In `PageHeader` toolbars, put destructive actions **last** (after Refresh and lifecycle commands). Section-level removes (bindings, settings rows) stay in-context with existing `bg-destructive/20` compact styling.
- **Toolbar style:** `ui-toolbar-btn ui-toolbar-btn--danger` for page-level delete (Integration Detail pilot).
- **Do not** change severity semantics (403/409 cooperative copy remains Info `ActionFeedback` until a dedicated UX epic).
- Show **pre-save warnings** in place (e.g. role permission replace banner) — do not collapse into a toast only.

## Interaction primitives (Epic 93)

Hardens repeated interaction markup from Epics 80–92 before visual polish or operation history.

### PageHeader actions

- Use **`PageHeader` `Toolbar`** for page-scoped commands: Back, Refresh, cooperative controls, destructive delete.
- Prefer **`ui-toolbar-btn`** on `<a>` and `<button>` in toolbars on modernized pilots.
- **Refresh** stays in toolbar; does not replace `ActionFeedback` for cooperative results.
- **Poll** (Run Detail) remains a checkbox in toolbar — not a `ui-toolbar-btn`.

### Section actions

- Use **`SectionActions`** or **`ui-section-actions`** for button groups inside a section (Schedule edit Save/Cancel, Module lifecycle row).
- Use **`ui-action-row`** for drill-in link clusters (Schedule Links section, Module links).
- Section **table row** actions (Blocked Work Retry) remain per-row — do not move to page toolbar.

### Metadata display

- Use **`MetadataGrid`** + **`MetadataItem`** when showing a block of label/value facts (Schedule configuration read-only, Module state, Run summary band timing fields).
- Use **`Monospace="true"`** on ids, recurrence, and technical tokens.
- **Do not** replace notification definition `<dl>` grids or Integration summary flex bands — different layout intent.
- **Do not** fetch data inside metadata components.

## Visual density and polish (Epic 97)

First visual refinement pass after structural modernization (Epics 78–96). **CSS-first** in `wwwroot/css/management-ui-primitives.css`; behavior unchanged.

| Utility | Role |
|---------|------|
| `ui-page-stack` | Vertical page rhythm (1.25–1.5rem gap) |
| `ui-page-header` | Title block with bottom rule; pairs with `ui-page-header__toolbar` |
| `ui-section-panel` | Detail/summary panels; `data-ui-layer="Summary"` for emphasis |
| `ui-section-panel__body` | Separates header from content with subtle top rule |
| `ui-summary-card` / `ui-summary-card-grid` | Quick-link cards with shadow and hover affordance |
| `ui-advanced-section` | Dashed, muted collapse for secondary evidence |
| `ui-detail-block` | Titled inline panels when `ManagementSection` is heavy (Module Detail) |
| `ui-data-table` | Operational tables (lists, recent activity, blocked work) |
| `ui-metadata-item` | Label/value cell in metadata grids |
| `ui-feedback-banner` / `ui-state-panel` | Action and load/empty/error/forbidden message padding |

See [Epic 97](../02-decisions/Epic97-Management-UI-Visual-Density-and-Polish-Pass.md). Integration Detail ActionFeedback: [Epic 96](../02-decisions/Epic96-Integration-Detail-Action-Feedback-Alignment.md).

### When not to abstract (Epic 93)

- ~~Integration Detail **Activate** primary CTA (`bg-body`)~~ — **migrate** to `btn btn-primary` during overhaul; do not add `bg-body` utilities.
- Wizard step buttons, inline table actions, `ActionButton` connection test.
- Toast-only Integration Detail mutations.
- Full metadata abstraction on Run Detail investigation sections (many conditional fields).

See [Epic 93 — Interaction primitive hardening](../02-decisions/Epic93-Management-UI-Interaction-Primitive-Hardening.md).

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

Epic 88 introduced **`ActionFeedback`** as the first reusable **presentation** layer for cooperative results. [Epic 96](../02-decisions/Epic96-Integration-Detail-Action-Feedback-Alignment.md) adopts it on **Integration Detail** for stay-on-page mutations (banner persists across `LoadAsync(preserveActionFeedback: true)`; toasts retained). [Epic 91](../02-decisions/Epic91-Operation-History-and-Action-Feedback-Groundwork.md) defines operation history semantics, existing sources of truth, and UI placement before any global shell. Until then:

1. Keep action copy and eligibility logic **page-local** (`_actionMessage`, handlers unchanged).
2. Reuse **`ActionFeedback`** + **`ActionButton`** prior art before adding global services.
3. Prefer **entity-local recent activity** from audit/run timeline before a new operation table.
4. Preserve **403/409** messaging contracts from Run Detail and Schedule Detail.

See [Epic 87](../02-decisions/Epic87-Management-UI-Action-Surface-and-Operation-Feedback-Groundwork.md), [Epic 88](../02-decisions/Epic88-Cooperative-Action-Feedback-Shell-Pilot.md), and [Epic 91](../02-decisions/Epic91-Operation-History-and-Action-Feedback-Groundwork.md).

### Entity-local recent activity panels (Epic 94)

First implementation slice for operation-history usefulness **without** a global operation registry.

| Rule | Guidance |
|------|----------|
| **Data sources** | Only existing APIs (`GetRunsAsync` filters, `GetAuditTimelineAsync`, entity-scoped notification instance lists, etc.). Do not invent or infer operations. |
| **Scope** | Entity-local only (integration module id for audit, `integrationId` / `moduleId` for runs). Omit sources the user cannot read (403 → omit audit, not fake rows). |
| **Bounds** | Default 7-day lookback, ~5 rows per source, ~10 after client merge. |
| **Presentation** | `ManagementSection` “Recent activity” + `EntityRecentActivityPanel` (or equivalent table): timestamp, source kind, summary, actor when present, status pill, `DrillInLink`. |
| **Errors** | Section-level `PageMessage` Error or `EmptyState`; never block the rest of the entity page. |
| **Drill-ins** | Row links to run/audit/notification detail; footer links to filtered list routes **only when the list route supports the filter**. Use `Epic33UiDisplayHelpers.GetRunsListModuleFilterHref` / `GetRunsListIntegrationFilterHref` for runs list drill-ins ([Epic 95](../02-decisions/Epic95-Recent-Activity-Filter-and-Drill-In-Completion.md)). |

Mapping and merge logic live in `EntityRecentActivityComposer` (`Apperture.Management.Ui.Shared`).

## Drill-in conventions

- Build URLs with **`Epic33UiDisplayHelpers`** (audit, runs, modules, runs-list filters) or documented query params (Epic 47, Epic 78 §7). Runs list supports `integrationId`, `moduleId`, `correlationKey`, `status`, and `createdFrom` query params when the API filter exists.
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

- **Async operation shell:** `ActionFeedback` on cooperative surfaces and Integration Detail hub mutations ([Epic 96](../../02-decisions/Epic96-Integration-Detail-Action-Feedback-Alignment.md)); entity-local recent activity panels ([Epic 94](../../02-decisions/Epic94-Entity-Local-Recent-Activity-Panels.md)); interaction primitives ([Epic 93](../../02-decisions/Epic93-Management-UI-Interaction-Primitive-Hardening.md)) — operation **registry** and backend tracking remain deferred.
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
- Appearance precedence (AUP-1): **account preference → this browser's local value → platform default
  (`System`)**. The operating system setting is not a separate tier — it is what `System` resolves to at
  paint time. `AppearancePreferenceCoordinator` applies the account preference through `ThemeService`,
  which also mirrors it into `localStorage`; that mirror is what the host page's pre-WebAssembly boot
  script reads, and therefore what prevents a flash of the wrong theme on the next visit. Both the
  header `ThemeMenu` and `/account/preferences` persist to the account. See
  [`AppearancePreferenceResolution`](../audits/aup1-account-profile-personal-preferences.md) §8.

## CSS and tokens

| File | Contents |
|------|----------|
| `wwwroot/css/app.css` | Tailwind v4 build + semantic theme tokens in `:root` and `.dark` |
| `wwwroot/css/management-ui-primitives.css` | `ui-*` primitives referencing theme variables |

### Token families (extend here, not in Razor)

| Family | Variables | Role |
|--------|-----------|------|
| Surfaces | `--color-bg`, `--color-bg-sunken`, `--color-surface`, `--color-surface-elevated`, `--color-surface-muted`, `--color-surface-header`, `--color-disclosure-bg`, `--color-disclosure-border`, `--color-table-header-bg`, `--color-row-hover-bg`, `--color-metadata-bg`, `--color-metadata-label-bg` | Canvas, cards, recess, headers, tables, metadata pills |
| Input | `--color-input-bg` | Form fields and nested panels (theme inversion) |
| Text | `--color-text-heading` … `--color-text-subtle`, `--color-text` | Body copy hierarchy |
| Accent | `--color-accent`, `--color-accent-wash`, `--color-accent-line`, `--color-accent-ring` | Brand actions and selection |
| Borders | `--color-border-subtle`, `--color-border`, `--color-border-strong` | Ink-alpha light / white-alpha dark |
| Tints | `--color-tint-teal|sand|sage` + `-line` + `-text` | Callouts, status chips, dirty/saved |
| Segmented | `--color-segmented-track`, `--color-segmented-active-*`, `--shadow-segmented-active` | Exclusive option chips |
| Chrome | `--color-rail-bg`, `--color-topbar-bg`, `--color-hamburger-line`, `--color-hover-overlay` | Shell |
| Destructive | `--color-destructive`, `--color-destructive-fg` | Danger toolbar and buttons |
| Buttons | `--color-btn-primary-*`, `--color-btn-secondary-*`, `--color-btn-primary-disabled-*` | `.btn-*` utilities |

Do not add one-off hex colors in Razor; extend semantic tokens in `app.css` when a new **role** is needed in both themes.

## Theme overhaul status

Tracked in [pattern-catalog.md](../../.planning/management-ui/pattern-catalog.md). Summary:

| Phase | Scope | Status |
|-------|--------|--------|
| Light/dark sibling tokens | `app.css`, primitives tokenization | Done |
| Chrome + Profile + Dashboard | Toast, menus, auth, profile, dashboard empty CTAs | Done |
| Shared form molecules | `ProcedureParameterForm`, `NotificationDefinitionAuthoringForm`, `ConnectorConnectionFieldInputs`, `PlatformSettingsEditor` / `PlatformSettingRow` | Done |
| Account Security + Notifications + Login polish | Account security/notifications, personal inbox, auth sign-in | Done |
| Integrations + Execute | Integrations list/detail, create wizard, execute launch | Done |
| Runs, Schedules, Control | Runs list/detail, schedule create/detail, control center, audit, blocked work | Done |
| Config + governance + attention | Users/roles/catalog, user/role detail, notification hub/lists, operational teams | Done |
| Modules, connections, runtimes, demo | Module list, connections CRUD, connector catalog, shutdown requests, demo customers | Done |
| Auth, contacts, notification detail, integration sub-pages | Auth/Entra/sign-in, managed contacts, definition/instance/delivery/diagnostics, integration notifications/settings/schedule diagnostics | Done |
| Final sweep (shared components) | `IntegrationConfigurationOverview`, `RunOperationItemizationPanel`, `WizardFlowPanel`, `NotificationPipelineCutoverStatusCard`, post-create prompt, row hover tokens | Done |

All operator-facing routes and shared components are on design-system primitives. When adding pages, reuse the
primitives above; add a new semantic token only when a role is missing in **both** themes.

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

The `/config` landing page demonstrates `PageHeader`, `ManagementSection`, and `ManagementSummaryCard` (hub landing archetype). **Epics 80–84** applied layered primitives to the five redesign pilots. **Epics 85–88** consolidated guidance, swept state branches on non-pilot pages, documented action categories, and introduced `ActionFeedback` on Run/Schedule detail. **Epic 89** audited coherence; **Epic 90** fixed action-feedback persistence; **Epic 91** defined operation history groundwork; **Epic 92** fixed state/reload edge cases; **Epic 93** hardened toolbar, metadata, and section-action primitives (see [Epic 93](../../02-decisions/Epic93-Management-UI-Interaction-Primitive-Hardening.md)). Other pages may continue using legacy layout until a scoped epic touches them.
