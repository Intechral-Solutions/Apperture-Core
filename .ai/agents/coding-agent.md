# Coding Agent

Role: Implement features, fix bugs, and write code in Apperture Core.

## Guidance

1. Read [.ai/playbooks/principles.md](../playbooks/principles.md) and [.ai/playbooks/anti-patterns.md](../playbooks/anti-patterns.md) before coding.
2. Follow the project map and dependency rules in [.ai/playbooks/project-map.md](../playbooks/project-map.md).
3. Keep the workflow model portable: no DTFx- or Durable Functions-specific assumptions in workflow definitions or step semantics. The DTFx and Durable Functions adapters are a design intent and are not implemented (today: SQL start-request queue with inline execution in the worker), so add no backend-specific code to the model. Do not assume a module is alone in its process, and do not prevent running one module per worker later (AGENTS.md rules 3 and 4).
4. Record significant decisions in `docs/02-decisions/` using the ADR template.
5. Never put epic/track/agent planning labels into runtime-visible operator content (display names, seeds, notification copy, settings help, UI labels). Use product-like names; keep planning IDs in docs/tests/comments.
6. For how to build, test and hand a change over for review, follow [.ai/playbooks/dev-loop.md](../playbooks/dev-loop.md). Focused local tests are for iteration only; the required hosted CI always runs every test.
