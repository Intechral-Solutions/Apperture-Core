# Anti-patterns

## Do not use message-based orchestration

- Do NOT design core workflow execution around message handlers or a message bus.
- Do NOT model workflow steps as handlers in a messaging pipeline.
- Orchestration is explicit: workflow definitions, step graphs, persisted state transitions, runtime services.

## Do not execute steps in the scheduler

- The scheduler triggers workflow starts only (durable enqueue — today `IWorkflowStartDispatchService` / schedule dispatch; not step execution).
- It MUST pass an idempotency key for fire semantics.
- It MUST NOT execute workflow steps, activities, or business logic directly.

## Do not leak execution backend into the workflow model

- Workflow definitions and step semantics are backend-agnostic.
- DTFx-specific or Durable Functions-specific concerns belong in adapters, not in the portable model.

## Do not run multiple modules in one process (on-prem)

- One module per worker process.
- Do not load multiple modules into a single Worker.Host instance.

## Do not mix control plane and instance plane

- Control plane: Management API, UI, registry, execution requests, audit.
- Instance plane: Worker, DTFx/Durable Functions execution, step execution.
- Keep boundaries clear; do not execute workflows from the Management API host.

## Do not put internal development metadata in runtime-visible content

Internal planning labels (epic IDs, track names, agent notes) are useful for engineering traceability in docs, ADRs, audits, tests, migrations, comments, and commit messages. They must **not** leak into the operator/business-facing product experience.

**Not allowed** in:

- Integration, module, procedure, or workflow **display names**
- Notification titles, subjects, summaries, bodies, or definition descriptions intended for operators
- Seeded business/demo records and sample fixture business names
- UI menu labels, page headers, and Developer/Internal card copy that operators see
- Platform setting labels and help text
- Operator-facing messages and toasts

**Allowed** in:

- Docs, ADRs, audits, implementation plans
- Test names and assertions about historical behavior
- Migration class names and technical identifiers
- Code comments explaining historical context
- Commit messages

**Bad (runtime-visible):**

- `SDK-5A ApiPolling Demo`
- `BDS-3 Projection Test Integration`
- `SWM-8 Scheduler Test`
- `Epic 121 HelloWorld`
- `Cursor-created demo`
- `RR-1 readiness sample`
- Setting help: “bypasses Epic 69 orchestrator…”

**Good (product-like):**

- `Sample API Polling`
- `Sample File Import`
- `Demo Customers`
- `Customer Import`
- `Customer Directory Sync`
- `Expected run missing`
- `Import completed with errors`
- Setting help: “uses the platform notification pipeline instead of the legacy attention path…”

If a demo needs uniqueness, use business-context names (`Customer Import Demo`), not epic chronology. Do not rename stable technical IDs, route constants, or migration names just to satisfy this rule — only runtime-facing display values.

Guardrail: `RuntimeVisibleDevMetadataGuardTests` scans curated seed/demo/menu/setting surfaces.
