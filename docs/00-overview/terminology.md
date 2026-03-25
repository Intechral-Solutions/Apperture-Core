# Terminology

See [.ai/memory/glossary.md](../../.ai/memory/glossary.md) for the canonical glossary. Key terms:

- **Environment** — Full deployed platform boundary for a tenant/org; one customer per Environment
- **Runtime Plane** — Execution/data-plane within an Environment
- **Module** — Installable capability type (catalog entry)
- **Module Installation** — Module enabled in an Environment
- **Module Instance** — Configured executable occurrence of a Module Installation (e.g. one worker or Function App)
- **Workflow** — Durable procedure with step graph and persisted state
- **Step** — Unit of execution within a workflow
- **Control plane** — Management API, UI, registry, schedules, audit
- **Instance plane** — Worker execution, DTFx/Durable Functions, step execution
- **Integration** — Operator-facing unit that composes a module deployment: settings, integration-scoped slot bindings, integration-scoped schedules, module **procedures** (named workflow/operation launches with parameters), and (when configured) lineage to workflow runs. See [ADR-0028](../02-decisions/ADR-0028-integration-canonical-deployment-unit.md) and [ADR-0029](../02-decisions/ADR-0029-module-contributed-procedures.md).
- **Procedure** — Module-declared operator action: stable id, metadata, launch kind (workflow or operation), target definition id, and optional JSON parameter schema. Exposed under the integration-scoped Management API when the integration is runnable; not a generic connector operation catalog (see [ADR-0027](../02-decisions/ADR-0027-connector-definitions-not-platform-clients.md)).
