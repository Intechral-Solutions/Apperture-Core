# SDK-6B Closure — Integration SDK Adoption and Testing Toolkit

**Date:** 2026-07-21  
**Status:** Closed (with documented environment/live limitations)  
**Predecessor:** SDK-6A Integration SDK contract consolidation + deferred Aspire smoke (completed)

## Closure recommendation

**SDK-6B can be marked closed.** Essential toolkit coverage, module boundary migration, architecture protections, public API snapshots, clean solution build, and post-change Aspire smoke for sample modules all pass. Remaining gaps belong to **SDK-6C** (configuration / secrets / connection management) or later.

Do not start SDK-6C in this pass.

## Final module dependency state

| Assembly | Depends on `ModuleSdk.Integration` | Direct `Core.Application` | Direct EF / SQL client in authoring assembly |
| --- | --- | --- | --- |
| `Sample.HelloWorld.Module` | Yes | No (via Integration type-forwarded namespaces) | No |
| `Sample.FileImport.Module` | Yes | No | No |
| `Sample.ApiPolling.Module` | Yes | No | No |
| `Proof.CampusNexus.StudentReview` | Yes | No | No — uses `IProofStudentReviewPersistenceAdapter` |
| `Proof.CampusNexus.StudentReview.Persistence` | Host/persistence only | N/A | Yes (EF adapter implementation) |
| `Unisa.CampusNexus.PaymentPlans` | Yes | No | No — repository/bootstrap moved out |
| `Unisa.CampusNexus.PaymentPlans.Persistence` | Host/persistence only | N/A | Yes (SQL/repository wiring) |

Hosts (`Management.Host`, `WorkerHost`) register persistence adapters beside module DI.

## Testing toolkit capabilities

`Apperture.ModuleSdk.Integration.Testing` provides:

- `IntegrationProcedureTestHost` for procedure / workflow / operation-item retry execution
- Slot injection (`WithSlot`)
- Identity + origin (`IntegrationExecutionIdentity`, `IntegrationExecutionOrigin`)
- Deterministic time (`TestTimeProvider`)
- Capture of platform events, operational logs, operation groups/items
- Outcome derivation from operation-group statuses via existing SDK aggregation contracts (`OperationGroupAggregator` / `ProcedureExecutionOutcome`)

Documented in `docs/05-modules/integration-testing-guide.md`.

## Toolkit scenario coverage

Covered by `IntegrationProcedureTestHostToolkitContractMatrixTests` plus representative module tests:

### Procedure execution

- Explicit success / failed / partial-success outcomes
- Unexpected exception translation
- Cancellation before and during execution (not classified as ordinary failure)
- Deterministic timestamps
- Manual and scheduled origins

### Event capture

- One and multiple events, emit order where defined
- Completion / partial-failure events exactly once for representative modules
- Correlation and run identity
- Secret plaintext absent from payloads

### Operation groups and items

- All succeeded, mixed, all failed, skipped, empty group
- Stable sequence, `TotalCount = Succeeded + Failed + Skipped`
- Safe metadata capture

### Retry

- Retryable accepted; non-retryable / succeeded rejected
- Retry success and retry-fails-again
- Retry count bump; deterministic retry timestamps
- Aggregate updates; full-run vs item-level distinction

### Settings / secrets (existing contracts)

- Setting/secret available and missing
- Secret omitted from logs, events, and operation metadata

### Representative modules

- HelloWorld, FileImport (valid/mixed/malformed paths in unit coverage), ApiPolling, Proof (fake adapter + scoped lifetime), Unisa (fake boundary)

## Proof lifetime / DI decision

**Retain `IServiceScopeFactory.CreateScope()` in `SyncRecentStudentsStepHandler`.**

Reason:

- Worker Host registers modules / step handlers as **singletons** for process lifetime.
- `IProofStudentReviewPersistenceAdapter` is **scoped** (owns DbContext / connection lifetime).
- Constructor-injecting the adapter into the singleton handler would capture a scoped dependency incorrectly.
- Procedure execution does not currently provide a DI scope to handlers that would make constructor injection lifetime-safe without a broader host change.

Documented in code comments; protected by unit test asserting a new scoped adapter instance per execution.

No broad DI abstraction was added solely to remove `GetRequiredService`.

## Architecture-test discovery decision

**Keep an explicit `IntegrationModuleTypes` list.**

Automatic discovery via build-output scanning or load-time heuristics was rejected as brittle.

Added guard: `Explicit_integration_module_list_covers_referenced_IModule_assemblies` fails if a referenced Sample/Proof/Unisa assembly exposing `IModule` is missing from the list.

## `PlatformEvents` alias decision

- Preferred path: `context.Signals.Events` (ADR-0038).
- Alias `IStepExecutionContext.PlatformEvents` still equals `Signals.Events` at runtime.
- New sample/integration authoring uses `Signals.Events`.
- Alias marked `[Obsolete("Use Signals.Events ...")]` with clear replacement; **not removed** to avoid churn in harnesses and older call sites.

## Public testing API

Public surface remains:

- `IntegrationProcedureTestHost`
- `IntegrationProcedureExecution`
- `IntegrationExecutionIdentity`
- `IntegrationExecutionOrigin`
- `TestTimeProvider`
- `CapturedPlatformEvent`
- `CapturedOperationalLog`

Snapshot test now asserts both missing and unexpected exported types. No production-only types were added to the testing assembly solely for tests.

## Automated validation results

| Suite | Result | Notes |
| --- | --- | --- |
| Clean solution build (`Apperture.slnx`) | Pass | |
| Architecture tests | Pass | Includes new list-coverage guard |
| Unit tests (`Apperture.UnitTests`) | Pass (1321) | Includes toolkit matrix, module, retry, snapshots |
| ModuleSdk.Testing | Pass | |
| Management.Ui.Shared.Tests | Pass | |
| EndToEndTests | Pass | |
| IntegrationTests (`Apperture.IntegrationTests`) | Fail (env) | SQL endpoint `127.0.0.1,58361` refused — **environment-dependent**, not an SDK-6B regression |

## Aspire smoke results (post-SDK-6B)

AppHost started; Management API `/health` = Healthy.

| Scenario | Result |
| --- | --- |
| Module discovery lifecycle | HelloWorld, FileImport, ApiPolling, Proof, Unisa = `InstalledEnabled` |
| HelloWorld manual greet | Pass — completed, `demo.completed` once, origin=manual |
| HelloWorld scheduled | Pass — minute schedule + `run-cycle`; origin=scheduled; event once |
| FileImport valid | Pass — group Succeeded `3/3/0`, one completion event |
| FileImport mixed | Pass — `CompletedWithErrors` `8/2/6/0`, one `import.completed.with.errors` |
| FileImport item retry | Pass — child run dispatched/completed; attempt recorded. Permanent bad row yields attempt `Failed` (`missing_external_id`) — expected for mixed fixture |
| ApiPolling success | Pass — Succeeded `4/4/0`, one `sync.completed.with.changes` |
| ApiPolling partial failure | Pass — `CompletedWithErrors` `7/2/5/0`, one `sync.completed.with.errors` |
| ApiPolling item retry | Pass — child run dispatched/completed; permanent error rows keep attempt Failed (expected) |
| Duplicate completion events | Not observed (exactly one module completion event per run inspected) |
| Host log scan for EF concurrency / migration / DI errors | No matches in AppHost terminal output for this pass |

WorkerHost participated (runs completed; schedule cycle accepted).

## Proof and Unisa live-validation limitations

| Module | Discovery | Procedures listed | Live execution |
| --- | --- | --- | --- |
| Proof | Yes (`InstalledEnabled`) | Yes (`syncRecentStudents`) but **not launchable** | Blocked by missing `CnsApi` + `IntegrationDb` bindings. Covered by toolkit + fake persistence adapter tests. |
| Unisa | Yes (`InstalledEnabled`) | **None exposed** while integration not ready | Blocked by missing `CnsApi`, `Sftp`, `IntegrationDb` bindings. Covered by toolkit/fake boundary tests only. |

Do **not** claim external CampusNexus / SFTP / live SQL integration validation for Proof or Unisa in this environment.

## ApiPolling lifecycle note

Earlier SDK-6A smoke required installing/enabling ApiPolling when it was only `Discovered`. That is an **environment/module-state prerequisite**, not an SDK runtime regression. Current smoke found ApiPolling already `InstalledEnabled`.

## Remaining gaps (SDK-6C or later)

- Production settings / secrets / connection management UX and packaging
- Capability manifests and workflow composition
- Shared entity model / UI redesign
- Automatic architecture discovery beyond the explicit list + guard
- Removing `PlatformEvents` after a deprecation window (optional)
- Full SQL-backed `Apperture.IntegrationTests` when Aspire/test SQL is available on the configured port
- Live Proof/Unisa execution once safe bindings and credentials exist

## Files changed (this closure pass highlights)

- Toolkit matrix + outcome derivation (`IntegrationProcedureTestHost`, contract-matrix tests)
- Proof scoped lifetime comments + test
- Arch list documentation + coverage guard
- `PlatformEvents` obsolete annotation
- Public API snapshot unexpected-type check
- Docs: this closure record; integration testing / authoring guides from SDK-6B implementation
