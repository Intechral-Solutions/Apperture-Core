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
3. One-module-per-process compliance
4. Portable workflow model (no backend-specific leakage)
5. Scheduling behavior (triggers only; no step execution)

Identify risks and suggest improvements.
