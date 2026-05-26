# Epic 119 — Integration Detail settings UI

**Status:** Delivered

**Canonical spec:** [integration-scoped-settings.md](../01-architecture/integration-scoped-settings.md)

## Context

Epic 118 delivered integration-scoped settings persistence and API routes. Operators needed a Management UI surface on Integration Detail to view and edit those settings when definitions exist.

## Decision

1. **Route:** `/integrations/{integrationId}/settings` as a sibling page (same pattern as Notifications and Schedule diagnostics).
2. **Reuse:** [`PlatformSettingRow.razor`](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Config/PlatformSettingRow.razor) with `IntegrationScope` and `IntegrationId` parameters; provider options via `GetIntegrationSettingOptionsAsync`.
3. **Auth:** View with `CanViewIntegrations`; edit with `CanUpdateIntegrations` (matches API `integration.update`), not `CanManageIntegrations`.
4. **Empty state:** Clear copy when `ListIntegrationSettingsAsync` returns no definitions (production registry empty).
5. **Labels:** `PlatformSettingUiHelpers` integration-scoped source labels ("Integration override" vs "Platform override").
6. **Navigation:** Settings link on Integration Detail toolbar, summary band card, and sibling integration pages via [`IntegrationUiRoutes`](../../src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/IntegrationUiRoutes.cs).

## Explicit non-goals

- No production integration setting keys
- No module-contributed definitions
- No runtime consumption
- No wizards, custom panels, or resource browser

## Verification

- `dotnet build Apperture.slnx -c Release`
- `Apperture.Management.Ui.Shared.Tests` — Epic 119, 112, 114 UI tests

## Recommended next epic

**Epic 120:** First real module-owned integration setting key in the integration definition registry.
