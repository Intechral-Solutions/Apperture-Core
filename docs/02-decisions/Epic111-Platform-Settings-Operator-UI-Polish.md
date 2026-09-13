# Epic 111 — Platform settings operator UI polish

**Status:** Delivered

**Context:** Epic 109 delivered API and a minimal `/config/platform-settings` page (text inputs, limited validation UX). Epic 110 recommended operator polish before bootstrap credential binding (108b) or broader UI sweeps.

## Decision

Harden the **operator experience** for existing Phase 1 keys only — no registry scope expansion, no new setting keys, no appsettings writeback.

## Delivered

| Area | Behavior |
|------|----------|
| **Layout** | Category grouping (Notifications, Platform events, Attention observation) |
| **Source badges** | Platform override, configuration default, definition default — friendly labels and tooltips from API `Source` |
| **Editors** | Typed controls by `ValueType` / `InputKind` (toggle, number, duration) |
| **UX** | Dirty state, save/reset, validation errors, read-only when lacking `platform.settings.manage` |
| **Safety** | Context notes on operationally sensitive flags (delivery, platform events, observation) |
| **Intro** | Page guidance on precedence and audit actions |

**Components:** [`PlatformSettings.razor`](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettings.razor), [`PlatformSettingRow.razor`](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettingRow.razor), [`PlatformSettingUiHelpers`](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/PlatformSettingUiHelpers.cs).

## Non-goals

- MultiSelect / `StringArray` (Epic 112)
- Dynamic option providers (Epic 113–114)
- Module-contributed settings or integration scopes
- Generated form framework or custom panels

## References

- [platform-settings-registry.md](../01-architecture/platform-settings-registry.md#operator-ui-epic-111)
- [Epic 109](./Epic109-Platform-Settings-Registry-Phase1.md)
- [Epic 112](./Epic112-Platform-Settings-MultiSelect-Foundation.md)
