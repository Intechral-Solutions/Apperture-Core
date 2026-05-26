# Epic 118 — Integration-scoped settings persistence and API foundation

**Status:** Delivered

**Canonical spec:** [integration-scoped-settings.md](../01-architecture/integration-scoped-settings.md)

## Context

Epic 117 defined integration-scoped **value storage**, API shape, auth/audit, and UI placement. Epics 114–116 delivered provider-backed options with integration **provider context** on the instance platform-settings route, but values remained instance-scoped.

## Decision

Implement integration value scope end-to-end at the management API layer without UI or runtime consumption:

1. **Persistence** — Use existing `PlatformSettingValues` columns; `SqlPlatformSettingValueRepository` supports scoped get/list/delete/upsert with `BuildId(scopeKind, scopeId, key)`.
2. **Definitions** — `AllowedValueScopes` on `PlatformSettingDefinition`; production `IntegrationSettingDefinitionRegistry` is empty; nine instance keys unchanged.
3. **Resolution** — `IntegrationSettingsResolver`: Integration DB → definition default only (no instance fallback, no configuration layer).
4. **API** — `IntegrationSettingsController` at `/api/integrations/{integrationId}/settings` (list/get/put/delete/options); route `integrationId` is both value scope and provider context.
5. **Authorization** — `integration.read` / `integration.update` only (not `platform.settings.*`) via `IntegrationSettingsAuthorization` and `IntegrationSettingOptionProviderAuthorization`.
6. **Audit** — `integration.settings.set` / `integration.settings.reset` on `Integration` resource.
7. **Client** — `ListIntegrationSettingsAsync`, `GetIntegrationSettingAsync`, `SetIntegrationSettingAsync`, `ResetIntegrationSettingAsync`, `GetIntegrationSettingOptionsAsync` on `IManagementClient`.

Platform `/api/config/platform-settings` behavior is unchanged. Epic 115/116 integration-context providers on that route still store instance-scoped values.

## Explicit non-goals

- Integration Detail settings UI (Epic 119)
- Runtime WorkerHost/module consumption
- Production integration setting keys
- Module-contributed definitions
- Cross-integration `ProviderBinding`

## Verification

- `dotnet build Apperture.slnx -c Release`
- Unit tests: `Epic118`, `Epic114`–`Epic116`, `PlatformSettings` (59 passed)

## Recommended next epic

**Epic 119:** Integration Detail settings UI — [delivered](Epic119-Integration-Detail-Settings-UI.md). **Epic 120:** first production integration setting key.
