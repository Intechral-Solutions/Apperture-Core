# docs/superpowers: completed plans and specs (historical, not instructions)

This folder holds **implementation plans and design specs that were written for work already done**. They are kept as a record of what was planned and why. **They are not active instructions.** Do not start work, tick boxes, or "finish" a plan because it is here.

| Question | Answer |
|---|---|
| Is a plan in `plans/` a to-do list? | No. The development-loop audit (2026-10-29, finding E.4) found these 29 plans completed. Many of them leave every `- [ ]` checkbox unticked even though the audit found them completed, so an unchecked box does **not** mean outstanding work. This index does not re-audit each plan's delivery. |
| Do the "For agentic workers: REQUIRED SUB-SKILL ..." headers apply to me? | No. They were addressed to the agent that executed that plan at the time. Follow [AGENTS.md](../../AGENTS.md) and [.ai/playbooks/](../../.ai/playbooks/dev-loop.md) now. |
| Where do current rules and decisions live? | [AGENTS.md](../../AGENTS.md), [.ai/](../../.ai/README.md), [docs/02-decisions/](../02-decisions/), the [RC1 program gate](../01-architecture/rc1-program-gate.md), and the [DEV-1 plan](../01-architecture/transitions/dev-loop/dev1-development-loop-plan.md). If a plan here disagrees with one of those, the current document wins. |
| Can I rely on the paths, names and commands inside a plan? | Only after checking them against the repository. They describe the tree as it was on the plan's date. Several plans use planning labels (epic and track IDs) that must **not** appear in operator-visible product content (AGENTS.md rule 6); they belong in documents like these. |
| How do I add a new plan or spec here? | Prefer the package's own record under `docs/01-architecture/transitions/<area>/` (template: [work-package record](../../.ai/prompts/work-package-record.md)). If you do add one, start it with a `Status:` line (`Planned`, `In progress`, `Implemented`, `Superseded by <link>`) and update it when the work lands. Do not rewrite the historical plans below. |

## Index

"Self-stated status" is what the file itself says (nothing is inferred). Many plans state none.

### Plans (`plans/`)

| Series | Files | Self-stated status |
|---|---|---|
| Module SDK sample and itemization, July 2026 (2, 3A to 3D) | [sdk2](plans/2026-07-15-sdk2-sample-fileimport.md), [sdk3a](plans/2026-07-15-sdk3a-operation-itemization.md), [sdk3b](plans/2026-07-15-sdk3b-operation-itemization-read-apis.md), [sdk3c](plans/2026-07-15-sdk3c-fileimport-operation-itemization.md), [sdk3d](plans/2026-07-15-sdk3d-run-detail-operation-itemization-panel.md) | none stated; 3B and 3C tick every box, 3D leaves 2 unticked |
| Module SDK retry (4A to 4C) | [sdk4a](plans/2026-07-16-sdk4a-operation-item-retry-dispatch.md), [sdk4b](plans/2026-07-17-sdk4b-retry-attempt-outcome-reconciliation.md), [sdk4c](plans/2026-07-17-sdk4c-run-detail-per-item-retry-ui.md) | `Implemented` |
| Module SDK API polling (5A to 5C) | [sdk5a](plans/2026-07-18-sdk5a-sample-apipolling.md), [sdk5b](plans/2026-07-19-sdk5b-apipolling-durable-sync-state.md), [sdk5c](plans/2026-07-20-sdk5c-apipolling-failed-record-retry.md) | none stated |
| Shared record reverse links | [plan](plans/2026-08-05-shared-record-reverse-links.md) | none stated |
| Application contexts, CTX-2 | [ctx2](plans/2026-09-11-ctx2-completion.md) | "core invariant implemented and proven; scope trimmed under session budget"; boxes unticked |
| Application contexts, CTX-3A (6 plans) | [01](plans/2026-09-13-ctx3a-01-contracts-and-domain.md), [02](plans/2026-09-13-ctx3a-02-authorization-and-management-api.md), [03](plans/2026-09-13-ctx3a-03-config-ui-and-shell-selector.md), [04](plans/2026-09-13-ctx3a-04-scoped-surfaces-and-detail.md), [05](plans/2026-09-13-ctx3a-05-creation-audit-business-data.md), [06](plans/2026-09-13-ctx3a-06-control-center-and-docs.md) | 06: "Complete as an internal engineering milestone"; others none stated; boxes unticked |
| Application contexts, CTX-3B (8 plans) | [01](plans/2026-09-15-ctx3b-01-preview-validator-and-contracts.md), [02](plans/2026-09-15-ctx3b-02-transactional-move-and-locking.md), [03](plans/2026-09-15-ctx3b-03-readiness-defense-in-depth.md), [04](plans/2026-09-15-ctx3b-04-movement-audit.md), [05](plans/2026-09-15-ctx3b-05-api-endpoints-and-movement-ui.md), [06a](plans/2026-09-15-ctx3b-06a-integration-referential-lifecycle.md), [06a stabilization](plans/2026-09-15-ctx3b-06a-stabilization.md), [06b](plans/2026-09-15-ctx3b-06b-closure.md) | none stated; 06b's status line is an unfilled template placeholder; boxes unticked |
| Housekeeping (HSK-A, HSK-B) | [hsk-a](plans/2026-09-19-hsk-a-operator-polish.md), [hsk-b](plans/2026-09-19-hsk-b-movement-dialog-ux.md) | none stated; boxes unticked |

### Specs (`specs/`)

| File | Self-stated status |
|---|---|
| [Shared record reverse links design](specs/2026-08-05-shared-record-reverse-links-design.md) | `Approved` |
| [SDK-2 Sample FileImport demo](specs/SDK-2-Sample-FileImport-Demo.md) | `Implemented (SDK-2)` |
| [SDK-5A Sample ApiPolling demo](specs/SDK-5A-Sample-ApiPolling-Demo.md), [SDK-5B](specs/SDK-5B-ApiPolling-Durable-Sync-State.md), [SDK-5C](specs/SDK-5C-ApiPolling-Failed-Record-Retry.md), [SDK-6A](specs/SDK-6A-Integration-Sdk-Contract-Consolidation.md) | `Implemented` |

The historical plans and specs themselves have **not** been edited. A status marker added to one of them would change its provenance; correct the index here instead.
