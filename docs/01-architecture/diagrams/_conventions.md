# Architecture Diagram Conventions

All diagrams under `docs/01-architecture/c4/` and `docs/01-architecture/diagrams/` follow these conventions. See also `_conventions.drawio` for the visual legend.

## File naming

- `NN-short-name.current.drawio` — implemented or partial state today
- `NN-short-name.projected.drawio` — future/target state
- C4 diagrams use prefix `c4-` (e.g. `c4-01-system-context.current.drawio`)

## Status legend (current variants)

| Status | Fill | Stroke | Border |
|--------|------|--------|--------|
| **Implemented** | `#dae8fc` | `#6c8ebf` | solid |
| **Partial** | `#fff2cc` | `#d6b656` | solid |
| **Stubbed / Not wired** | `#f5f5f5` | `#666666` | dashed |

Future-only elements **never** appear in current variants.

## Projection legend (projected variants)

| Meaning | Fill | Stroke | Border |
|---------|------|--------|--------|
| **Existing today** | per status above | per status above | solid |
| **New / changed** | `#d5e8d4` | `#82b366` | solid |
| **Not yet wired** | — | — | dashed edge |

## Plane colors

| Plane / layer | Fill | Stroke |
|---------------|------|--------|
| **Control Plane** | `#dae8fc` | `#6c8ebf` |
| **Instance Plane** | `#d5e8d4` | `#82b366` |
| **Foundation** | `#fff2cc` | `#d6b656` |
| **Shared / Core** | `#e1d5e7` | `#9673a6` |
| **External** | `#f5f5f5` | `#666666` |
| **Runtime Plane** | `#ffe6cc` | `#d79b00` |

## Edge styles

| Style | Use |
|-------|-----|
| `edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;` | Structural relationships |
| `dashed=1;` | Future, not wired, or optional paths |
| Labeled edges | Protocols: HTTP, SQL enqueue, lease claim, OIDC, SMTP |

## Containers

- Use nested `swimlane` containers for planes and hosts
- Cross-container edges use `parent="1"`
- `pointerEvents=0` on containers that should not capture connections

## Source of truth

- **Current state:** [current-architecture-tracking.md](../current-architecture-tracking.md)
- **Decisions:** [docs/02-decisions/](../../02-decisions/)
- **Architecture docs:** [docs/01-architecture/](../)
