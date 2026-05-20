# Epic 78 — Management UI modernization groundwork

## Status

Accepted — documentation only (May 2026).

## 1. Problem statement

The Apperture Management UI (Blazor WASM, `Apperture.Management.Ui.Client`) has grown feature-rich across operations, automation, integrations, connections, and configuration. Multiple epics (33, 35, 43, 45–47, 76–77, and others) added **truthful** operator surfaces, cross-navigation, and permission-aware behavior, but did so incrementally.

Operators and implementers now face:

- **Scan density** — Important pages (control center, run detail, integration detail, notification authoring) stack many sections at the same visual priority with limited summary/detail/advanced separation.
- **Inconsistent page shapes** — Some routes use `PageHeader` + `PageMessage`; others use ad-hoc headings and bordered cards; legacy start routes coexist with `/execute`.
- **Navigation vs. documentation drift** — Shell landing, menu labels, and architecture docs do not always agree (see validation notes).
- **No shared UI contract** — Partial atomic folders (`Atoms/`, `Molecules/`, `Organisms/`) exist, but there is no documented page archetype or layering model to guide the next redesign wave.

This epic records the **current** UI inventory and defines **documentation-only** rules (archetypes, layering, drill-ins, permission states, pilots) so later epics can modernize pages without redesigning the platform or inventing unused abstractions.

## 2. Scope and non-goals

### In scope

- Audit inventory of Management UI routes and domains.
- Page **archetype** catalog tied to real routes.
- **Summary / detail / advanced** layering model with examples.
- **Drill-in** pattern rules aligned with Epics 33 and 47.
- Permission, empty, and forbidden-state guidance.
- Pilot page recommendations for a follow-on redesign epic.
- Future-epic dependency map and extension seams to preserve.

### Non-goals (this epic and explicit deferrals)

- No page redesign, visual refresh, or Tailwind/token overhaul.
- No shared component library, design tokens package, or Storybook.
- No new backend APIs, query filters, or behavior changes.
- No schema-driven form framework beyond documenting existing `ProcedureParameterForm` usage.
- No module-contributed dashboard panels, chart browsing, guided wizards, visual workflow builders, or calendar/temporal UX (listed as future epics only).
- No implementation of dashboard personalization or deferred dashboard management routes.

## 3. Existing groundwork / related docs

| Artifact | Relevance to Epic 78 |
|----------|----------------------|
| [control-panel-ui.md](../01-architecture/control-panel-ui.md) | Shell domains, operator console baseline; **note landing drift** (see §12). |
| [Epic33-Management-UI-Deepening.md](Epic33-Management-UI-Deepening.md) | Cross-navigation matrix; blocked-work semantics; global run-cycle truthfulness. |
| [Epic45-Dashboard-Summary-Foundations.md](Epic45-Dashboard-Summary-Foundations.md) | Dashboard card contract; permission omission; partial-load placeholder. |
| [Epic46-Dashboard-Card-Expansion.md](Epic46-Dashboard-Card-Expansion.md) | Additional cards and drill-ins. |
| [Epic47-Dashboard-Drill-in-Refinement.md](Epic47-Dashboard-Drill-in-Refinement.md) | Contextual drill-in matrix; query-param alignment for audit and runs. |
| [Epic43-Management-UI-sign-in-gate-and-auth-ui-policy.md](Epic43-Management-UI-sign-in-gate-and-auth-ui-policy.md) | `AuthRouteGate`, public vs protected routes. |
| [Epic35-Run-Inspection-Forensics.md](Epic35-Run-Inspection-Forensics.md) | Run detail investigation section and origin conventions. |
| `ControlPanelMenuDefinitions.cs` | Canonical menu domains and hrefs (`Apperture.Management.Ui.Shared`). |
| `Epic33UiDisplayHelpers` | Testable display/drill-in helpers for operator truthfulness. |
| `IDashboardCardProvider` / `GET /api/dashboard/default` | Backend summary layer for dashboard home (not Blazor presentation layers). |

**Implementation roots (audit sources):**

- Pages: `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/`
- Shell: `Layout/MainLayout.razor`, `Layout/ConfigLayout.razor`, `Organisms/ControlPanel.razor`
- Shared menu: `src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/ControlPanelMenuDefinitions.cs`

## 4. UI inventory by domain and route

Domains follow the Control Panel menu (`ControlPanelMenuDefinitions`) plus shell/auth routes not always listed in the menu.

**Legend — Archetype (short):** CC = Operator console; DH = Summary hub (dashboard); HL = Hub landing; FL = Filtered list; ED = Entity detail; CR = Create/new; EX = Start/execute; AU = Authoring; DX = Diagnostics; AUTH = Auth/session; RD = Redirect; DEF = Deferred/placeholder; DEMO = Demo; RT = Runtime admin.

**Legend — Menu:** ✓ = in default menu; (config) = reachable via Config hub or child links; — = not in menu.

### Shell and auth

| Route | Page | Archetype | Menu | Primary purpose | Notes |
|-------|------|-----------|------|-----------------|-------|
| `/` | `OperatorLanding.razor` | RD | — | Redirect to platform dashboard | Redirects to `/dashboard` (not `/control/center`). |
| `/dashboard` | `DashboardHome.razor` | DH | ✓ Dashboard | Permission-aware summary cards | `GET /api/dashboard/default`; empty vs failure copy (Epic 45). |
| `/dashboard/manage`, `/add`, `/delete`, `/layout` | Dashboard deferred pages | DEF | ✓ (labeled deferred) | Placeholders | Menu text marks deferred. |
| `/login`, `/login/local` | `Login.razor`, `LoginLocal.razor` | AUTH | — | Sign-in chooser / local recovery | Public; `AuthRouteGate`. |
| `/account`, `/preferences` | `Account.razor`, `Preferences.razor` | ED / HL | — | Session account, theme prefs | User menu entry. |

### Operations domain

| Route | Page | Archetype | Menu | Primary purpose | Notes |
|-------|------|-----------|------|-----------------|-------|
| `/control/center` | `ControlCenter.razor` | CC | ✓ | Unified targets, activity, attention, optional platform health | High section count; capability-gated health block. |
| `/control/blocked-work` | `BlockedWork.razor` | FL | ✓ | Blocked deferred work; workflow retry when eligible | Operation items inspection-only (Epic 33). |
| `/control/audit` | `AuditTimeline.razor` | FL | ✓ | Audit query timeline | Query: `moduleId`, `scheduleId`, `runId`, `hours` (Epic 47). |
| `/execute` | `ExecutionStart.razor` | EX | ✓ | Unified operation/workflow start | Query: `kind`, `integrationId`. |
| `/operations/start` | `OperationStart.razor` | EX | ✓ Legacy | Operation-centric start | Metadata-driven inputs; superseded in menu intent by `/execute`. |
| `/workflows/start` | `WorkflowStart.razor` | EX | ✓ Legacy | Minimal workflow start | Legacy parallel to `/execute`. |
| `/tools/explorer` | (menu href) | — | ✓ | API Explorer | **Menu entry present**; no matching `@page` in Ui.Client at audit time — verify host/static route. |
| `/demo/nested-nav{/*}` | `NestedNavDemo.razor` | DEMO | ✓ | Nested nav behavior demo | Not operator product surface. |
| `/attention/operational-teams`, `/attention/contact-groups` | `AttentionContactGroups.razor` | FL | ✓ | Contact group list | Alias routes for same page. |
| `/attention/contact-groups/{Id}`, `.../operational-teams/{Id}` | `AttentionContactGroupDetail.razor` | ED | — | Group detail and members | |
| `/attention/routing-defaults` | `AttentionRoutingDefaults.razor` | AU | ✓ | Instance attention routing defaults | |

### Automation domain

| Route | Page | Archetype | Menu | Primary purpose | Notes |
|-------|------|-----------|------|-----------------|-------|
| `/runs` | `RunsList.razor` | FL | ✓ History | Run history list | Query: `status`, `createdFrom`, `correlationKey`, `integrationId`. |
| `/runs/{RunId}` | `RunDetail.razor` | ED | — | Run lifecycle, steps, controls, forensics | Very large page; cooperative action messaging; poll toggle. |
| `/schedules` | `SchedulesList.razor` | FL | ✓ Scheduling | Schedule definitions list | No degraded-only server filter (Epic 47 deferred). |
| `/schedules/{ScheduleId}` | `ScheduleDetail.razor` | ED | — | Schedule detail and enable/disable | Global run-cycle action (not schedule-specific fire). |
| `/schedules/new` | `ScheduleCreate.razor` | CR | — | Create schedule | Not linked from default Automation menu (Epic 33 note). |

### Integrations domain

| Route | Page | Archetype | Menu | Primary purpose | Notes |
|-------|------|-----------|------|-----------------|-------|
| `/integrations` | `IntegrationsList.razor` | FL | ✓ | Integration catalog | |
| `/integrations/new` | `IntegrationNew.razor` | CR | — | Create integration | |
| `/integrations/{IntegrationId}` | `IntegrationDetail.razor` | ED | — | Bindings, slots, schedules, actions | Very large; uses `PageHeader`, wizard services, toasts. |
| `/integrations/{IntegrationId}/notifications` | `IntegrationNotifications.razor` | AU/DX | — | Integration-scoped notification config | Heavy form + cutover warnings. |
| `/integrations/{IntegrationId}/notifications/definitions/new` | `IntegrationNotificationDefinitionCreate.razor` | CR | — | Create integration-scoped definition | |
| `/integrations/{IntegrationId}/schedule-diagnostics` | `IntegrationScheduleDiagnostics.razor` | DX | — | Schedule/incident diagnostics | Query: `incidentId`. |

### Connections domain

| Route | Page | Archetype | Menu | Primary purpose | Notes |
|-------|------|-----------|------|-----------------|-------|
| `/connections` | `ConnectionsPage.razor` | FL | ✓ | Connections + connectors list | Embeds `ConnectorsListContent`. |
| `/connections/new` | `ConnectionNew.razor` | CR | — | New connection | |
| `/connections/{ConnectionId}` | `ConnectionDetail.razor` | ED | — | Connection detail and verification | |

### Config domain (gear owner)

Modules are menued under Config; pages live at `/modules`.

| Route | Page | Archetype | Menu | Primary purpose | Notes |
|-------|------|-----------|------|-----------------|-------|
| `/config` | `ConfigLanding.razor` | HL | ✓ | Config area index | Lists auth, contacts, governance, notifications. |
| `/modules` | `ModulesList.razor` | FL | ✓ Modules | Installed modules | |
| `/modules/{ModuleId}` | `ModuleDetail.razor` | ED | — | Lifecycle, validation, links | Links to audit, blocked work. |

**Authentication (config)**

| Route | Page | Archetype | Menu |
|-------|------|-----------|------|
| `/config/auth` | `AuthOverview.razor` | HL | ✓ |
| `/config/auth/sign-in` | `SignInExperience.razor` | AU | ✓ |
| `/config/auth/tenants` | `EntraTenants.razor` | FL/ED | ✓ |
| `/config/auth/entra/{RegistrationId}/users` | `EntraDirectoryUsers.razor` | FL | — |
| `/config/auth/entra/{RegistrationId}/groups` | `EntraDirectoryGroups.razor` | FL | — |
| `/config/security/sign-in` | `SignInExperienceRedirect.razor` | RD | — |

**Governance (config)**

| Route | Page | Archetype | Menu |
|-------|------|-----------|------|
| `/config/governance` | `GovernanceLanding.razor` | HL | ✓ |
| `/config/governance/catalog` | `GovernanceCatalog.razor` | FL | ✓ |
| `/config/governance/roles` | `RolesList.razor` | FL | ✓ |
| `/config/governance/roles/{RoleId}` | `RoleDetail.razor` | ED | — |
| `/config/governance/users` | `UsersList.razor` | FL | ✓ |
| `/config/governance/users/{UserId}` | `UserDetail.razor` | ED | — |
| `/config/governance/users/{UserId}/role-derived-access` | `UserEffectiveAccess.razor` | DX | — |
| `/governance/*` | `GovernanceRedirects.razor` | RD | — | Legacy paths → `/config/governance/*`. |

**Managed contacts (config hub only — not in icon-rail submenu)**

| Route | Page | Archetype | Menu |
|-------|------|-----------|------|
| `/config/contacts` | `ManagedContactsList.razor` | FL | (config) |
| `/config/contacts/{Id?}` | `ManagedContactEditor.razor` | ED/CR | (config) |

**Notifications (config)**

| Route | Page | Archetype | Menu |
|-------|------|-----------|------|
| `/config/notifications` | `NotificationsLanding.razor` | HL | ✓ |
| `/config/notifications/definitions` | `NotificationDefinitionsList.razor` | FL | ✓ |
| `/config/notifications/definitions/new` | `NotificationDefinitionCreate.razor` | CR | — |
| `/config/notifications/definitions/{DefinitionId}` | `NotificationDefinitionDetail.razor` | ED/AU | — |
| `/config/notifications/definitions/{DefinitionId}/edit` | `NotificationDefinitionEdit.razor` | AU | — |
| `/config/notifications/instances` | `NotificationInstancesList.razor` | FL | ✓ |
| `/config/notifications/instances/{InstanceId}` | `NotificationInstanceDetail.razor` | ED | — |
| `/config/notifications/delivery` | `NotificationDelivery.razor` | DX | ✓ |
| `/config/notifications/diagnostics` | `NotificationDiagnostics.razor` | DX | ✓ |

### Runtime admin (not in default menu)

| Route | Page | Archetype | Notes |
|-------|------|-----------|-------|
| `/runtimes/{RuntimeId}/controls/shutdown/requests` | `RuntimeShutdownRequestsList.razor` | FL | Deep-linked operational control surface. |
| `/runtimes/{RuntimeId}/controls/shutdown/requests/{RequestId}` | `RuntimeShutdownRequests.razor` | ED | |

### Density audit (qualitative)

| Area | Observation |
|------|-------------|
| **Control Center** | Multiple full-width sections (health, targets, activity, attention) share equal visual weight; operator must scroll to scan. |
| **Dashboard home** | Card grid is comparatively scannable; drill-ins are primary exit (Epic 47). |
| **Run detail** | Header packs four control actions + poll + refresh; body stacks investigation, status, steps, output, related context, external control trail. |
| **Integration detail** | Long vertical composition (bindings, connectors, schedules, evidence); toolbar duplicates navigation patterns. |
| **Notification authoring** | Definition detail/edit and integration notifications pages mix policy, template, routing, and cutover guidance in one flow. |
| **Config hub** | Intentionally sparse; good hub pattern candidate for other domains. |
| **Legacy start routes** | Three entry points (`/execute`, `/operations/start`, `/workflows/start`) increase cognitive load. |

## 5. Page archetype catalog

| Archetype | Intent | Typical routes | Presentation expectations (target state) |
|-----------|--------|----------------|------------------------------------------|
| **Summary hub (dashboard)** | Instance-wide KPIs and drill-outs | `/dashboard` | Card grid; omit forbidden cards; distinguish empty vs load failure. |
| **Operator console** | Multi-source operational picture | `/control/center` | Summary bands per concern; drill-in links; optional gated sections. |
| **Hub landing** | Orient within a config domain | `/config`, `/config/governance`, `/config/notifications` | Short index of child areas; minimal data load. |
| **Filtered list** | Browse/search a collection | `/runs`, `/schedules`, `/integrations`, lists under config | Filters in summary band; table/cards in detail; preserve query params in links. |
| **Entity detail** | Inspect one resource; selective actions | `/runs/{id}`, `/modules/{id}`, `/integrations/{id}` | Summary strip (identity, status, key actions); detail sections below; advanced/diagnostic collapsed or linked out. |
| **Create / new** | Create resource with validation | `/integrations/new`, `/schedules/new`, `/config/contacts/new` | Focused form; success navigates to detail. |
| **Start / execute** | Start operation or workflow | `/execute` (primary), legacy start routes | Schema-driven parameters where applicable; clear dispatch outcome. |
| **Authoring** | Edit policy/definition with side effects | Notification definition create/edit/detail, attention routing | Separate **policy** from **preview/diagnostics**; advanced JSON/rule editors deprioritized visually. |
| **Diagnostics** | Read-only troubleshooting | `/config/notifications/diagnostics`, integration schedule diagnostics | Clearly labeled read-only; link back to owning entity. |
| **Auth / session** | Sign-in and account | `/login`, `/account` | Public vs protected per `AuthRouteGate`. |
| **Redirect** | Back-compat URLs | `/governance/*`, `/config/security/sign-in` | No unique UX; preserve bookmarks. |
| **Deferred / placeholder** | Future product | Dashboard manage/layout routes | Must remain labeled deferred if linked from menu. |

**Archetype assignment rule for future pages:** Pick exactly one primary archetype per route. Secondary patterns (e.g. diagnostics block on a detail page) should be labeled **advanced** or linked to a **Diagnostics** route.

## 6. Standard layering model

Three presentation layers apply to **operator-facing** pages (not auth/login).

### Summary layer

- **Purpose:** Answer “what is the state?” in one screenful or less.
- **Contains:** Title, subtitle, primary status, highest-severity attention, top actions (≤3 primary), key identifiers (mono), refresh.
- **Sources:** Single aggregate DTO or small parallel fetch; reuse dashboard/control-center conventions where possible.
- **Example (current → target):**
  - *Run detail:* status + outcome + origin one-liner in summary; move step table to detail.
  - *Integration detail:* display name, state, execute/notifications shortcuts in summary; move binding editors to detail/advanced.

### Detail layer

- **Purpose:** Support routine operator tasks without expert knowledge.
- **Contains:** Tables, timelines, related links, enable/disable, standard drill-ins, schema-driven parameter forms on start surfaces.
- **Rules:** Only show fields the API exposes; use `Epic33UiDisplayHelpers` (or successors) for truthful labels; cross-links use documented query params.

### Advanced layer

- **Purpose:** Expert, forensic, or rare configuration.
- **Contains:** Raw identifiers, verbose JSON/rule editors, cutover flags, correlation trails, external-control request history, evaluate-only tooling.
- **Rules:** Collapse by default, separate route, or “Advanced” disclosure; never hide **safety-critical** state (failed run, blocked work) behind advanced.

### Layering vs backend “layering”

Epic 45 uses “layering” for **dashboard provider assembly** in `Apperture.ControlPlane.Dashboard`. That remains a **server composition** concern. This document’s layers are **presentation** concerns in Blazor pages only.

## 7. Drill-in pattern decision rules

Align with [Epic33](Epic33-Management-UI-Deepening.md) and [Epic47](Epic47-Dashboard-Drill-in-Refinement.md).

### When to link

| From | To | Rule |
|------|-----|------|
| Dashboard card | List or console | Use Epic 47 matrix (contextual label matches destination). |
| Control center health | Modules, schedules, blocked work, audit | Count-based links only when count &gt; 0 (except audit always available). |
| Control center anomalies | Run, schedule, module | Prefer entity URL when single unambiguous id; else control center. |
| List row | Entity detail | Default drill-in. |
| Entity detail | Related entity | Link when API provides stable id; do not infer missing ids (e.g. run `ModuleId` gap per Epic 33). |
| Forensics / audit | Filtered audit or list | Use `Epic33UiDisplayHelpers` link formation for `moduleId`, `scheduleId`, `runId`. |

### Query parameters (canonical)

| Parameter | Consumer | Semantics |
|-----------|----------|-----------|
| `hours` | `/control/audit` | `24` default card alignment; `>=168` → 7d window (Epic 47). |
| `moduleId`, `scheduleId`, `runId` | `/control/audit` | Pre-filter related references. |
| `status`, `createdFrom` | `/runs` | Dashboard executions drill-in; bookmarkable filters. |
| `integrationId` | `/runs`, `/execute` | Integration-scoped views/start. |
| `kind` | `/execute` | Operation vs workflow selection. |
| `incidentId` | Integration schedule diagnostics | Deep link to incident context. |
| Post-create notification queries | Definition detail | `NotificationPostCreateNavigation` constants. |

### When **not** to deep-link

- Destination list cannot honor filter (e.g. schedules **degraded only** — Epic 47 deferred).
- Multiple ambiguous targets (runtime health with multiple anomaly run ids → control center).
- Operator lacks permission to target page — **omit** link or section; do not link to a forbidden page that only errors.

### Labeling

- Drill-in label should name the destination (“View blocked work”, “View roles”), not generic “Learn more”.
- Prefer same-tab navigation for operational continuity; new tab only for external docs.

## 8. Permission, empty, and forbidden-state guidance

### Permission model in UI

- **Capabilities:** `IOperatorCapabilitiesService` mirrors server permissions for gating sections and actions.
- **Dashboard cards:** Unauthorized cards are **omitted** (`null` provider), not shown locked (Epic 45).
- **Detail pages:** Prefer explicit forbidden message (`PageMessage` Forbidden) when user hits route without capability — see `IntegrationDetail.razor`.
- **Actions:** Disable with `title` tooltip explaining missing permission (run control actions).

### State matrix

| State | When | Operator copy | Layout |
|-------|------|-----------------|--------|
| **Loading** | Initial fetch | “Loading…” / `PageMessage` Loading | Do not flash forbidden before capabilities load. |
| **Forbidden** | Authenticated, lacking capability | “Not authorized” + which capability if known | Replace body; keep shell. |
| **Empty (authorized)** | Successful fetch, zero rows | Neutral empty state; suggest create only if permitted | Distinguish from forbidden. |
| **Empty (dashboard)** | All cards omitted | Explain permissions may exclude all summary sources | Distinct from provider failure. |
| **Partial load (dashboard)** | Provider failure | Single placeholder card + `providerFailureCount` | Rest of dashboard still renders. |
| **Error** | HTTP or unexpected failure | Error title + message; retry when safe | Do not masquerade as empty. |
| **Cooperative action pending** | Run cancel/retry/etc. | Action banner; poll/refresh guidance | Do not claim terminal state until backend agrees. |

### Rules for future redesign

1. Never use empty state copy for forbidden.
2. Never show locked tiles for dashboard cards unless product explicitly changes Epic 45 contract.
3. Load capabilities before rendering forbidden on detail pages (`_capabilitiesLoaded` pattern).
4. Menu items may still appear when child actions are forbidden — pages must enforce.

## 9. Pilot page recommendations

Recommended **first redesign pilots** (documentation choice for a follow-on epic; no implementation in Epic 78):

| Priority | Route | Archetype | Rationale |
|----------|-------|-----------|-----------|
| 1 | `/control/center` | Operator console | Default operational home for many workflows; high section density; existing drill-ins and capability gates. |
| 2 | `/runs/{RunId}` | Entity detail | Richest execution truth surface; actions + forensics + steps; defines async-operation UX patterns for later standardization. |
| 3 | `/integrations/{IntegrationId}` | Entity detail | Canonical deployment unit (ADR-0028); many related routes; strong candidate for summary/detail/advanced split. |
| 4 | `/config/notifications/definitions/{DefinitionId}` | Authoring + detail | Represents complex config authoring cluster (Epic 77). |
| 5 | `/schedules` + `/schedules/{ScheduleId}` | List + detail | Smaller blast radius than integrations; benefits from list filter gaps identified in Epic 47 when backend ready. |

**Implemented from this table:** priority 1 — [Epic 82 — Control Center redesign pilot](Epic82-Control-Center-Redesign-Pilot.md); priority 3 — [Epic 80 — Integration Detail redesign pilot](Epic80-Integration-Detail-Redesign-Pilot.md); priority 2 — [Epic 81 — Run Detail redesign pilot](Epic81-Run-Detail-Redesign-Pilot.md).

**Secondary pilots:** `/dashboard` (card grid already closer to target), `/execute` (consolidate start UX), `/config` hub (template for other landings).

## 10. Future epic dependency map

| Future capability | Depends on Epic 78 | Notes |
|-------------------|---------------------|-------|
| **UI redesign pilots** | Archetypes, layering, drill-in rules | Implement per pilot route without breaking query contracts. |
| **Shared design system** | Archetypes + extension seams | Implemented in Epic 79 — [management-ui-design-system.md](../01-architecture/management-ui-design-system.md). |
| **Async operation UX standard** | Run detail pilot, layering | Cooperative actions already exist; standardize banners/poll/disabled patterns. |
| **Schema-driven parameter forms** | Start/execute archetype | Extend `ProcedureParameterForm`; operations already metadata-driven. |
| **Module-contributed dashboards/panels** | Dashboard provider seam | `IDashboardCardProvider` pattern; not WASM widget framework yet. |
| **Operational statistics / charts** | Summary hub archetype | New routes or cards; permission omission rules apply. |
| **Guided wizards / visual builders** | Integration detail wizard services | `Services/Wizard` exists for integration flows — separate epic. |
| **Calendar / temporal context** | Schedules, audit `hours` param | May require new query conventions documented here first. |
| **Schedules degraded filter drill-in** | Epic 47 deferred item | Requires list DTO/filter API before UI rule change. |
| **API Explorer menu href** | Inventory gap | Resolve `/tools/explorer` routing before redesign. |

## 11. Extension seams to preserve

Do not break these integration points during UI modernization:

| Seam | Location | Preserve |
|------|----------|----------|
| **Menu API** | `GET /api/menus`, `ControlPanelMenuDefinitions` | Domain keys, href stability, deferred labeling. |
| **Dashboard composition** | `IDashboardCardProvider`, `DashboardDefaultViewService` | Card omission, partial-load behavior, drill-in hrefs. |
| **Operator capabilities** | `IOperatorCapabilitiesService` | Client gating aligned with server enforcement. |
| **Display truth helpers** | `Epic33UiDisplayHelpers` | Centralize humanized labels and audit link URLs in tests. |
| **Management client** | `IManagementClient` | Page data contracts; UI does not invent fields. |
| **Auth gate** | `AuthRouteGate`, sign-in options | Public routes list; returnUrl handling. |
| **Parameter forms** | `ProcedureParameterForm` | Schema-driven execution inputs for operations. |
| **Shared organisms** | `PageHeader`, `PageMessage`, notification authoring organisms | Incremental extraction target, not big-bang library. |
| **Module UI** | `Apperture.ModuleSdk` / module `.Ui` projects | Placeholder pattern (`Sample.HelloWorld.Ui`); future panels must not collapse into monolithic Management.Ui.Client. |
| **Toast host** | `IToastService` / `ToastHost` | Transient action feedback on long forms (integrations). |
| **Query-param bookmarks** | See §7 | Dashboard and audit drill-ins rely on stable URLs. |

## 12. Acceptance criteria / validation notes

### Acceptance criteria (Epic 78)

- [x] Single decision doc published at `docs/02-decisions/Epic78-Management-UI-Modernization-Groundwork.md`.
- [x] Full route inventory for `Apperture.Management.Ui.Client` pages with domain grouping.
- [x] Page archetype catalog and layering model defined with examples.
- [x] Drill-in rules consistent with Epics 33 and 47.
- [x] Permission/empty/forbidden guidance documented.
- [x] Pilot pages named with rationale.
- [x] Future epics and extension seams listed.
- [x] No application code, backend, or visual changes.

### Validation performed

- Inventory cross-checked against `@page` directives under `Apperture.Management.Ui.Client` and `ControlPanelMenuDefinitions.GetDefaultMenus()`.
- Drill-in and query-param tables cross-checked against Epic 47 and grep of `SupplyParameterFromQuery` usages.
- No `dotnet build` or test run required (documentation-only epic).

### Human review / uncertainties

1. **Landing URL drift:** `OperatorLanding` redirects `/` → `/dashboard`, while [control-panel-ui.md](../01-architecture/control-panel-ui.md) states `/` → `/control/center`. Decide canonical operator landing and update architecture doc in a small follow-up.
2. **`/tools/explorer`:** Listed in Operations menu; no Blazor `@page` found in Ui.Client — confirm whether served by host, static file, or stale menu entry.
3. **Managed contacts:** Reachable via `/config` hub but absent from Config gear submenu in `ControlPanelMenuDefinitions` — intentional or oversight?
4. **Run detail size:** ~800+ lines — pilot redesign should plan component extraction without behavior change.
5. **Integration detail wizard code:** Substantial `Services/Wizard` usage — confirm which flows are production vs experimental before wizard epic.
6. **Governance legacy routes:** `GovernanceRedirects.razor` maintains `/governance/*` — keep redirects through redesign.
7. **Schedule create:** `/schedules/new` exists; Automation menu has no create link — align menu with product intent in a future epic.

---

## Summary

Epic 78 establishes the **documentation baseline** for Management UI modernization: inventory (~70+ routed surfaces), archetypes, summary/detail/advanced layering, drill-in contracts, permission states, five pilot routes, and preserved extension seams. Implementation epics should reference this document and avoid changing backend behavior or introducing unused abstractions until a pilot is explicitly chartered.
