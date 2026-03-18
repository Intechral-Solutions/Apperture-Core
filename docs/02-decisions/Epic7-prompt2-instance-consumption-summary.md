---
title: Epic 7 Prompt 2 — Instance-plane start request consumption
status: implemented
date: 2026-03-17
---

## Summary

Epic 7 Prompt 2 makes the **instance plane** consume the persisted workflow start handoff created in Prompt 1 and drive a minimal real execution lifecycle.

## What changed

- **Durable handoff consumption**: A Windows worker host background service (`StartRequestWorker`) now polls `WorkflowStartRequests` and processes pending rows.
- **Safe claiming**: `WorkflowStartRequests` now has `Status`, `ClaimedAt`, `ClaimedBy`, `LastError` (plus `RowVersion`) so workers can claim work and avoid duplicate processing.
- **Minimal execution lifecycle**:
  - Start request: `requested → running → completed/failed`
  - WorkflowInstance: `requested → running → completed/failed`
  - OperationRun: `requested → running → completed/failed` (sets `CompletedAt`)
- **Execution path**: The worker bridges to the existing runtime engine via `IWorkflowExecutor` (default: `DefaultWorkflowExecutor`) and executes steps sequentially.

## Worker flow (high level)

1. Poll for `WorkflowStartRequests` where `Status == requested` and `ClaimedAt == null`.
2. Claim a row atomically (SQL `UPDATE ... WHERE ...`).
3. Load the associated `WorkflowInstance` and `OperationRun`.
4. Transition instance/run to `running`.
5. Resolve the workflow definition and execute each step via `IWorkflowExecutor`.
6. Persist `completed` or `failed` status across request/run/instance.

## Corrective pass notes (baseline safety)

- **Recoverable claiming**: a claimed request is now treated as a lease. If a worker crashes after claiming, the request becomes eligible for reclaim after a timeout and another worker can pick it up.
- **Discovery includes stale running**: the poll loop now considers both unclaimed `requested` requests and stale `running` requests with expired leases.
- **Lease renewal (Epic 8 Prompt A)**: while executing, the lease owner renews `ClaimedAt` at step boundaries so long-running execution does not become reclaimable solely due to elapsed time.
- **Ownership enforcement (Epic 8 Prompt A)**: processing enforces `ClaimedBy` ownership; if a worker loses ownership (successful reclaim by another worker), the old worker stops safely without writing terminal `completed/failed` state.

## Current scope / limitations

- **Definition resolution is minimal**: the processor currently recognizes `Sample.HelloWorld.HelloWorld` to prove the end-to-end slice. This should be replaced with a real module-based definition resolver when module hosting is wired into the worker host.
- **Prompt B update**: worker-side definition resolution now enumerates DI-registered `IModule` definitions (via `GetDefinitionsAsync()`), and step handler registration is module-driven via `IModuleStepHandlerRegistrar`. WorkerHost also validates collisions/ownership at startup for safety under multi-module dev/test composition.

## Deferred beyond Epic 7

- Signal
- Operation execution runtime
- Richer lifecycle and retries/backoff/poison-queue behavior
- Cloud runtime adapters (Durable Functions) beyond the current stubbed path
- Rich execution telemetry and UI surfaces
- Prompt B: generic worker-side definition resolution and module-driven step handler registration

