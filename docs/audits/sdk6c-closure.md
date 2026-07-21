# SDK-6C Closure — Typed Integration Configuration, Connections, Secrets, and Readiness

**Date:** 2026-07-21  
**Status:** Closed (with documented environment / live-system limitations)  
**Predecessor:** SDK-6B closed (`docs/audits/sdk6b-closure.md`)  
**Design:** `docs/audits/sdk6c-design.md`  
**Audit:** `docs/audits/sdk6c-current-state-audit.md`

## Closure recommendation

**SDK-6C can be marked closed.** The intentional Integration SDK configuration model, platform adapters, module migrations, Management API surfaces, testing toolkit extensions, architecture/security protections, SQL test-environment fix for stale Aspire ports, and Aspire smoke (health + hosts) are in place. Remaining work belongs to **SDK-6D+** (operator UX depth, schedule-list readiness pills, live Proof/Unisa connectivity claims).

## Deliverable map

| # | Deliverable | Artifact / outcome |
| --- | --- | --- |
| 1 | Current-state audit | `docs/audits/sdk6c-current-state-audit.md` |
| 2 | Ownership model | `docs/audits/sdk6c-design.md` §1 |
| 3 | Typed settings design | SDK `Configuration/*` + design §2 |
| 4 | Runtime settings access | `IIntegrationSettingsAccessor` / `IIntegrationOptions<T>` + WorkerHost accessors |
| 5 | Secret integration | Reuses `ISecretStore`; presence-only GET; testing `SecretLeakageAssertions` |
| 6 | Connection requirements | `ConnectionRequirementDefinition` + slot factory |
| 7 | Runtime connection access | `IIntegrationConnectionAccessor` via `IRuntimeSlotResolver` |
| 8 | Verification model | `ConnectionVerificationStatusKind` + expectation enum |
| 9 | Integration/procedure readiness | `ConfigurationReadinessEvaluator` + `GetProcedureReadinessAsync` |
| 10 | Execution enforcement | Manual launch + `ScheduleDispatchRunner` (`BlockedByReadiness`) |
| 11 | Proof migration | `ProofIntegrationConfiguration` — `CnsApi`, `IntegrationDb` |
| 12 | Unisa migration | `UnisaIntegrationConfiguration` — `CnsApi`, `Sftp`, `IntegrationDb` + procedure subsets |
| 13 | Sample modules | HelloWorld config-free; FileImport optional; ApiPolling `DirectoryApi` connection sample |
| 14 | Testing toolkit | `IntegrationConfigurationTestContext`, `SecretLeakageAssertions` |
| 15 | Management API | Declarations + procedure readiness routes on `IntegrationsController` |
| 16 | Minimal UI | Existing Integration Detail readiness / procedure blocked codes (no redesign) |
| 17 | Persistence | Reused integration-scoped settings + secrets + bindings; no plaintext settings |
| 18 | Security controls | Redaction tests; presence-only secrets; authz patterns retained |
| 19 | Architecture protections | Arch tests for module/SDK/testing boundaries |
| 20 | Public API | Snapshot tests updated for Configuration/Readiness/Testing types |
| 21–23 | Test + SQL + Aspire | See results below |
| 24 | External-system limits | No live Proof/Unisa CampusNexus/SFTP production claims |
| 25 | Remaining → SDK-6D+ | See below |
| 26 | Close? | **Yes** |

## Module configuration state

| Module | Settings | Connections | Procedure subsets |
| --- | --- | --- | --- |
| HelloWorld | Optional demo only (`IsRequired=false`) | None | N/A — always ready |
| FileImport | Optional with defaults | None | Local fixtures stay simple |
| ApiPolling | Poll behavior settings | Required `DirectoryApi` (`UnsupportedOk` verification) | Poll procedure |
| Proof | None required | `CnsApi`, `IntegrationDb` (advisory verification) | Sync procedure requires both |
| Unisa | None required | `CnsApi`, `Sftp`, `IntegrationDb` (advisory verification) | Daily files: all three; TRANS: CnsApi+Db |

## Test results (2026-07-21)

| Suite | Result |
| --- | --- |
| Solution build (`Apperture.slnx`) | Succeeded |
| Unit tests | **1342 passed** |
| Architecture tests | **25 passed** |
| Management UI Shared tests | **374 passed** |
| End-to-end tests | **1 passed** |
| Integration tests (SQL on Aspire stable `127.0.0.1,14333`) | **144 passed** on confirmation run after DI + advisory verification fixes (one earlier Entra 403 flake resolved on retry) |
| SDK / readiness / security / schedule filtered suites | Passed during implementation |

### SQL test environment

- Removed stale `.env` Aspire ephemeral port `58361`.
- `SqlServerTestConfiguration` now probes reachability and skips dead Aspire ports, falling through to the next source; failure text warns about stale ephemeral ports.
- Supported Aspire SQL host port: **14333** (`AppHost` `Aspire:SqlServer:HostPort`).

## Aspire smoke results

| Check | Result |
| --- | --- |
| Stop / clean build / start AppHost | Pass |
| Management API `/health` | **200 Healthy** |
| Management UI HTTP | **200** |
| WorkerHost under AppHost | Running (`WorkerHost.csproj`) |
| SQL `127.0.0.1,14333` | Reachable; `Apperture` DB present |
| Secret plaintext in API health path | N/A (no secrets) |
| Live Proof/Unisa external systems | **Not claimed** (no production credentials in this pass) |

Operator API smoke for declarations/procedure readiness was validated via automated Management Host governance/integration tests rather than a full interactive UI walkthrough.

## External-system limitations

- Do **not** claim live CampusNexus or production SFTP validation for Proof/Unisa without safe credentials and connectivity.
- Sample ApiPolling uses connection-backed resolution with embedded/fake fallback for local runs.
- Test SFTP container (`apperture-test-sftp` on port 2222) required for Epic 59A SFTP governance test; start it when validating that path.

## Remaining work (SDK-6D or later)

1. Richer operator UX for declarations, secret replace/clear, and procedure-specific readiness explanations (beyond current detail surface).
2. Schedule list/detail pills for integration-not-ready (dispatch enforcement already done).
3. Optional persisted readiness snapshots if fleet-scale caching is needed.
4. Broader value-type persistence (Decimal/Uri/TimeZone as first-class `PlatformSettingValueType` columns) if String mapping proves insufficient.
5. Live Proof/Unisa connectivity certification with customer-safe credentials.
6. Controllers-focused API contract tests for new declarations/procedure-readiness endpoints (coverage today is via readiness + governance flows).

## Explicit non-goals (honored)

SMTP redesign, notification-provider redesign, packaging, marketplace, codegen, shared entities, workflow composition, module UI contribution, scheduler redesign, WorkerHost lifecycle redesign.
