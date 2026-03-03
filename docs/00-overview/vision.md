# Vision

Apperture Core is an open-core automation and integration platform built around a **durable, persistent workflow model**.

## Goals

- **Consistent logging and telemetry** — Correlation IDs, standardized events, traceability
- **Auditable operations** — Who did what, when; workflow run history; step-level status
- **Clean boundaries** — Control plane (management, API, UI) vs instance plane (execution, Worker, DTFx)
- **Portable execution** — Same workflow model runs on-prem (DTFx + SQL Server) and in cloud (Azure Durable Functions)

## Design

The platform is designed for **single-tenant Instance** deployments: one customer per deployment. Everything is traceable and auditable per instance. Workflow orchestration is explicit (definitions, step graphs, persisted state), not message-based.

See [roadmap.md](roadmap.md) for phasing and [docs/01-architecture/](../01-architecture/) for detailed architecture.
