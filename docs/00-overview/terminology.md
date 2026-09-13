# Terminology

See [.ai/memory/glossary.md](../../.ai/memory/glossary.md) for the canonical glossary. Key terms:

## Platform and trust boundary

- **Platform** — The whole Apperture system.
- **Instance** — One deployed Apperture installation / trust boundary.
- **Environment** — Same trust boundary as **Instance**; preferred term in the canonical glossary for one customer per deployment.

## Planes

- **Control Plane** — Management API, UI, and governance/configuration authority (module registry, schedules, audit).
- **Runtime** — The execution side of the platform (workers, durable workflow execution, step execution).
- **Runtime Plane** — Same scope as **Runtime**; execution/data-plane within an Instance/Environment.
- **Instance plane** — Legacy alias for the execution side; prefer **Runtime** in new docs.

## Workers and topology

- **Worker** — A process that executes runtime work.
- **Worker Node** — A specific live/registered worker process.
- **Worker Pool** — A logical group of workers with common purpose or capabilities.
- **Capability** — A declared thing a worker can execute or provide.
- **Assignment** — A rule binding work, modules, or scopes to eligible workers or pools.
- **Runtime Resource** — A dependency or component in the runtime topology.
- **Binding** — Association between runtime/module parameter needs and configured resources.

## Work lifecycle

- **Work Item** — A unit of pending executable work.
- **Claim** — A worker’s reservation of a work item.
- **Lease** — Time-bound ownership of a claim.
- **Run** — The durable execution record of work being performed.
- **Schedule** — A rule that creates work over time.
- **Expected Run** — A schedule-derived expectation used for diagnostics.

## Modules and workflows

- **Module** — Installable package (catalog entry) that contributes runtime capabilities and UI/control-plane artifacts.
- **Module Installation** — Module enabled in an Instance/Environment.
- **Module Instance** — Configured executable occurrence of a Module Installation (e.g. one worker or Function App).
- **Workflow** — Durable procedure with step graph and persisted state.
- **Step** — Unit of execution within a workflow.
- **Procedure** — Module-declared operator action: stable id, metadata, launch kind (workflow or operation), target definition id, and optional JSON parameter schema. Exposed under the integration-scoped Management API when the integration is runnable; not a generic connector operation catalog (see [ADR-0027](../02-decisions/ADR-0027-connector-definitions-not-platform-clients.md)).

## Integrations and connections

- **Integration** — Configured business-level automation/integration unit. Operator-facing deployment that composes settings, integration-scoped slot bindings, integration-scoped schedules, module **procedures** (named workflow/operation launches with parameters), and (when configured) lineage to workflow runs. See [ADR-0028](../02-decisions/ADR-0028-integration-canonical-deployment-unit.md) and [ADR-0029](../02-decisions/ADR-0029-module-contributed-procedures.md).
- **Connection** — Configured access to an external system.

## Operations

- **Signal** — Standardized event, log, or telemetry emission from the platform or a module.
- **Incident** — Concrete operational problem with a lifecycle.
- **Attention** — Operator-facing aggregation or prompt around incidents and advisory concerns.
