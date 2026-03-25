# Epic 60: Integration Activation, Operational State, and Safe Launch Gating

Status: Accepted  
Date: 2026-03-25

## Context

The integration setup flow now captures truthful configuration, binding, and connection verification state. The remaining gap is operational control: operators need a clear way to pause/resume integration execution without losing configuration, and execution paths must enforce that state consistently.

## Decision

1. Integrations have an explicit operational state with at least:
   - `Active`: operationally allowed
   - `Inactive`: configuration retained, execution paused
2. Readiness and operational state are separate:
   - readiness evaluates whether configuration and dependencies are safe for execution
   - operational state determines whether execution is currently permitted
3. Blocking vs advisory readiness remains explicit:
   - blocking issues prevent launch
   - advisory issues are visible but do not block launch
4. Execution gates are enforced at all integration-scoped entrypoints:
   - procedure launch
   - schedule dispatch
   - manual workflow/operation starts when `integrationId` is provided
5. Operators use explicit activate/deactivate actions from integration surfaces; configuration is preserved.

## Operational semantics

- **Active**
  - Procedure launches may run if no blocking readiness issues exist.
  - Schedule dispatch may enqueue work for integration-owned schedules.
  - Manual starts with `integrationId` may dispatch if integration is active and module-compatible.

- **Inactive**
  - Procedure launch is blocked with deterministic machine-readable reason.
  - Schedule dispatch records blocked lifecycle outcome/reason and does not enqueue work.
  - Manual starts with `integrationId` are rejected deterministically.
  - Existing bindings, connections, settings, and schedules remain intact.

## Blocking vs advisory policy in this epic

- Blocking examples:
  - integration inactive
  - missing/incompatible required bindings
  - missing/inactive/ineligible referenced connection
- Advisory examples:
  - connection verification unverified/failed/unavailable (per Epic 59 policy seam)

## Out of scope

- Module-specific policy customization beyond the existing readiness policy seam.
- Retroactive schedule compensation for skipped inactive windows.
- New generalized lifecycle framework.
- Redesign of procedures, connectors, or verification model.
