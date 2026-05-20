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
| Pilot pages | `/config` hub proof; Integration Detail ([Epic 80](../../02-decisions/Epic80-Integration-Detail-Redesign-Pilot.md)); Run Detail ([Epic 81](../../02-decisions/Epic81-Run-Detail-Redesign-Pilot.md)); other pilots per Epic 78 |

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
| **Molecules** | `Molecules/` | `ManagementSummaryCard`, `ManagementAdvancedSection`, page-state wrappers |
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
- Destructive: `ActionButtonVariant.Danger` or `btn-danger`.
- Section labels: `text-xs text-muted` for field labels; `text-heading` for section titles inside `ManagementSection`.

## Drill-in conventions

- Build URLs with **`Epic33UiDisplayHelpers`** (audit, runs, modules) or documented query params (Epic 47, Epic 78 §7).
- Present links with **`DrillInLink`** or `class="ui-drill-in-link"`.
- Label destination explicitly (“View blocked work”, not “Click here”).
- Omit link when filter unsupported or permission missing.

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
- **Epic 80+ pilots** — Control center, run detail ([Epic 81](../../02-decisions/Epic81-Run-Detail-Redesign-Pilot.md)), integration detail ([Epic 80](../../02-decisions/Epic80-Integration-Detail-Redesign-Pilot.md)), and others per Epic 78 §9.

## Non-goals (Epic 79)

- Full UI redesign or pilot pages (except `/config` hub proof).
- Storybook, NuGet design package, or CSS-in-JS framework.
- Schema-driven form system, async operation shell, charts, module dashboards, wizards, calendar UX.
- Backend, permission, or route/menu behavior changes.

## Proof usage

The `/config` landing page demonstrates `PageHeader`, `ManagementSection`, and `ManagementSummaryCard` (hub landing archetype). **Epic 80** applied the same primitives to [Integration Detail](../../02-decisions/Epic80-Integration-Detail-Redesign-Pilot.md) (`/integrations/{IntegrationId}`). **Epic 81** applied them to [Run Detail](../../02-decisions/Epic81-Run-Detail-Redesign-Pilot.md) (`/runs/{RunId}`). Other pages may continue using legacy markup until their pilot epic.
