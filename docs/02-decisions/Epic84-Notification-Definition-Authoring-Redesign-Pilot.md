# Epic 84 — Notification definition detail and authoring redesign pilot

## Status

Accepted — implemented May 2026.

## Context

Epic 78 identified `/config/notifications/definitions/{DefinitionId}` as pilot **#4** because notification definition authoring combines policy metadata, rule/template payloads, subscriptions, and delivery diagnostics in one dense surface.

Epics 79–83 established and validated the Management UI layering primitives across config hubs, integration detail, run detail, control center, and schedules list/detail.

Before Epic 84, notification definition detail/edit/create pages used mostly ad-hoc bordered sections and inline links, with limited summary orientation and no progressive-disclosure advanced layer for raw JSON payloads.

## Why this pilot

- Completes the remaining Epic 78 high-priority pilot for dense authoring/detail pages.
- Applies Epic 79 primitives to a configuration-heavy route without changing notification behavior or backend contracts.
- Improves scanability while preserving existing form, validation, subscription, preview, and diagnostics behavior.

## Before / after layering

### `/config/notifications/definitions/{DefinitionId}` (detail/authoring cluster)

| Layer | Before (Epic 77 baseline) | After (Epic 84) |
|-------|---------------------------|-----------------|
| **Header** | Breadcrumb line + title + separate edit button | `PageHeader` Eyebrow **Notifications**, contextual subtitle, mono id, preserved toolbar navigation/actions |
| **Summary** | No dedicated orientation layer | `ManagementSection` summary with status/origin/scope pills, lock/attention indicators, and `ManagementSummaryCard` drill-outs |
| **Detail** | Flat stack of metadata, subscriptions, preview, recipients, recent instances | Single operational `ManagementSection` grouping metadata, subscription management, preview tooling, recipient resolution, and recent instances |
| **Advanced** | Rule/template JSON always visible in normal flow | `ManagementAdvancedSection` with raw JSON payloads and technical identifiers |

### `/config/notifications/definitions/{DefinitionId}/edit` and `/new` (consistency pass)

Authoring pages now use `PageHeader` + `ManagementSection` + state wrappers (`LoadingState`, `ForbiddenState`, `EmptyState`) while preserving the same `NotificationDefinitionAuthoringForm` behavior and navigation.

## Epic 79 components used

| Component | Usage |
|-----------|-------|
| `PageHeader` + `Eyebrow` | Notifications context and toolbar actions |
| `ManagementSection` | Summary and operational authoring/detail grouping |
| `ManagementSummaryCard` | Quick links to definitions, diagnostics, instances, delivery, integration notifications |
| `ManagementAdvancedSection` | Raw JSON payloads and technical identifiers |
| `StatusPill` | Enabled/scope/origin/source posture |
| `AttentionPill` | Rule/template lock and deliverable-recipient warning posture |
| `DrillInLink` | Operational navigation links |
| `LoadingState` / `EmptyState` / `ForbiddenState` / `ErrorState` | Truthful state rendering |

No new `ManagementUiSemantics` mappings were required.

## Behavior preservation rules

- **No API, route, permission, or menu changes**.
- Existing `IManagementClient` calls and DTO usage remain unchanged for definition detail, subscriptions, preview render, recipient resolution, and recent instances.
- Existing capability gates (`CanViewNotificationDefinitions`, `CanManageNotificationDefinitions`, `CanUpdateIntegrations`, `CanManageManagedContacts`) remain unchanged.
- Existing form behavior, validation handling, save/cancel flow, and authoring redirect behavior remain unchanged through `NotificationDefinitionAuthoringForm`.
- Existing post-create prompt and navigation query behavior via `NotificationPostCreateNavigation` remains unchanged.

## Known deferred work

- Optional extraction of the subscriptions table/controls into a dedicated presentational child component if reused in integration-scoped notification surfaces.
- Shared semantic mapping for notification definition lifecycle/origin/status can be introduced only when another page needs the same presentation mapping.
- Any server-side filtering or additional summary metrics remain backend-dependent and out of scope.

## References

| Doc | Relationship |
|-----|---------------|
| [Epic 78](Epic78-Management-UI-Modernization-Groundwork.md) | Pilot #4 rationale |
| [Management UI design system](../01-architecture/management-ui-design-system.md) | Primitives canon |
| [Epic 79](Epic79-Management-UI-Design-System-Foundation.md) | Foundation components and layering |
| [Epic 80](Epic80-Integration-Detail-Redesign-Pilot.md), [Epic 81](Epic81-Run-Detail-Redesign-Pilot.md), [Epic 82](Epic82-Control-Center-Redesign-Pilot.md), [Epic 83](Epic83-Schedules-List-Detail-Redesign-Pilot.md) | Adjacent layering pilots |

## Validation

- `dotnet build src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`.
- Focused semantics slice: `dotnet test ... --filter FullyQualifiedName~ManagementUiSemanticsTests`.
