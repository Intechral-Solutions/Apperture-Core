# Roadmap

## Hosting strategy

**Azure hosting comes after the Windows/on-prem runtime is established and working.** The on-prem golden path (DTFx, Worker, SQL Server) must be solid before we build out the cloud path. Clean interfaces and encapsulation are essential: workflow semantics, persistence, scheduling, and telemetry must be abstracted behind contracts so the Azure/Durable Functions adapter can plug in without coupling to on-prem specifics.

## Phases

1. **Documentation and repo foundation** — Complete. Docs, AI guidance, git config.
2. **Solution skeleton** — Largely complete. Projects, boundaries, dependency rules; compilable solution with arch tests.
3. **Golden path (on-prem)** — In progress / largely delivered. Hello Workflow module, DTFx adapter, Worker, Management API, Management UI. Stabilization (reliability floor, auth, integrations) continues before declaring v1.
4. **Scheduling** — Partially delivered. Quartz backend, schedule management, trigger integration behind `IScheduleTriggerBackend`.
5. **CLI** — Partial. PowerShell module and operational commands exist; expansion continues.
6. **Cloud (Azure)** — Not started for production path. Depends on stable on-prem runtime abstractions.
7. **UI import** — Ongoing. Integration Center and Control Panel Blazor patterns are implemented; visual polish and deferred features remain.
