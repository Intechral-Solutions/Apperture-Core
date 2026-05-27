# Epic 120: Settings presentation refinement and demo gallery

## Status

Delivered.

## Goal

Improve operator ergonomics for platform and integration settings UI before adding more production settings: compact rows, category tabs, and a Development-gated input gallery that exercises every supported editor type.

## Non-goals (honored)

- No new production or integration-scoped operational settings
- No runtime / WorkerHost consumption of demo keys
- No module-contributed settings, custom Razor panels, wizards, or catalog browser
- No changes to persistence model, source precedence, or appsettings writeback

## UI changes

| Area | Change |
|------|--------|
| **Row layout** | Compact single-line header (label, info tooltip, source pill, editor, save/reset); multi-select expands below |
| **Help** | Description, safety note, and key in `SettingInfoAffordance` tooltip via `PlatformSettingUiHelpers.BuildHelpTooltip` |
| **Restart** | Compact warning glyph with `title` instead of full-width `PageMessage` |
| **Read-only** | Honors `IsEditable` on definition (demo `demo.settings.readOnly`) |
| **Tabs** | `PlatformSettingsCategoryTabs` — one tab per `Category` (Notifications, Platform events, Attention observation, Settings gallery) |
| **Shared shell** | `PlatformSettingsEditor` used by platform and integration settings pages |

## Demo settings

Registered in `PlatformSettingDemoDefinitionRegistry`; merged by `GatedPlatformSettingDefinitionRegistry` on **Management host only**.

**Gating:** `IHostEnvironment.IsDevelopment()` OR `PlatformSettings:DemoGallery:Enabled`.

| Key | InputKind |
|-----|-----------|
| `demo.settings.boolean` | Toggle |
| `demo.settings.integer` | Number |
| `demo.settings.text` | Text |
| `demo.settings.duration` | Duration |
| `demo.settings.staticSelect` | Select (static options) |
| `demo.settings.staticMultiSelect` | MultiSelect (static options) |
| `demo.settings.providerSelect` | Select (`platform.connectorDefinitions`) |
| `demo.settings.providerMultiSelect` | MultiSelect (`platform.connectorDefinitions`) |
| `demo.settings.requiresRestart` | Toggle (`RequiresRestart`) |
| `demo.settings.readOnly` | Text (`Editable = false`) |

Category: `Demo` (UI tab: **Settings gallery**).

## Production settings

Nine Phase 1 instance keys unchanged in `PlatformSettingDefinitionRegistry`. WorkerHost continues to use the production registry only.

## Follow-on

**Epic 121 (recommended):** first production integration-scoped setting key (previously documented as Epic 120 in foundation docs).

## References

- [platform-settings-registry.md](../01-architecture/platform-settings-registry.md)
- [Epic119-Integration-Detail-Settings-UI.md](Epic119-Integration-Detail-Settings-UI.md)
- [epic110-foundation-stabilization.md](../03-operations/epic110-foundation-stabilization.md)
