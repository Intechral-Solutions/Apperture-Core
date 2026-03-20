# Unified Control Facade

The Unified Control Facade is an application/read-model layer that projects a cohesive operator control experience over existing domain slices (operations/workflows/schedules/runtimes/control requests/run history).

## Goals

- Provide a stable top-level operator shell that does not mirror internal domain boundaries.
- Keep governance and authorization real: the facade never “guesses” permissions or capability constraints in the UI.
- Preserve drill-down: top-level normalization should not erase domain-specific detail.

## Facade Concepts

### Control Targets

Top-level “things the operator can act on” are projected as `ControlTargetSummary` entries:

- `operation` — operation definitions with a server-projected start action availability.
- `workflow` — workflow definitions with a server-projected start action availability.
- `runtimeInstance` — runtime identities with a server-projected governed runtime shutdown request action.
- `schedule` — schedule targets projected via scheduled-activity correlation (minimal shell; no schedule evaluation/dispatch in this cycle).

### Unified Activity

Operator monitoring uses `ControlActivitySummary` entries with a normalized vocabulary:

- A shared status/outcome summary for executions and durable runtime control requests.
- A common “detail link” that keeps drill-down routes domain-native (run detail pages and runtime control request pages).
- An `ActivitySource` field that distinguishes `direct` execution vs `scheduled` overlay classification.

### Attention Items

`ControlAttentionItem` entries are small cross-domain triage units.
They are permission-safe (items are included only when the caller is authorized to observe the underlying domain facts).

## Schedule Integration (Overlay, Not Conflation)

For this cycle, scheduled execution is integrated using a correlation convention:

- When an activity/run includes a correlation token `schedule:{scheduleId}`, the facade classifies the activity as `ActivitySource=scheduled`.
- Scheduled activities are attached to a `schedule` target kind for top-level grouping.
- This facade does not implement schedule evaluation or dispatch; it projects what can be inferred from correlation.

## Server-Side Action Availability

Action availability is projected server-side (`ControlActionAvailabilityProjection`) using:

- authorization evaluation seams (run/action-specific “ensure” services),
- runtime capability inspection (`RuntimeCapabilityCatalog`),
- and state constraints (terminal vs non-terminal; parked-only resume; queued-only resume/park duplication prevention).

The UI consumes availability as data and renders it as “available/unavailable” with reason codes.

## Primary Facade Endpoints

- `GET /api/control/center` — Targets + recent activity + attention items.
- `GET /api/control/activity` — Unified activity feed (executions + durable runtime control requests).
- `GET /api/control/attention` — Cross-domain triage items.

