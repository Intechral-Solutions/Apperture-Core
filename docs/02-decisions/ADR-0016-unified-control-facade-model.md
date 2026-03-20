# ADR-0016: Unified Control Facade Model

## Status

Accepted

## Context

The platform now has strong domain foundations for operations/workflows, durable runtime identities, and governed runtime control requests. The remaining operator pain is a fragmented experience: the UI mirrors internal slices (runs vs runtimes vs governance vs schedules) instead of presenting a cohesive “control system” view.

We need an application/read-model layer that:

- unifies top-level operator concepts (targets, activity, attention),
- keeps governance and authorization real (server-side projection, not UI guessing),
- supports drill-down into domain-native detail pages,
- integrates schedules only as a disciplined overlay (no evaluation/dispatch subsystem in this cycle).

## Decision

Introduce a Unified Control Facade composed of three permission-safe projections:

1. `ControlTargetSummary` with a small `ControlTargetKind` vocabulary:
   - `operation`, `workflow`, `runtimeInstance`, and `schedule` (overlay shell).
2. `ControlActivitySummary` providing a normalized facade activity vocabulary:
   - executions and durable runtime control requests share common status/outcome fields,
   - `ActivitySource` distinguishes `direct` vs `scheduled` overlay classification,
   - drill-down is preserved via `DetailLink`.
3. `ControlAttentionItem` providing minimal cross-domain triage units:
   - failed executions,
   - queued-but-stuck runtime shutdown requests,
   - rejected workflow external control requests.

Schedule integration uses a correlation overlay:

- if correlation includes `schedule:{scheduleId}`, facade classifies the activity as `ActivitySource=scheduled` and attaches it to a `schedule` target kind.

## Consequences

### Positive

- The operator top-level model becomes cohesive without conflating distinct domains.
- Authorization remains server-enforced via existing “ensure” services and capability inspection.
- Drill-down preserves domain-native detail and reduces the risk of “flat monolith” UI modeling.

### Negative

- Schedule integration is inference-based (correlation overlay); schedule evaluation/dispatch remains out of scope.

### Neutral

- The facade is explicitly a read-model/projector; persistence and execution semantics remain domain-owned.

