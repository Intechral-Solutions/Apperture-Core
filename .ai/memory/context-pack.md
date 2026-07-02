# Context Pack

Quick context for agents working in Apperture Core.

## What is Apperture?

Apperture Core is an open-core automation/integration platform whose core is a durable, persistent workflow model. It supports on-prem (DTFx + SQL Server) and cloud (Azure Durable Functions) runtimes.

## Non-negotiables

1. Workflow-centric orchestration (not message-based)
2. Single-tenant Instance per deployment
3. One module per compute process
4. Portable workflow model with DTFx and Durable Functions adapters
5. Scheduling triggers workflows only; never executes steps

## Current phase

Implementation is underway and stabilizing toward a narrow v1. The solution includes Management Host/API, Blazor UI, worker/runtime hosts, SQL persistence, auth/RBAC, integrations, connections, notifications, scheduling hooks, and test coverage. See [docs/00-overview/roadmap.md](../../docs/00-overview/roadmap.md) for phase status.
