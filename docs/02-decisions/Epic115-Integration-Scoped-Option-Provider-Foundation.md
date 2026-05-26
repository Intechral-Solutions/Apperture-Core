# Epic 115 — Integration-scoped option provider foundation

**Status:** Delivered

**Context:** Epic 114 proved instance-scoped provider-backed options (`platform.connectorDefinitions`). Before implementing real same-integration catalogs (CampusNexus campuses, SQL tables, SFTP paths), the platform needs a safe integration context model for option lookup and write-time validation.

## Decision

Add **provider context** (distinct from setting value scope):

| Piece | Choice |
|-------|--------|
| **Context kinds** | `Instance` (default), `Integration` (requires `integrationId` on request) |
| **Types** | `PlatformSettingOptionProviderContextKind`, `PlatformSettingOptionProviderContext` (`SettingKey`, `IntegrationId`, `Actor`) |
| **Definition metadata** | `OptionProviderContextKind` on `PlatformSettingDefinition` / resolved DTOs |
| **Coordinator** | `PlatformSettingOptionProviderCoordinator` — build context, authorize, invoke provider |
| **Authorization** | `PlatformSettingOptionProviderAuthorization` — `platform.settings.read`/`manage` + `integration.read` when integration context |
| **Options API** | `GET .../options?integrationId=` — reject extra/missing context per definition |
| **Write validation** | Optional `integrationId` on `PlatformSettingUpdatePayload` / PUT body; instance persistence unchanged |
| **Test provider** | `test.integration.options` — test assembly only, returns `{integrationId}:alpha` / `:beta` |
| **Production** | No new setting keys; nine Phase 1 keys unchanged; `platform.connectorDefinitions` behavior unchanged |

## Provider context vs value scope

- **Provider context** — where selectable options are resolved (platform catalog vs integration catalog).
- **Value scope** — where the setting value is stored (still instance-only in Epic 115).

## Explicit non-goals

- Real remote providers (`campusnexus.campuses`, `sqlserver.tables`, `sftp.remoteDirectories`)
- Integration-scoped setting persistence or integration settings UI tab
- Cross-integration `ProviderBinding`, module-contributed providers, resource/catalog provider system
- Test provider in production DI

## Verification

- `dotnet build Apperture.slnx -c Release`
- Unit tests: `Epic115IntegrationScopedOptionProviderTests`, Epic 114 regression, platform settings suite
- Manual smoke: **not applicable** — no production setting uses integration context

## Recommended next epic

**Epic 116:** First real same-integration option provider (e.g. `campusnexus.campuses`) with integration settings UI and remote/catalog reads.

## References

- [platform-settings-option-providers-and-scopes.md](../01-architecture/platform-settings-option-providers-and-scopes.md)
- [Epic 114](./Epic114-Platform-Setting-Option-Provider-Prototype.md)
