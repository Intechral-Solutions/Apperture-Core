# Epic 74 — CampusNexus Student Proof Integration

## Status

Accepted

## Context

After Epic 73, we needed an end-to-end proof that:

- integration-scoped connector resolution (`ICampusNexusApiClient` via slots and `StartWorkflowRequest.IntegrationId`) works on the real worker path;
- module-owned SQL (separate EF context and migration history from `AppertureDbContext`) can be applied from Management and used from the worker;
- a small workflow can return operator-visible summaries without entangling vendor-specific UNISA payment-plan logic.

## Decision

Introduce a dedicated proof module **`Proof.CampusNexus.StudentReview`** with:

- **Module** (`Proof.CampusNexus.StudentReview`): three-step workflow `Validate` → `SyncRecentStudents` → `Complete`; OData query parameters (`entitySet`, `odataOrderBy`, `odataTop`, `odataSelect`) with documented defaults; `OperatorExecutionResult` inline summary on sync; procedure declaration for future UI launch.
- **Persistence** (`Proof.CampusNexus.StudentReview.Persistence`): tables `ProofStudentReviewSnapshots` (upsert key `(IntegrationId, CnsStudentId)`) and `ProofStudentReviewRuns`; migration history table `__EFMigrationsHistory_ProofStudentReview`.
- **Hosts**: Management.Host registers the module, runs module migrations on startup (`applyModulePersistenceMigrationsOnStartup: true`), and hosts `ProofCampusNexusStudentReviewSlotBootstrapHostedService` for slots `CnsApi` and `IntegrationDb`. WorkerHost registers persistence with `registerCapabilitySlotBootstrap: false` and constructs `ProofCampusNexusStudentReviewModule` with `IServiceScopeFactory` and `ILoggerFactory` for the sync handler.
- **Execution**: `WorkflowStartRequestProcessor` merges server-injected step parameters (`__appertureRunId`, `__appertureWorkflowInstanceId`) so the sync step can attribute run rows.

## Schedule-driven execution

No new scheduler work. `ScheduleDispatchRunner` already dispatches workflow starts with `IntegrationId` from the schedule. For this module, define a schedule with the proof workflow definition id and the same integration id used for bindings; verify manually or via existing schedule SQL tests.

## Operator checklist (real CNS tenant)

Confirm with environment documentation or `$metadata`:

- OData entity set name for students (default in code: `Students`).
- Property names for order-by, select, and timestamps (defaults: `DateLstMod`, `DateAdded`, etc.).
- `BaseUrl`, auth mode (`ApiKey` or `Basic`), and service account rights to read the entity set.
- Apperture: Campus Nexus **connection**; **integration** for module id `Proof.CampusNexus.StudentReview` (active); bindings for **`CnsApi`** → CNS connection and **`IntegrationDb`** → platform SQL connection; module enabled per registry/guards if applicable.

## Manual runbook

1. Start **Management.Host** once so platform migrations and proof module migrations apply (or run `dotnet ef database update` with the proof design-time factory).
2. Start **WorkerHost** against the same database.
3. Create connection, integration, and bindings as above.
4. `POST /api/workflows/{workflowDefinitionId}/start` with body including `integrationId` and optional OData override fields in parameters (see Management API models).
5. Inspect `ProofStudentReviewRuns` / `ProofStudentReviewSnapshots` in SQL and workflow step output for the inline summary.

## Tests

- Unit: OData query builder, JSON shape for `CnsStudentReviewODataRow`, sync handler upsert/idempotency with mocked `ICampusNexusApiClient` and EF in-memory.
- SQL integration: `Epic73SqlSeamsIntegrationTests` migrates proof schema alongside platform and UNISA (same collection; requires configured SQL).

## Consequences

- UNISA payment plans remain the vendor-specific slice; Epic 74 is a reusable baseline for “CNS + module DB + dispatch”.
- Tenant-specific OData names remain **parameters** with documented defaults; wrong names surface as CNS errors and failed run rows, not silent wrong data.
