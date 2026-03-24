# ADR-0025: Epic 50 Integration Composition

## Status

Accepted

## Context

ADR-0024 established Integration as the operator-facing deployment/configuration unit and added nullable `IntegrationId` to Bindings and ScheduleDefinitions. We need to make Integration a real composition hub: instance settings, connection bindings, and schedules owned by or attached to Integrations, while preserving backward compatibility with legacy module-scoped bindings and schedules.

## Decision

### Integration-Owned Resources (Epic 50)

1. **Settings** — `Integrations` table has `SettingsJson` (nvarchar(max)) storing key-value pairs. No separate table or module-specific schema for the first slice. Instance settings are managed via Integration CRUD.

2. **Connection bindings** — Domain `Binding` includes `IntegrationId?`. Bindings created via `api/integrations/{id}/bindings` set `IntegrationId`. Slot uniqueness is enforced per Integration: one active binding per `(IntegrationId, ModuleId, SlotName)` when Integration-owned; one per `(ModuleId, SlotName)` for legacy (IntegrationId null). Existing `api/modules/{moduleId}/slots/{slotName}/binding` remains for compatibility; it creates bindings with `IntegrationId = null`.

3. **Schedules** — Domain `ScheduleDefinition` includes `IntegrationId?`. Schedules created via `api/integrations/{id}/schedules` set `IntegrationId`. Global schedules (IntegrationId null) remain supported.

4. **Connections Used In** — `ConnectionUsedInEntry` includes optional `IntegrationId` and `IntegrationDisplayName`. When a binding has `IntegrationId`, the Used In list links to the Integration; otherwise it links to the Module.

### Bridged Behavior

- **Runtime `SlotBoundResolver`** — Resolves by `(moduleId, slotName)` only. Integration-scoped resolution is deferred. When multiple bindings exist for the same slot (Integration-owned and legacy), the current `GetBySlotAsync` returns the first match; there is no Integration-aware selection.

- **Schedule assignment** — Assignment to RuntimePlane is unchanged. Schedules (Integration-owned or global) are treated the same for scheduling/runtime purposes.

### Deferred / Out of Scope

- Runtime worker deployment/control
- Client modeling (e.g. Client entities that reference Integrations)
- Full migration of legacy bindings and schedules to Integrations
- Module-specific settings schema
- `SlotBoundResolver` Integration-aware resolution

## Consequences

### Positive

- Integration becomes a usable composition hub in the UI (settings, bindings, schedules)
- Operators manage bindings and schedules from Integration context
- Connections Used In accurately reflects Integration ownership
- Backward compatible: legacy API and bindings/schedules remain valid

### Negative

- Two parallel paths (module-scoped vs Integration-scoped) until migration
- Runtime does not yet distinguish Integration context when resolving bindings

### Neutral

- Migration of legacy bindings/schedules to Integrations is deferred
- Code comments in `SlotBoundResolver` and `GetBySlotAsync` document deferred Integration-scoped resolution
