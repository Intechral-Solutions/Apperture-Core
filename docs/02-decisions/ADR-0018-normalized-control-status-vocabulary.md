# ADR-0018: Normalized Control Status Vocabulary

## Status

Accepted

## Context

Operators need top-level monitoring that is:

- consistent across operations and workflows,
- consistent for durable runtime control requests,
- normalized enough to support a single activity feed UI,
- but not flattened permanently in persistence or domain state machines.

## Decision

Introduce a small, stable facade-normalization vocabulary for top-level monitoring:

- `requested`, `running`, `parked`, `succeeded`, `failed`, `cancelled`

Normalization rules:

- Workflow lifecycle statuses map into the facade vocabulary.
- Execution outcome summaries are derived from the normalized status (succeeded/failed/cancelled in particular).
- Durable runtime shutdown control requests map queued/applied/rejected/failed into the facade vocabulary.

Preservation of domain detail:

- Domain-native detail remains drill-down-only (e.g., run detail pages and runtime control request detail pages).
- The facade provides links (`DetailLink`) to keep drill-down routes stable and domain-native.

## Consequences

- Top-level operator monitoring becomes cohesive and predictable.
- The facade vocabulary remains narrow so it does not force underlying domain state machines to converge.

