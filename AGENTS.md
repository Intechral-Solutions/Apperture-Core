# Apperture Core — Agent Instructions

When working in this repository, follow the canonical guidance in:

- **[.ai/playbooks/](.ai/playbooks/)** — Project map, principles, anti-patterns, module SDK, management service, runtime portability
- **[.ai/memory/](.ai/memory/)** — Glossary, decisions index, context pack

## Non-negotiables

1. **Workflow-centric orchestration** — Do NOT design around message handlers or a message bus. Orchestration is explicit: workflow definitions, step graphs, persisted state, runtime services.
2. **Single-tenant Instance** — Each deployment is one Instance (one customer per deployment). Everything is auditable per instance.
3. **One module per compute process** — On-prem: one module per worker process. Cloud: one module per Azure Function App.
4. **Portable workflow model** — Workflow definitions and step semantics are backend-agnostic. Adapters exist for DTFx (on-prem) and Azure Durable Functions (cloud).
5. **Scheduling triggers workflows only** — When a schedule fires, the host enqueues a durable start (today: `IWorkflowStartDispatchService` / schedule dispatch runner with an idempotency key). Nothing in the scheduling path executes workflow steps directly.

Review `.ai/playbooks/anti-patterns.md` to avoid common mistakes.
