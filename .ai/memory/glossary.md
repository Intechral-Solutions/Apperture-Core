# Glossary

| Term | Definition |
|------|------------|
| **Environment** | The full deployed Apperture platform boundary for a tenant or organization. One customer per Environment. Everything is auditable per environment. (Replaces the overloaded use of "platform instance" for this concept.) |
| **Runtime Plane** | The execution/data-plane subsystem within an Environment (workers, DTFx/Durable Functions, step execution). |
| **Module** | An installable capability type (catalog entry): workflow definitions, step implementations, optional connectors, optional UI. |
| **Module Installation** | A module enabled within an Environment. |
| **Module Instance** | A configured executable occurrence of a Module Installation. One module per worker process (on-prem) or Function App (cloud). Do not use the bare word "instance" ambiguously. |
| **Workflow** | A durable procedure with a step graph, persisted state, and typed parameters. Executed by the runtime. |
| **Step** | A unit of execution within a workflow. The runtime invokes steps in order. |
| **OperationRun** | Top-level record for a workflow execution, surfaced in the UI and audit. |
| **Control plane** | Management API, UI, module registry, execution requests, schedules, audit, telemetry queries. |
| **Emergency administrator** | The single designated Platform Administrator (`Users.IsBreakGlassEligible`) that provides Entra-independent recovery. Signs in with local password plus native MFA or a recovery code. Not a Super Admin role. |
| **Instance plane** | Worker, DTFx/Durable Functions execution, step execution. The "data plane" for workflow runs. (When referring to deployment boundary, prefer "Environment".) |
