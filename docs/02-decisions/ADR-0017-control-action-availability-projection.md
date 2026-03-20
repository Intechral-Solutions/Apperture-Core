# ADR-0017: Control Action Availability Projection

## Status

Accepted

## Context

The Unified Control Facade must show operators which actions are available (or not) in a way that:

- never guesses permissions in the UI,
- respects runtime capability declarations,
- applies state constraints to prevent “no-op” confusion (e.g., terminal executions cannot be cancelled/parked/resumed/retried in the facade),
- preserves separate meanings for control targets vs execution/control request records.

## Decision

Action availability is produced server-side via `ControlActionAvailabilityProjection`:

1. **Authorization-first checks**
   - Start actions use definition-level start authorization semantics (via existing start authorization services or definition query outputs).
   - Execution actions (cancel/retry/park/resume) use the existing “ensure can …” authorization services per run kind.
   - Runtime shutdown request availability checks:
     - capability prerequisite via `RuntimeCapabilityCatalog.ControlShutdown`,
     - and permission evaluation for `runtime.control.shutdown.request` on the runtime instance scope.

2. **State constraints (no-op avoidance)**
   - `cancel`: available only for non-terminal execution states.
   - `retry`: allowed only for terminal failures that are retryable by the workflow retry semantics.
   - `park`: allowed only for non-terminal, not-already-parked workflows; duplication prevented using queued external control request state.
   - `resume`: allowed only for `parked` workflows and only when no queued resume request already exists.

3. **Availability is returned as data**
   - The projection returns `ControlActionAvailabilityDto` entries with:
     - `AvailabilityState` (`available`, `unavailable`, `alreadyQueued`, `notEligible`)
     - and a stable `ReasonCode` for UX and auditability.

## Consequences

- UI becomes a pure data renderer for availability (it never needs to replicate authorization or capability logic).
- Reason codes enable clear operator feedback without exposing governance internals.

