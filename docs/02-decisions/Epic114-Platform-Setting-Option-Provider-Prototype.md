# Epic 114 — Platform setting option provider prototype

**Status:** Delivered

**Context:** Epic 113 specified dynamic option providers and a phased roadmap. The first implementation step is one **platform-owned** provider to prove options loading, write-time validation, and UI integration without integration scopes or module contributions.

## Decision

Implement a narrow provider-backed path for platform settings:

| Piece | Choice |
|-------|--------|
| **First provider** | `platform.connectorDefinitions` — in-memory [`IConnectorDefinitionRegistry`](../../src/Apperture.Core/Apperture.Core.Application/Services/IConnectorDefinitionRegistry.cs), authorized via [`IConnectorDefinitionCatalogReadService`](../../src/Apperture.Core/Apperture.Core.Application/Services/IConnectorDefinitionCatalogReadService.cs) |
| **Scalar Select storage** | New `ValueType.String` (distinct from `StringArray` MultiSelect) |
| **Metadata** | `OptionProviderKey` on `PlatformSettingDefinition` / resolved DTOs |
| **Options API** | `GET /api/config/platform-settings/{key}/options` (setting controls allowed provider) |
| **Write validation** | `PlatformSettingsService.SetAsync` re-fetches provider options; rejects values not in allowed set |
| **UI** | `PlatformSettingRow` loads provider options when `OptionProviderKey` is set and static `AllowedValues` is empty; Select dropdown + MultiSelect checkboxes; loading/error states; preserve unavailable selections in editor when feasible |
| **Production keys** | **None added** — nine Phase 1 keys unchanged; test-only definitions for unit/UI tests |

**Provider abstraction:** `IPlatformSettingOptionProvider`, `IPlatformSettingOptionProviderRegistry`, `PlatformConnectorDefinitionsOptionProvider`, DI in `AddApperturePlatformSettingsCore`.

## Explicit non-goals

- Same-integration or cross-integration providers
- Module-contributed providers or settings
- Resource/catalog provider system
- Search/paging on options endpoint
- Stale-selection warnings in UI (minimal preserve-unavailable only)
- `ConnectorDefinitions` SQL table (catalog remains in-memory/API)

## Verification

- `dotnet build Apperture.slnx -c Release`
- Unit tests: options endpoint, provider write validation, `String` codec
- UI structural tests: Select branch, `GetPlatformSettingOptionsAsync`, scalar save payload
- Manual UI smoke **not** run — no user-visible provider-backed production setting

## Recommended next epic

**Epic 115 (delivered):** Integration-scoped provider context foundation — see [Epic115-Integration-Scoped-Option-Provider-Foundation.md](./Epic115-Integration-Scoped-Option-Provider-Foundation.md).

**Epic 116 (suggested):** First real same-integration option provider (e.g. `campusnexus.campuses`) with integration context from Epic 115.

## References

- [platform-settings-option-providers-and-scopes.md](../01-architecture/platform-settings-option-providers-and-scopes.md)
- [platform-settings-registry.md](../01-architecture/platform-settings-registry.md#option-providers-and-scoped-settings-epic-113114)
- [Epic 113](./Epic113-Platform-Settings-Option-Providers-Design.md)
