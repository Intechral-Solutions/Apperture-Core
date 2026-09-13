# Management UI — design system catalog (working)

This folder holds the **working inventory** for the site-wide theme and layout overhaul. It is agent/developer planning material, not operator-facing copy.

## Canonical end product

Normative guidance lives in **[docs/01-architecture/management-ui-design-system.md](../../docs/01-architecture/management-ui-design-system.md)**. Update that document as patterns stabilize. This catalog feeds it; do not duplicate long prose here.

## Files

| File | Purpose |
|------|---------|
| [catalog-schema.md](catalog-schema.md) | Field definitions and promotion rules |
| [pattern-catalog.md](pattern-catalog.md) | Living inventory of patterns, components, and page instances |
| [disclosure-audit.md](disclosure-audit.md) | Progressive-disclosure opportunities (updated per batch) |

## Workflow (each page batch)

1. Touch pages → add or update rows in `pattern-catalog.md`.
2. Note **interesting variations**; decide merge vs justify vs new primitive.
3. If a row appears on **3+ routes**, promote fix-once (shared molecule or primitive) in the same batch.
4. Fold stable patterns into `management-ui-design-system.md` (controls, patterns, anti-patterns).
5. Log disclosure candidates in `disclosure-audit.md` when restructuring pages.

## Maturity labels

| Label | Meaning |
|-------|---------|
| **reference** | Signed off; copy this implementation |
| **pilot** | Epic 79–97 structure; needs visual/token harmonization |
| **legacy** | Pre-pilot markup; scheduled for migration |
| **candidate** | Proposed primitive or pattern; not yet codified |
