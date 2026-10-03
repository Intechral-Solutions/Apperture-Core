# Context Pack

Quick context for agents working in Apperture Core.

## What is Apperture?

Apperture Core is an open-core automation/integration platform whose core is a durable, persistent workflow model. It supports on-prem (DTFx + SQL Server) and cloud (Azure Durable Functions) runtimes.

## Non-negotiables

1. Workflow-centric orchestration (not message-based)
2. Single-tenant Instance per deployment
3. Module/process isolation is a supported deployment option, not a current invariant (one worker may host several modules today)
4. Portable workflow model; DTFx and Durable Functions adapters are a design intent and are not implemented (today: SQL start-request queue with inline execution)
5. Scheduling triggers workflows only; never executes steps

## Current phase

**Product foundation baseline complete; major pre-release product development ahead** — not release-ready. Stabilization S1–S13 and Scheduling & Worker Management SWM-1–SWM-12 are closed. The solution includes Management Host/API, Blazor UI, worker/runtime hosts, SQL persistence, auth/RBAC, integrations, connections, notifications, schedule dispatch, and test coverage (Aspire/Manage.ps1 operable path). See [docs/00-overview/roadmap.md](../../docs/00-overview/roadmap.md), [docs/00-overview/pre-release-product-roadmap.md](../../docs/00-overview/pre-release-product-roadmap.md), and [docs/audits/pfb1-product-foundation-baseline-audit.md](../../docs/audits/pfb1-product-foundation-baseline-audit.md).
