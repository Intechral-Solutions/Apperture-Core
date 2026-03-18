# Epic 11 — Workflow Lifecycle Refinement (Summary)

This stage makes the current workflow control baseline more honest by recording and surfacing lifecycle history instead of only the terminal snapshot.

## What was implemented

### Lifecycle vocabulary

- Shared lifecycle status names are centralized in `WorkflowLifecycleStatusNames`
- Shared durable lifecycle event names are centralized in `WorkflowLifecycleEventNames`

### Durable history

- Start dispatch records a `workflow.dispatched` lifecycle event
- Worker execution records `workflow.started`, `workflow.completed`, `workflow.failed`, and `workflow.cancelled`
- Cancel control records `workflow.cancel_requested`
- Retry control records `workflow.retry_requested`
- Worker step execution persists `WorkflowStepInstances`

### Query surface

- `RunQueryService.GetRunAsync` now returns:
  - the current run snapshot
  - the durable lifecycle event trail
  - the per-step execution trail

### Authorization

- Lifecycle refinement does not change the executable-permission model
- Start, cancel, retry, and read remain governed by the same workflow permission fallback rules already in place

## Deferred beyond this stage

- Signal
- Wait/resume/bookmark semantics
- In-place restart or reopening the original run
- Operation execution surfaces
- Cloud runtime adapters
- Rich telemetry/reporting
