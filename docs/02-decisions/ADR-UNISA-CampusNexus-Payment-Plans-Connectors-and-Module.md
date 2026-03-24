# ADR: UNISA Campus Nexus payment plans — platform connectors and module

## Status

Accepted (scaffold delivered March 2026). Renamed from PMI-centric naming March 2026: the module is **general-purpose for UNISA** integrating with **Campus Nexus**; vendor profiles (e.g. PMI) are configuration, not the module id.

## Context

UNISA payment plan integrations need governed connections to Campus Nexus (HTTP), SFTP, and SQL mapping tables. The platform already models SQL via `Apperture.Platform.Sql` and slot-bound resolution.

## Decision

1. **Connector definition ids** (see `WellKnownConnectorDefinitionIds` in `Apperture.Core.Domain`):
   - `Apperture.Platform.Sql` — unchanged.
   - `Apperture.Platform.CampusNexus` — CNS command + OData HTTP client (`ICampusNexusApiClient`).
   - `Apperture.Platform.Sftp` — SSH.NET SFTP (`ISftpFileClient`).

2. **Runtime registration** — `AddPlatformSqlConnector()` registers all three definitions (in-memory registry on worker) and factories on `IConnectorClientFactoryRegistry` via `PlatformConnectorClientFactoriesHostedService`.

3. **Control plane** — `SeedWellKnownConnectorDefinitionsHostedService` registers the same three definitions into `StubConnectorDefinitionRegistry` so connection CRUD and bindings can reference them. `CampusNexusConnectorDefinitionContributor` (registered with the UNISA module) enriches the Campus Nexus entry with connection-field catalog metadata for operators; see [ADR-0027](./ADR-0027-connector-definitions-not-platform-clients.md).

4. **Connection material (Campus Nexus)** — `BaseUrl`, `AuthMode` (`ApiKey` | `Basic`), plus `ApiKey` or `Username`/`Password` in resolved material.

5. **Connection material (SFTP)** — `Host`, optional `Port`, `Username`, and either `PrivateKeyPem` (+ optional `PrivateKeyPassphrase`) or `Password`.

6. **UNISA SQL tables** — `UnisaFundSourceCampusMaps`, `UnisaPaymentPlanArrangements`, `UnisaFileImportWatermarks` in shared instance database (EF migration `AddPmiIntegrationTables`; migration id unchanged for history — **if a database was created from an older revision that created `Pmi*` tables, reset the database or rename those tables manually to match**).

7. **Module** — `Unisa.CampusNexus.PaymentPlans` declares slots `CnsApi`, `Sftp`, `IntegrationDb` and workflow `Unisa.CampusNexus.PaymentPlans.ProcessDailyFiles`. Assembly/project: `Unisa.CampusNexus.PaymentPlans`. Business logic from legacy eProm/TRANS is scaffolded in `IUnisaPaymentPlanManagementService` for incremental porting.

## Consequences

- Operators create three connection types in the control plane and bind them to module slots.
- Full payment-plan orchestration, cancellation rules, and file parsers remain follow-up work on top of this scaffold.
- First-vendor configuration (e.g. PMI fund/campus mappings) should live in data or instance config keyed by an integration profile, not in separate modules.
