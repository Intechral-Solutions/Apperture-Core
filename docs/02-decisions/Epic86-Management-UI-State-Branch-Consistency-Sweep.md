# Epic 86 — Management UI state branch consistency sweep

## Status

Accepted — implemented May 2026.

## Purpose

Apply [Epic 79](Epic79-Management-UI-Design-System-Foundation.md) and [Epic 85](Epic85-Management-UI-Pilot-Consolidation.md) **state-branch** guidance across additional Management UI pages where the replacement was **clearly equivalent** to existing markup. This epic is a **consistency sweep**, not a visual redesign or new layout layering.

Canonical context: [Epic 78](Epic78-Management-UI-Modernization-Groundwork.md), [management UI design system](../01-architecture/management-ui-design-system.md).

## Scope

- Replace ad-hoc loading / forbidden / empty / load-error branches with `LoadingState`, `ForbiddenState`, `EmptyState`, or `ErrorState` where semantics matched what the page already showed.
- Keep **warnings**, **form/action errors**, **Graph browse failures alongside results** (`PageMessage`), and **inline domain notices** as `PageMessage` (or existing markup) when they were not whole-page branch states.

## Pages touched

| Area | Pages / components | Branch types standardized |
|------|---------------------|---------------------------|
| Governance | `UserDetail.razor`, `RoleDetail.razor` | Loading / forbidden / error / catalog loading in edit mode |
| Config — notifications | `NotificationsLanding.razor`, `NotificationDefinitionsList.razor`, `NotificationInstancesList.razor`, `NotificationDelivery.razor`, `NotificationDiagnostics.razor`, `NotificationInstanceDetail.razor` | Loading / forbidden / error / empty / not-found-as-empty |
| Config — contacts | `ManagedContactsList.razor`, `ManagedContactEditor.razor` | Forbidden / loading / empty (inline create errors unchanged) |
| Integrations | `IntegrationsList.razor`, `IntegrationNew.razor`, `IntegrationNotifications.razor`, `IntegrationNotificationDefinitionCreate.razor`, `IntegrationScheduleDiagnostics.razor` | Loading / forbidden / error / empty (module picker) |
| Connections | `ConnectionsPage.razor`, `ConnectionDetail.razor`, `ConnectionNew.razor`, `ConnectorsListContent.razor` | Loading / forbidden / error / empty |
| Control | `AuditTimeline.razor` | Forbidden / error / loading / empty |

Previously updated in the same effort (prior session): auth overview/Entra/sign-in clusters, dashboards, governance lists, attention, modules, runs, blocked work, shutdown requests, etc. — see git history on `Apperture.Management.Ui.Client/Pages/` for precise diffs.

## State usage rules applied

- **`LoadingState`** — Page or section is actively fetching; existing copy equivalent to “Loading…”.
- **`ErrorState`** — Initial load/list load failed (`_overview`/`_detail`/`_items`/`_fleet` null after fetch, HTTP errors surfaced as `_error`).
- **`ForbiddenState`** — Only where the UI **already** showed explicit not-authorized messaging (panels or muted denial text promoted to the shared forbidden branch).
- **`EmptyState`** — Authorized user, **no matching records** (“no X match”, empty fleet under filters).
- **`PageMessage` retained** — Entra directory **browse** failures with inline technical detail (`_result.Success` false), tenant **form errors** (`_formError`), sign-in policy **save** errors, Notification definitions **landing operational** warnings (`NotificationDelivery`), and **`Login` / `LoginLocal`** flows (small surface; defer to a dedicated pass if desired).

## Deferred / intentionally untouched

- **`Login.razor`**, **`LoginLocal.razor`** — Error banners remain `PageMessage`; swapping to `ErrorState` is mechanically equivalent but was left to avoid churn on unauthenticated shells without a targeted review.
- **Epic 80–84 pilot pages** — No broad rewrites unless already aligned elsewhere; Integration Detail inner sections were already on primitives.
- **Graph/API “soft” failures rendered beside tables** (`EntraDirectoryUsers`/`EntraDirectoryGroups` `_result.Summary`) — Kept as `PageMessage` Error to preserve pairing with `_result.TechnicalDetail` and avoid misclassifying as full-page failures.
- **Notification diagnostics inner “Health summary unavailable”** (`NotificationDiagnostics`) — Operational/degraded informational text; not converted without a clearer empty vs degraded contract.

## Behavior preservation

- Routes, menus, capability checks, API calls, query parameters, refresh/polling, and navigation targets were **not** intentionally changed—only markup for equivalent state branches.

## Validation

Recorded at implementation time:

- `dotnet build` on `src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Apperture.Management.Ui.Client.csproj`
- `dotnet test` on unit tests filtered with `FullyQualifiedName~ManagementUiSemanticsTests`

## Recommended next steps

- Optional: normalize **login** shells to state wrappers once copy and layout are reviewed together.
- Any page mixing **omit vs deny** semantics should stay deferred until Epic 78 permission-display rules are revisited page-by-page.
