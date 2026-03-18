# Epic 12 — Runtime External-Control Recipient Model (Summary)

This stage creates a real runtime-side recipient model for external control so later signal work is not just durable plumbing.

## What was implemented

### Recipient state

- Workflow runs can now enter a durable `parked` state at a safe step boundary
- Parked runs resume from the saved next step instead of restarting from the beginning
- Lease metadata is released again when a run reaches a terminal state

### Durable control inbox

- `WorkflowExternalControlRequests` stores queued external control requests
- Park and resume requests are durably recorded with request/apply status
- The worker consumes queued control requests at safe execution boundaries

### Query surface

- `RunQueryService.GetRunAsync` now returns:
  - the current run snapshot
  - the current step pointer
  - the lifecycle event trail
  - the per-step execution trail
  - the external-control request trail

### Authorization

- Park/resume use the existing executable-permission fallback model
- The new recipient model does not add signal semantics by itself

## Deferred beyond this stage

- Signal endpoints
- Waiting/bookmark/inbox semantics beyond the parked/resumed workflow control model
- In-place restart or reopening the original run
- Operation execution surfaces
- Cloud runtime adapters
- Rich telemetry/reporting dashboards
