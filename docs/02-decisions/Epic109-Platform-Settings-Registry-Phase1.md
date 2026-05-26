# Epic 109 — Platform settings registry Phase 1

**Status:** Delivered

**Context:** Notification cutover, platform events, and attention observation flags lived in `appsettings` and post-configure overlays. Epic 105B defined a platform-managed settings layer; Epic 107 identified concrete keys. Operators needed in-product edit, audit, and precedence without JSON writeback.

## Decision

Implement **instance-scoped** platform settings Phase 1:

- **Nine** platform-owned keys (notifications, platform events, attention observation)
- SQL persistence (`PlatformSettingValues`)
- Resolver precedence: **`Database` > `ConfigurationDefault` > `DefinitionDefault`**
- API: `GET/PUT/DELETE /api/config/platform-settings`
- Management UI: `/config/platform-settings`
- Governance: `platform.settings.read`, `platform.settings.manage`
- Audit: `platform.settings.set`, `platform.settings.reset`
- **No** write-back to `appsettings`; **no** secrets in registry

Runtime overlay via `IPostConfigureOptions` on Management.Host (and partial WorkerHost, e.g. platform events).

## Phase 1 keys

| Key area | Examples |
|----------|----------|
| Notifications | Evaluation, instances, delivery, use-platform-pipeline, poll interval, batch size |
| Platform events | `PlatformEvents.Enabled` |
| Attention | Observation enabled, observation interval |

Definitions: [`PlatformSettingDefinitionRegistry`](../../src/Apperture.Core/Apperture.Core.Application/PlatformSettings/PlatformSettingDefinitionRegistry.cs), keys in [`PlatformSettingKeys`](../../src/Apperture.Core/Apperture.Core.Application/PlatformSettings/PlatformSettingKeys.cs).

## Explicitly deferred

- Module-contributed definitions; integration/module/user scopes
- Generated settings form framework; custom Razor panels
- Full WorkerHost overlay for every Phase 1 key
- Secret values; bootstrap values in registry
- Dynamic option providers (Epic 113 design, Epic 114 prototype)

## Verification (recorded in Epic 110)

API smoke on Aspire: PUT override → `source: Database`; DELETE reset → fallback; validation 400 for invalid types/ranges; audit rows; appsettings SHA256 unchanged.

## References

- [platform-settings-registry.md](../01-architecture/platform-settings-registry.md)
- [ADR-0039](./ADR-0039-environment-startup-bootstrap-and-platform-settings.md)
- [Epic 110](./Epic110-Foundation-Stabilization.md)
- [Epic 111](./Epic111-Platform-Settings-Operator-UI-Polish.md)
