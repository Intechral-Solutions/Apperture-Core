# Pattern catalog

**Last updated:** theme overhaul kickoff (Appearance reference, Phase A/B chrome + Profile + Dashboard).

Legend: **R** reference · **P** pilot · **L** legacy · **C** candidate

---

## Tokens (`app.css`)

| ID | Name | Reference | Mat. | Action | Notes |
|----|------|-----------|------|--------|-------|
| `token.theme.sibling` | Light/dark sibling themes | `:root` + `html.dark` in `app.css` | R | none | No `light` class; no `dark:` in Razor |
| `token.surface.canvas` | Page floor | `--color-bg` | R | none | Greige light / cool black dark |
| `token.surface.card` | Cards | `--color-surface` | R | none | |
| `token.surface.elevated` | Inputs, menus, toasts | `--color-surface-elevated` / `--color-input-bg` | R | none | Inversion: white inputs in light, sunken in dark |
| `token.surface.header` | List headers, card footers | `--color-surface-header` | R | none | |
| `token.tint.teal` | Info / callouts / pending | `--color-tint-teal*` | R | none | |
| `token.tint.sand` | Warning / dirty | `--color-tint-sand*` | R | none | |
| `token.tint.sage` | Success / saved | `--color-tint-sage*` | R | none | |
| `token.segmented` | Segmented track + active chip | `--color-segmented-*`, `--shadow-segmented-active` | R | none | Raised white chip light; wash+inset dark |
| `token.destructive` | Danger actions | `--color-destructive`, `--color-destructive-fg` | R | none | Maps to error |
| `token.chrome.hamburger` | Hamburger lines | `--color-hamburger-line` | R | none | |

---

## Primitives (`management-ui-primitives.css`)

| ID | Name | Reference | Mat. | Action | Instances / variation |
|----|------|-----------|------|--------|---------------------|
| `primitive.input` | Text input | `.ui-input`, `TextInput.razor` | R | none | `select`/`textarea`; `--inline` / `--narrow` for settings rows |
| `primitive.button.primary` | Primary button | `.btn.btn-primary` in `app.css` | R | reuse | Replace ad-hoc `bg-body text-surface` (Integrations, Connections) |
| `primitive.button.secondary` | Secondary button | `.btn.btn-secondary` | R | reuse | Settings Reset, dashboard empty CTAs |
| `primitive.button.toolbar` | Toolbar neutral | `.ui-toolbar-btn` | P | reuse | PageHeader toolbars |
| `primitive.button.toolbar-danger` | Toolbar destructive | `.ui-toolbar-btn--danger` | P | reuse | Integration Delete |
| `primitive.segmented` | Exclusive small set | `.ui-segmented*` | R | reuse | Clock format, future filter modes |
| `primitive.chooser.theme-card` | Theme picker cards | `.ui-theme-card*` | R | reuse | Appearance only today |
| `primitive.chooser.searchable-list` | Searchable list panel | `.ui-searchable-list*` | R | reuse | Timezone; candidate for large selects |
| `primitive.pill.status` | Status chip | `.ui-status-pill--*` | R | none | Tint fills (Phase A) |
| `primitive.pill.attention` | Attention chip | `.ui-attention-pill--*` | R | none | Dashboard cards |
| `primitive.settings.card` | Settings section card | `.ui-settings-card*` | R | reuse | Account, Appearance reference |
| `primitive.settings.status` | Saved/dirty chip | `.ui-settings-status` | R | reuse | Preferences toolbar |
| `primitive.settings.callout` | Teal info callout | `.ui-settings-callout` | R | reuse | Timestamps callout |
| `primitive.toast` | Toast surface | `.ui-toast` | R | reuse | `ToastHost` |
| `primitive.menu.panel` | Dropdown panel | `.ui-menu-panel`, `.ui-menu-item` | R | reuse | Theme, account, notification menus |
| `primitive.section.panel` | Generic bordered panel | `.ui-section-panel` | P | reuse | Login card, config sections |
| `primitive.inbox.list` | Notification inbox | `.ui-inbox-list`, `.ui-inbox-toolbar` | R | none | `/me/notifications` |
| `primitive.table.data` | Operational table | `.ui-data-table` | P | reuse | Runs, schedules, control lists |
| `primitive.disclosure.advanced` | Forensic collapse | `.ui-advanced-section` | P | none | Stays `--color-bg-sunken` (recessed) |
| `primitive.disclosure.details` | Inline disclosure | `.ui-disclosure`, `Disclosure.razor` | P | tokenize | Profile roles block |

**Tech debt (primitives):** duplicate CSS blocks ~L22–876 vs ~L1021+ in `management-ui-primitives.css` — do not merge until pattern set stabilizes.

---

## Components (Razor)

| ID | Name | Reference | Mat. | Action | Instances / variation |
|----|------|-----------|------|--------|---------------------|
| `component.page-header` | Page title block | `PageHeader.razor` | P | reuse | All modernized pages |
| `component.management-section` | Detail panel | `ManagementSection.razor` | P | reuse | |
| `component.summary-card` | Hub tile | `ManagementSummaryCard.razor` | P | reuse | Dashboard, config hub |
| `component.metadata-grid` | Label/value facts | `MetadataGrid.razor` | P | reuse | Run/schedule detail |
| `component.action-feedback` | Cooperative result | `ActionFeedback.razor` | P | reuse | Run/schedule detail |
| `component.theme-card-group` | Appearance radios | `ThemeCardGroup.razor` | R | reuse | |
| `component.segmented-control` | Segmented radios | `SegmentedControl.razor` | R | reuse | |
| `component.timezone-selector` | Timezone picker | `TimeZoneSelector.razor` | R | reuse | |
| `component.avatar-picker` | Avatar + upload | `AvatarPicker.razor` | R | reuse | Profile |
| `component.procedure-param-form` | Execution parameters | `ProcedureParameterForm.razor` | **R** | none | `/execute`; `ui-input` on all field types |
| `component.notification-authoring` | Definition editor | `NotificationDefinitionAuthoringForm.razor` | **P** | none | `ui-section-panel`, `ui-input`, `ui-data-table`, `btn` |
| `component.platform-settings` | Settings rows | `PlatformSettingsEditor.razor`, `PlatformSettingRow.razor` | **P** | none | `ui-section-panel` + `ui-input--inline` row editors |
| `component.local-login` | Local auth form | `LocalLoginFormSection.razor` | R | none | Login paths |
| `component.wizard-flow` | Module wizard | `WizardFlowPanel.razor` | P | reuse | Module/integration create |

---

## Patterns (composed)

| ID | Name | Reference | Mat. | Action | Instances / variation |
|----|------|-----------|------|--------|---------------------|
| `pattern.settings.shell` | Account settings layout | `ui-settings-page` + `ui-settings-grid` + `AccountSectionNav` | R | reuse | Profile, Security, Notifications, Preferences |
| `pattern.settings.footer` | Save/reset strip | `ui-settings-card__footer` | R | reuse | Preferences regional card |
| `pattern.auth.shell` | Sign-in layout | `AuthLayout` + `ui-section-panel` | R | reuse | `/login`, `/login/local` |
| `pattern.chrome.shell` | App chrome | `MainLayout`, `TopBar`, `NavRail`, menus | P | reuse | ToastHost, notification panel updated |
| `pattern.hub.summary-grid` | Quick links | `ui-summary-card-grid` | P | reuse | Dashboard, config landing |
| `pattern.list.filters` | Filter toolbar + table | ad-hoc on IntegrationsList | L | new-component | 8+ raw `<select>`; candidate `FilterBar` molecule |
| `pattern.entity.hub` | Integration-style hub | `IntegrationDetail.razor` | P | restructure | 17 legacy bordered pockets; inline schedule form |
| `pattern.list.detail` | Schedules, runs | `SchedulesList` + `ScheduleDetail` | P | reuse | ScheduleCreate still legacy inputs |
| `pattern.authoring.dense` | Notification definition | `NotificationDefinitionAuthoringForm` | L | restructure | |
| `pattern.empty.forbidden` | Page states | `LoadingState`, `EmptyState`, `ForbiddenState`, `ErrorState` | P | reuse | Dashboard empty uses `btn-secondary` |
| `pattern.cooperative.toolbar` | Run/schedule actions | `PageHeader` toolbar + `ActionFeedback` | P | reuse | |

---

## Page recipes (routes)

| Route / area | Recipe | Mat. | Action | Notes |
|--------------|--------|------|--------|-------|
| `/account/preferences` | Settings shell + choosers | **R** | none | Theme overhaul reference |
| `/account/profile` | Settings shell + identity | **R** | none | AvatarPicker harmonized |
| `/account/security`, `/account/notifications` | Settings shell | **R** | none | Security + notification prefs aligned with account reference |
| `/me/notifications` | Personal inbox | **R** | none | `ui-inbox-list`, `SegmentedControl`, `btn` |
| `/dashboard` | Hub + summary cards | P | none | Pills via Phase A |
| `/login`, `/login/local` | Auth shell | **R** | none | `ui-section-panel`, `EntraSignInButton` → `btn btn-secondary` |
| `/integrations`, `/integrations/{id}` | List filters + entity hub | L/P | restructure | Highest risk |
| `/execute` | Procedure form + sections | L | restructure | |
| `/runs`, `/runs/{id}` | List + execution detail | P | reuse | Polish only |
| `/control/center`, `/control/blocked-work` | Operational console | P | reuse | |
| `/config/**` | Hub + hybrid lists | P/L | reuse | Governance lists legacy |
| `/attention/**` | Legacy lists | L | restructure | |
| Redirects/stubs | — | — | skip | AccountRedirect, OperationStart, etc. |

---

## Anti-patterns (catalog as “do not”)

| ID | Symptom | Fix |
|----|---------|-----|
| `anti.bg-body` | `bg-body text-surface` unstyled buttons | `btn btn-primary` or `ui-toolbar-btn` |
| `anti.surface-as-input` | `border-default bg-surface` on inputs | `ui-input` / `TextInput` |
| `anti.raw-palette` | `bg-slate-*`, `dark:`, hex in Razor | Semantic tokens; `NoRawColorUtilitiesTests` |
| `anti.white-alpha-light` | `rgba(255,255,255,...)` outside `.dark` tokens | Theme tokens only |
| `anti.prose-selected` | `· Selected` suffix | `data-state="on"` + `sr-only` / badge |

---

## Batch queue (suggested)

1. ~~Phase A CSS + chrome + Profile + Dashboard~~ (done)
2. ~~Shared forms: ProcedureParameterForm, NotificationDefinitionAuthoringForm, ConnectorConnectionFieldInputs, PlatformSettingsEditor~~ (done)
3. ~~Account Security + Notifications + Login polish~~ (done)
4. Integrations list/detail + Execute
5. Runs, Schedules, Control
6. Config notifications + governance + contacts
7. Modules, connections, runtimes, demo

Update this table as batches complete.
