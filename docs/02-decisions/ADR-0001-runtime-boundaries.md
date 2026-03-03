# ADR-0001: Runtime boundaries (control vs instance plane, one-module-per-process)

## Status

Accepted

## Context

We need clear separation between management concerns (control plane) and workflow execution (instance plane). We also need isolation boundaries for reliability and deployment.

## Decision

1. **Control plane vs instance plane** — The control plane (Management API, UI, registry, schedules, audit) does not execute workflows. It issues requests. The instance plane (Worker, DTFx or Durable Functions) executes workflows and reports status.

2. **One module per process** — On-prem: one module per worker process. Cloud: one module per Azure Function App. Modules are trusted; no sandboxing. This is a core isolation boundary.

## Consequences

### Positive

- Clear responsibility boundaries
- Simplified deployment and scaling
- Easier to reason about failures

### Negative

- More processes/containers to manage on-prem
- One Function App per module in cloud (cost/management consideration)

### Neutral

- Scheduler may run in Management Host initially or as separate Scheduler Host
