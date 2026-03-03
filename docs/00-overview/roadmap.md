# Roadmap

## Hosting strategy

**Azure hosting comes after the Windows/on-prem runtime is established and working.** The on-prem golden path (DTFx, Worker, SQL Server) must be solid before we build out the cloud path. Clean interfaces and encapsulation are essential: workflow semantics, persistence, scheduling, and telemetry must be abstracted behind contracts so the Azure/Durable Functions adapter can plug in without coupling to on-prem specifics.

## Phases

1. **Documentation and repo foundation** — Current. Docs, AI guidance, git config.
2. **Solution skeleton** — Projects, boundaries, dependency rules. Compilable. Enforce clean interfaces for runtime, persistence, and scheduling.
3. **Golden path (on-prem)** — Hello Workflow module, DTFx adapter, Worker, Management API, minimal UI. Windows/on-prem must be working end-to-end before cloud work begins.
4. **Scheduling** — Quartz backend, schedule management, trigger integration. Implemented behind `IScheduleTriggerBackend` for future cloud variants.
5. **CLI** — PowerShell module, doctor, module new, workflow, schedule commands.
6. **Cloud (Azure)** — Durable Functions adapter, Function App template. Only after on-prem runtime is stable. Depends on clean encapsulation in runtime abstractions.
7. **UI import** — Reproduce Integration Center Blazor WASM patterns and features.
