# Glossary

| Term | Definition |
|------|------------|
| **Instance** | A single-tenant deployment boundary. One customer per Instance. Everything is auditable per instance. |
| **Module** | An installable capability bundle: workflow definitions, step implementations, optional connectors, optional UI. One module per worker process (on-prem) or Function App (cloud). |
| **Workflow** | A durable procedure with a step graph, persisted state, and typed parameters. Executed by the runtime. |
| **Step** | A unit of execution within a workflow. The runtime invokes steps in order. |
| **OperationRun** | Top-level record for a workflow execution, surfaced in the UI and audit. |
| **Control plane** | Management API, UI, module registry, execution requests, schedules, audit, telemetry queries. |
| **Instance plane** | Worker, DTFx/Durable Functions execution, step execution. The "data plane" for workflow runs. |
