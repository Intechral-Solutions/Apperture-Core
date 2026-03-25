# Epic 55 — Management Host HTTP governance integration harness (55 / 55A)

Date: 2026-03 (approximate)

## Status

Accepted — implemented.

## Context

Much of the control plane is covered by unit tests and lighter-weight API stubs, but **authorization and authentication at the real HTTP boundary** of **`Apperture.Management.Host`** need confidence: real DI, SQL migrations, seeded roles/permissions, and predictable principals. Epic 55 adds an integration test **collection** that hosts the full management pipeline and asserts status codes for allow/deny scenarios (401 without auth, 403 without permission, 200 with correct permission mix).

## Summary of changes

1. **`WebApplicationFactory`** — `ManagementHostWebApplicationFactory` hosts the management host entry assembly under test configuration.
2. **Bootstrap override** — `FixedBootstrapConnectionProfile` replaces **`IBootstrapConnectionProfile`** so tests can use a normal SQL connection string when **`ASPNETCORE_ENVIRONMENT=Testing`** (product bootstrap otherwise restricts config-based SQL outside Development).
3. **Test authentication** — `ManagementTestAuthHandler` (scheme **`ManagementTest`**): header **`X-Test-External-Id`** selects a seeded test user; missing header → unauthenticated → **401** on protected endpoints.
4. **Fixture** — `ManagementHostGovernanceFixture` (collection fixture): probes SQL, runs migrations once, idempotently seeds users, identity links, roles, permissions, and a sample integration (`ManagementGovernanceTestConstants`).
5. **HTTP tests** — `ManagementHostGovernanceHttpTests` exercises modules, connector definitions (catalog shape + permission gate), integration detail, and related governance scenarios with **one expected status per test** (no OR-assertions).
6. **CI** — `.github/workflows/ci.yml` provisions SQL Server, sets **`APPERTURE_TEST_SQL`** and related variables so the suite runs in CI.

## Environment variables

Documented in `tests/Apperture.IntegrationTests/README-ManagementHostGovernance.md`:

| Variable | Role |
|----------|------|
| **`APPERTURE_TEST_SQL`** | Connection string for Epic 55 governance tests (includes Initial Catalog; fixture can create DB on master then migrate). |
| **`MSSQL_SA_PASSWORD`** / **`MSSQL_TEST_SERVER`** / **`MSSQL_TEST_DATABASE`** | Broader SqlServer integration test configuration. |

If SQL is unavailable, the fixture sets **`SqlAvailable = false`** and tests fail fast with an explicit message.

## Consequences

### Positive

- Regressions in permission wiring, host startup, and migration ordering show up as failing HTTP tests.
- New permissions can add stable external ids + seed rows + a focused HTTP test (documented in the README).

### Negative

- Requires SQL (container, instance, or LocalDB) for local/CI runs of this suite.

## Related

- [Epic 52](./Epic52-Connector-Definition-Catalog.md) — connector definition HTTP scenarios in harness
- [Epic 54](./Epic54-Integration-Canonical-Deployment-Lineage.md) — integration detail endpoint coverage

## Testing

- `tests/Apperture.IntegrationTests/ManagementHost/*`
- Filter: `FullyQualifiedName~ManagementHostGovernanceHttpTests`
