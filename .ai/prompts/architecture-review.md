# Architecture Review Prompt

Use this prompt when requesting an architecture review of a design or change.

## Context

- Project: Apperture Core — durable workflow automation platform
- Principles: [.ai/playbooks/principles.md](../playbooks/principles.md)
- Anti-patterns: [.ai/playbooks/anti-patterns.md](../playbooks/anti-patterns.md)

## Request

Review the proposed architecture/change for:

1. Alignment with workflow-centric orchestration (no message-based design)
2. Correct control plane vs instance plane boundaries
3. Module/process isolation (a supported deployment option, not a current invariant: nothing may assume a module is alone in its process, and nothing may prevent running one module per worker later; AGENTS.md rule 3)
4. Portable workflow model (no backend-specific leakage; DTFx and Durable Functions adapters are a design intent and are not implemented, today's path is the SQL start-request queue with inline worker execution; AGENTS.md rule 4)
5. Scheduling behavior (triggers only; no step execution)

Identify risks and suggest improvements.
