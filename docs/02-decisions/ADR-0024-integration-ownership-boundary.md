# ADR-0024: Integration ownership boundary

## Status

Accepted

## Context

We need an operator-facing resource that represents a configured instance of a module. Bindings and schedules are currently standalone; we need a clear ownership boundary for future composition (settings, bindings, schedules, runtime deployment intent).

## Decision

- **Integration** is the operator-facing deployment/configuration unit. Each integration references a module definition (e.g. `Sample.HelloWorld`) and represents a configured instance for a particular purpose.
- **Module** = definition (reusable capability); **Integration** = configured instance.
- A nullable `IntegrationId` column is added to `Bindings` and `ScheduleDefinitions`. Existing rows have `IntegrationId = null` (transitional; treated as "global" until migrated).
- New integration-owned resources (future epics) will set `IntegrationId`.
- This epic (Epic 49): schema and entity only; no logic changes to binding/schedule services.

## Consequences

### Positive

- Clear ownership boundary for future composition
- Backward compatible: existing bindings and schedules remain valid with null IntegrationId
- Enables future: instance settings, connection bindings, schedules, runtime deployment under Integration

### Negative

- Two concepts (Module vs Integration) to explain to operators

### Neutral

- Migration of existing bindings/schedules to Integrations is deferred
