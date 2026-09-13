# Epic 74 — CampusNexus Student Proof Integration

## Status

Accepted

## Context

After Epic 73, we needed an end-to-end proof that:

- integration-scoped connector resolution (`ICampusNexusApiClient` via slots and `StartWorkflowRequest.IntegrationId`) works on the real worker path;
- module-owned SQL (separate EF context and migration history from `AppertureDbContext`) is applied and used on the **integration-bound** SQL database (`IntegrationDb` slot), not the platform bootstrap database;
- a small workflow can return operator-visible summaries without entangling vendor-specific UNISA payment-plan logic.

## Decision

Introduce a dedicated proof module **`Proof.CampusNexus.StudentReview`** with:

- **Module** (`Proof.CampusNexus.StudentReview`): three-step workflow `Validate` → `SyncRecentStudents` → `Complete`; OData query parameters (`entitySet`, `odataOrderBy`, `odataTop`, `odataSelect`) with documented defaults; `OperatorExecutionResult` inline summary on sync; procedure declaration for future UI launch.
- **Persistence** (`Proof.CampusNexus.StudentReview.Persistence`): tables `ProofStudentReviewSnapshots` (upsert key `(IntegrationId, CnsStudentId)`) and `ProofStudentReviewRuns`; migration history table `__EFMigrationsHistory_ProofStudentReview`.
- **Hosts**: Management.Host registers the module and hosts `ProofCampusNexusStudentReviewSlotBootstrapHostedService` for slots `CnsApi` and `IntegrationDb`. WorkerHost calls `AddProofCampusNexusStudentReviewIntegration(registerCapabilitySlotBootstrap: false)` and constructs `ProofCampusNexusStudentReviewModule` with `ILoggerFactory` for the sync handler. Proof EF migrations run against the **integration SQL database** on first sync when pending migrations exist (idempotent `MigrateAsync`).
- **Execution**: `WorkflowStartRequestProcessor` merges server-injected step parameters (`__appertureRunId`, `__appertureWorkflowInstanceId`) so the sync step can attribute run rows.

## Schedule-driven execution

No new scheduler work. `ScheduleDispatchRunner` already dispatches workflow starts with `IntegrationId` from the schedule. For this module, define a schedule with the proof workflow definition id and the same integration id used for bindings; verify manually or via existing schedule SQL tests.

## Operator checklist (real CNS tenant)

Confirm with environment documentation or `$metadata`:

- OData entity set name for students (default in code: `Students`).
- Property names for order-by, select, and timestamps (defaults: `LastModifiedDateTime`, `CreatedDateTime`, etc.).
- `BaseUrl`, auth mode (`ApiKey` or `Basic`), and service account rights to read the entity set.
- Apperture: Campus Nexus **connection**; **integration** for module id `Proof.CampusNexus.StudentReview` (active); bindings for **`CnsApi`** → CNS connection and **`IntegrationDb`** → SQL connection that should hold proof tables (module-owned schema); module enabled per registry/guards if applicable.

## Manual runbook

1. Start **Management.Host** once so **platform** migrations apply (proof module tables are not created on the bootstrap database).
2. Start **WorkerHost** against the same platform database used for workflow dispatch (integration SQL may be the same server or a different database per binding).
3. **Configure through Management UI** (supported path; no manual SQL for connections or bindings):
   - **Connections**: Create a **Campus Nexus API** connection (`Apperture.Platform.CampusNexus`) with `BaseUrl`, `AuthMode`, and credentials via **Connections → Add connection** (secret fields are stored in the platform secret store; only secret ids are persisted on the connection).
   - **Connections**: Create a **Platform SQL** connection (`Apperture.Platform.Sql`) for the **`IntegrationDb`** binding — this is the database where `ProofStudentReviewSnapshots` / `ProofStudentReviewRuns` are stored (first successful sync applies proof EF migrations to that database if needed).
   - **Integrations**: Create an integration for module `Proof.CampusNexus.StudentReview` (**Integrations → New integration**), activate it, then on the integration detail page under **Connection bindings** bind slot **`CnsApi`** to the Campus Nexus connection and **`IntegrationDb`** to the SQL connection. Use **Readiness** on the same page to confirm the integration is runnable before launch.
4. Start the proof workflow (e.g. **Launch** on the module procedure from integration detail if available, or `POST /api/workflows/{workflowDefinitionId}/start` with body including `integrationId` and optional OData override fields in parameters).
5. Inspect `ProofStudentReviewRuns` / `ProofStudentReviewSnapshots` on the **integration-bound SQL database** (not necessarily the platform bootstrap catalog) and workflow step output for the inline summary.

## Tests

- Unit: OData query builder, JSON shape for `CnsStudentReviewODataRow`, sync handler guard when `IntegrationDb` is unbound.
- SQL integration: `Epic73SqlSeamsIntegrationTests` migrates proof schema alongside platform and UNISA (same collection; requires configured SQL).

## Consequences

- UNISA payment plans remain the vendor-specific slice; Epic 74 is a reusable baseline for “CNS + module DB + dispatch”.
- Tenant-specific OData names remain **parameters** with documented defaults; wrong names surface as CNS errors and failed run rows, not silent wrong data.
