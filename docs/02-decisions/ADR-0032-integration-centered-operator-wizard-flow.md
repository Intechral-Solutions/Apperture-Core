# ADR-0032: Integration-centered authoring and remediation wizard flow

## Status

Accepted

## Context

Epics 52-56C established truthful building blocks (connector definitions, connections, integrations, readiness, schedules, procedures, provenance).  
Operator workflow remained fragmented across separate pages and APIs, making it harder to complete setup as one deployment journey.

Epic 57 requires a coherent integration-centered flow:

1. Create integration from a module
2. Bind required slots to compatible connections
3. Resolve readiness blockers
4. Attach schedules (optional behavior)
5. Launch module procedures

## Decision

1. **Integration stays the control-plane center**
   - Integration detail remains the source of truth for readiness, required slots, bindings, schedules, and procedures.
   - We continue to reuse existing APIs (`/integrations/{id}/detail`, bindings/schedules/procedures routes) rather than introducing parallel abstractions.

2. **Wizard UX becomes a reusable subsystem**
   - Management UI adds reusable wizard primitives (`WizardFlowController`, `WizardStepDefinition`, `WizardStepState`, `WizardFlowPanel`).
   - Wizard state is deterministic and derived from server truth (readiness projection, slots, bindings, procedures), not client-only assumptions.
   - Epic 57 applies this subsystem to integration authoring first; other management flows may adopt it later.

3. **Readiness remediation is actionable**
   - Readiness issues map to deterministic remediation cues and actions (open add-binding, open add-schedule, navigate to connection, activate integration).
   - We do not build a workflow engine; issue-code mappings stay explicit and auditable.

4. **Schedules are integration-owned but optional for readiness**
   - Schedules are shown and managed in integration context.
   - Absence of schedules does not block `IsRunnable` in Epic 57 unless future module contracts declare schedule prerequisites.

## Out of scope

- Connector/procedure redesign
- Generic connector operation surface
- Sensitive-field model for procedure parameters
- Full workflow designer

## Consequences

### Positive

- Operators can follow one coherent setup journey without hunting across unrelated screens.
- The wizard pattern is reusable for future management workflows while still grounded in truthful API projections.

### Negative

- UI now maintains an explicit issue-to-remediation mapping table that must stay in sync with readiness codes.

### Neutral

- Extends ADR-0025/0028/0029/0030/0031 without changing domain boundaries.
