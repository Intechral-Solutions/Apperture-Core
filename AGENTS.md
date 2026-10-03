# Apperture Core — Agent Instructions

When working in this repository, follow the canonical guidance in:

- **[.ai/playbooks/](.ai/playbooks/)** — Project map, principles, anti-patterns, module SDK, management service, runtime portability
- **[.ai/memory/](.ai/memory/)** — Glossary, decisions index, context pack
- **[docs/01-architecture/rc1-program-gate.md](docs/01-architecture/rc1-program-gate.md)** — The RC1 program contract: decisions, deferred items, epic sequence

## Non-negotiables

1. **Workflow-centric orchestration** — Do NOT design around message handlers or a message bus. Orchestration is explicit: workflow definitions, step graphs, persisted state, runtime services.
2. **Single-tenant Instance** — Each deployment is one Instance (one customer per deployment). Everything is auditable per instance.
3. **Module/process isolation is a supported deployment option, not a current invariant** — Today one Worker Host may host several modules. Do not write code that assumes a module is alone in its process, and do not write code that prevents running one module per worker process (on-prem) or per Function App (cloud) later; a worker module-selection mechanism can provide that topology. Do not restructure the runtime merely to enforce one-module-per-process.
4. **Portable workflow model** — Workflow definitions and step semantics are backend-agnostic. Today's execution path is the SQL start-request queue with inline execution in the worker. Adapters for DTFx (on-prem) and Azure Durable Functions (cloud) are a design intent and are not implemented; do not add backend-specific assumptions to the workflow model.
5. **Scheduling triggers workflows only** — When a schedule fires, the host enqueues a durable start (today: `IWorkflowStartDispatchService` / schedule dispatch runner with an idempotency key). Nothing in the scheduling path executes workflow steps directly.
6. **No internal development metadata in runtime-visible content** — Do NOT put epic IDs, track names (SDK-/SWM-/BDS-/RR-/PFB-), agent notes, migration names, or planning labels into operator-facing display names, seeded records, notification titles/templates, UI labels, settings help text, or sample business data. Those belong in docs, ADRs, audits, tests, migrations, comments, and commits. Prefer product-like names (`Sample API Polling`, `Demo Customers`, `Import completed with errors`). See [.ai/playbooks/anti-patterns.md](.ai/playbooks/anti-patterns.md).

Review `.ai/playbooks/anti-patterns.md` to avoid common mistakes.
