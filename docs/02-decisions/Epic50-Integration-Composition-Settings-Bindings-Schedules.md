# Epic 50 — Integration composition (settings, bindings, schedules)

Date: 2026-03 (approximate)

## Status

Accepted — implemented.

## Context

ADR-0024 introduced Integration and nullable ownership on bindings and schedules. Epic 50 makes Integration a **composition hub**: instance settings, integration-scoped bindings, integration-scoped schedules, and richer **Used In** on connections—while keeping legacy module-scoped APIs working until Epic 54 tightens the contract.

## Summary of changes

1. **Settings** — `Integrations.SettingsJson` (nvarchar(max)) stores key/value settings; managed through integration create/update (partial update support on integration).
2. **Bindings** — Domain `Binding.IntegrationId`; API **`/api/integrations/{id}/bindings`** for list/create/delete; slot uniqueness enforced per integration when `IntegrationId` is set; legacy **`/api/modules/.../binding`** still creates bindings with null `IntegrationId` until Epic 54.
3. **Schedules** — `ScheduleDefinition.IntegrationId`; API **`/api/integrations/{id}/schedules`** for list/create; global schedules (`IntegrationId` null) remain valid in this epic.
4. **Connections Used In** — `ConnectionUsedInEntry` carries optional integration id and display name when the consuming binding is integration-owned.
5. **Eligibility helpers** — Endpoints/DTOs for connections eligible for a slot and capability slots for an integration’s module (binding UX).
6. **Repositories** — `IBindingRepository` / `IScheduleRepository` methods to list by integration.
7. **Management client** — Integration binding and schedule DTOs and client methods aligned with API shapes (e.g. `ScheduleDtos` for create schedule body).

### Deferred in Epic 50 (documented in ADR-0025)

- **`SlotBoundResolver`** remained **`(moduleId, slotName)`**-only; ambiguous multi-integration cases could “first match” until Epic 54 added integration-scoped resolution when an integration id is known.

## Consequences

- Operators manage settings, bindings, and schedules **in integration context** without losing legacy data paths.
- Two parallel configuration paths (module-scoped vs integration-scoped) until Epic 54 retires unsafe creates.

## Related

- [ADR-0025](./ADR-0025-epic-50-integration-composition.md)
- [ADR-0028](./ADR-0028-integration-canonical-deployment-unit.md) — follow-up tightening

## Testing

- `tests/Apperture.UnitTests/Integration/Epic50IntegrationCompositionTests.cs`
