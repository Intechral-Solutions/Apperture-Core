# Decisions Index

Architecture decision records (ADRs) live in [docs/02-decisions/](../../docs/02-decisions/). This index links to them.

| ADR | Title |
|-----|-------|
| [ADR-0001](../../docs/02-decisions/ADR-0001-runtime-boundaries.md) | Runtime boundaries (control vs instance plane, one-module-per-process) |
| [ADR-0002](../../docs/02-decisions/ADR-0002-single-tenant-instance.md) | Single-tenant instance deployment boundary |
| [ADR-0003](../../docs/02-decisions/ADR-0003-runtime-portability.md) | Runtime portability (portable workflow model, DTFx + Durable Functions adapters) |
| [ADR-0020](../../docs/02-decisions/ADR-0020-operator-output-display-policy.md) | Operator output display policy (intent vs capture status; OperatorRunOutput) |
| [ADR-0021](../../docs/02-decisions/ADR-0021-failure-diagnostics-visibility.md) | Failure diagnostics visibility and capture semantics |
| [ADR-0022](../../docs/02-decisions/ADR-0022-declared-execution-result-contracts.md) | Declared execution result contracts (unified success/failure authoring intent) |
| [ADR-0023](../../docs/02-decisions/ADR-0023-execution-contract-author-guidance-and-validation-signals.md) | Author guidance taxonomy and lightweight validation signals for execution contracts |
| [ADR-0027](../../docs/02-decisions/ADR-0027-connector-definitions-not-platform-clients.md) | Connector definitions vs platform-managed clients (Epic 52 correction; supersedes ADR-0026) |
| [ADR-0028](../../docs/02-decisions/ADR-0028-integration-canonical-deployment-unit.md) | Integration as canonical deployment, binding, and scheduling unit (Epic 54) |
| [Epic 35](../../docs/02-decisions/Epic35-Run-Inspection-Forensics.md) | Run inspection and execution forensics (`ForensicContext`, origin inference, schedule convention, UI cross-nav) |
| [Epic 73](../../docs/02-decisions/Epic73-execution-hot-path-and-host-roles.md) | Execution hot path, host roles, runtime boundary (SQL dispatch, WorkerHost vs SchedulerHost stub, module DbContext) |
