# Epic 116 — CampusNexus campuses option provider

**Status:** Delivered

**Context:** Epic 115 added integration-scoped provider context. Epic 116 delivers the first **real** same-integration provider that calls Campus Nexus OData for active campuses.

## Decision

| Piece | Choice |
|-------|--------|
| **Provider key** | `campusnexus.campuses` |
| **Context** | `OptionProviderContextKind.Integration` — requires `integrationId` on options GET and write validation |
| **Campus data** | OData `Campuses` entity set via [`CampusNexusCampusQueries`](../../src/Apperture.Connectors/Apperture.Connectors.CampusNexus/CampusNexusCampusQueries.cs) (`$filter=IsActive eq true`, `$select=Id,Name,Code`) |
| **Client resolution** | [`IRuntimeSlotResolver`](../../src/Apperture.Core/Apperture.Core.Application/Services/IRuntimeSlotResolver.cs) — `(integration.ModuleId, CnsApi, integrationId)` → binding → connection → [`ICampusNexusApiClient`](../../src/Apperture.Connectors/Apperture.Connectors.CampusNexus/ICampusNexusApiClient.cs) |
| **Option mapping** | **Value:** campus `Id` (string); **Label:** `Name` (fallback `Code`, then `Campus {Id}`); **Description:** `Code` when distinct from label |
| **Host wiring** | [`AddConnectorResolution()`](../../src/Apperture.ConnectorResolution/ServiceCollectionExtensions.cs) on Management.Host; provider registered in [`AddApperturePlatformSettingsCore`](../../src/Apperture.ControlPlane/Apperture.ControlPlane.Security/ServiceCollectionExtensions.PlatformSettings.cs) |
| **Production setting keys** | **None** — test-only definition `Test.CampusNexus.Campuses.Select.Sample` |

## Explicit non-goals

- Integration-scoped setting persistence or integration settings UI tab
- Programs, statuses, SQL/SFTP providers, resource browser, `ProviderBinding`
- Storing campus catalog in platform settings or appsettings
- Cross-integration or module-contributed providers

## Verification

- `dotnet build Apperture.slnx -c Release`
- Unit tests: `Epic116CampusNexusCampusesOptionProviderTests`, `CampusNexusCampusQueriesTests`, Epic 114/115 regression
- Manual smoke: **not applicable** — no production setting uses the provider

## Recommended next steps

- Epic 117 design completed: [Epic117-Integration-Scoped-Settings-Design.md](./Epic117-Integration-Scoped-Settings-Design.md)
- Epic 118 delivered: integration-scoped persistence and explicit integration settings API
- Epic 119: Integration Detail settings UI

## References

- [Epic 115](./Epic115-Integration-Scoped-Option-Provider-Foundation.md)
- [platform-settings-option-providers-and-scopes.md](../01-architecture/platform-settings-option-providers-and-scopes.md)
