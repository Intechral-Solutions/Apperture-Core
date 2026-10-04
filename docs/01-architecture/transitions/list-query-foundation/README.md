# LST-1 — List and query foundation

Working records for the RC1 list and query foundation (program gate decision D-18, section 6.1). LST-1 is the RC1 layer. The richer table presentation layer is LST-2 and is post-RC1.

| Document | Status |
|---|---|
| [lst1-design-gate.md](lst1-design-gate.md) | **LST-1.0 design gate, approved by the product owner with amendments (2026-10-03).** Paging contracts, query lifecycle, the empty-state invariant, URL state, list-state view, pager footer, truncation, Runs keyset design, audit cursor invariant, `AsyncSearchSelect`, team-selector seam, surface classification, test matrix and package decomposition. Section 30 records the answers to Q1 to Q9 and the five amendments. LST-1.2a (run-derived signal authorization fill) is added by the review |

No list or query product code has been written. LST-1.1 awaits an explicit start instruction, and no LST-1 implementation branch exists.

Governing contract: [rc1-program-gate.md](../../rc1-program-gate.md), decisions D-16 to D-20 and section 6.1. Evidence: the [Part 1 audit](../../../audits/rc1-architecture-audit-and-program-plan.md) and its [supplemental addendum](../../../audits/rc1-supplemental-reconciliation-addendum.md). Where they differ from the program gate, the gate governs. Where the gate's section 4.1 differs from the addendum on a code fact, the design gate states the verified fact.

When the LST-1 packages ship, fold the outcome into an ADR and the architecture docs, then archive this set (see the [transitions index](../README.md)).
