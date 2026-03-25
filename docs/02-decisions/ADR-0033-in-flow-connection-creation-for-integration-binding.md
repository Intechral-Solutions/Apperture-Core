# ADR-0033: In-flow connection creation for integration binding

## Status

Accepted

## Context

Epic 57 made integration setup coherent (create integration -> bind slots -> remediate readiness -> schedules -> procedures), but a key seam remained:

- when a required slot has no compatible connection, operators had to leave integration flow, create a connection elsewhere, then return to bind.

This added navigation friction and broke remediation momentum.

## Decision

1. **Integration remains the operator journey center**
   - Operators may create a missing connection directly inside integration slot-binding flow.
   - The flow immediately continues to bind and readiness remediation.

2. **Connections remain first-class platform entities**
   - In-flow creation calls the same connection create API and persists normal `Connection` entities.
   - No integration-specific shadow connection type is introduced.

3. **Connector definitions remain the form source**
   - In-flow connection forms are metadata-driven from connector definition fields (including secret vs non-secret shape).
   - No ad hoc field model is introduced.

4. **Compatibility must be truthful**
   - Eligible connection listing for a slot must enforce compatibility checks deterministically (connector definition + contract constraints), not just return all active connections.
   - Create-and-bind flow uses that same eligibility truth.

## Out of scope

- Connector-definition redesign
- Integration or procedure model redesign
- Generic connection-management UX overhaul outside integration remediation

## Consequences

### Positive

- Removes the most obvious integration remediation seam.
- Preserves domain truth (first-class connections, connector-definition-driven forms).
- Keeps readiness remediation actionable and in-context.

### Negative

- Integration detail now owns additional UI state for in-flow connection creation.

### Neutral

- Extends ADR-0032 integration-centered workflow without changing platform boundaries.
