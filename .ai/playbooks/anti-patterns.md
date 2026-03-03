# Anti-patterns

## Do not use message-based orchestration

- Do NOT design core workflow execution around message handlers or a message bus.
- Do NOT model workflow steps as handlers in a messaging pipeline.
- Orchestration is explicit: workflow definitions, step graphs, persisted state transitions, runtime services.

## Do not execute steps in the scheduler

- The scheduler triggers workflow starts only.
- It MUST call `IWorkflowStarter.StartAsync(...)` and pass an idempotency key.
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
