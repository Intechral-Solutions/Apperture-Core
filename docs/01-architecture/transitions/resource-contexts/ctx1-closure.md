# CTX-1 closure — ResourceContext foundation

Date: 2026-09-11
Status: **Complete.** CTX-2 may begin.
Branch: `ctx1-resource-context-foundation`
Design basis: [`ctx-design-gate.md`](./ctx-design-gate.md), with the corrections recorded in §12 below.

---

## 1. Final ResourceContext schema

New table `ResourceContexts`:

| Column | Type | Notes |
|---|---|---|
| `Id` | `nvarchar(64)`, PK | Reserved literal `shared` for the Shared context; `Guid.NewGuid().ToString("N")[..24]` otherwise |
| `Name` | `nvarchar(256)`, required | Display name. **Not unique** |
| `Slug` | `nvarchar(128)`, required | Normalized. **Unique** via `UX_ResourceContexts_Slug` |
| `Kind` | `nvarchar(32)`, required | `Shared` \| `Application`. String, so new kinds are additive |
| `State` | `nvarchar(32)`, required | `Active` \| `Archived` |
| `IsReserved` | `bit`, required | True only for Shared |
| `Description` | `nvarchar(1024)`, null | |
| `CreatedAtUtc` | `datetimeoffset` | |
| `UpdatedAtUtc` | `datetimeoffset` | |
| `RowVersion` | `rowversion` | Matches `Integrations` concurrency handling |

No `ParentResourceContextId`. No metadata JSON column.

Domain types (`Apperture.Core.Domain.ResourceContexts`): `ResourceContext`, `ResourceContextId`,
`ResourceContextIds` (`Shared`, `SharedValue`, `IsShared`), `ResourceContextKinds`,
`ResourceContextStates`, `ResourceContextSlug` (normalizer + reserved set), `ResourceContextRef`.
The concept is never abbreviated to `Context` in code or XML documentation.

### Slugs

Deterministic normalization: lowercase, trim, whitespace → `-`, `_` → `-`, unsupported characters
removed, repeated hyphens collapsed, leading/trailing hyphens trimmed, empty result rejected, capped
at 128 characters. Only normalized slugs are persisted. Reserved slugs are exactly `shared` and
`system` — `api`, `apps` and `new` are deliberately **not** reserved, because no slug-addressed route
exists yet that would need them. Shared's slug is immutable. No alias/history table.

---

## 2. Shared seeding and backfill

Migration `20260911120000_Ctx1_ResourceContextFoundation` (one forward migration, hand-authored per
the convention established by `20260909180000_Iam5_...`; no Designer file). `Up` in order:

1. `CreateTable ResourceContexts` + unique slug index.
2. Insert the reserved Shared row: `Id='shared'`, `Name='Shared'`, `Slug='shared'`, `Kind='Shared'`,
   `State='Active'`, `IsReserved=1`. Guarded by `IF NOT EXISTS`. Seeding lives in the migration, not a
   hosted service, because step 4 depends on the row and must not race startup.
3. `AddColumn ResourceContextId` **nullable** on `Integrations`, `Connections`, `ScheduleDefinitions`.
4. `UPDATE ... SET ResourceContextId = N'shared' WHERE ResourceContextId IS NULL` on each.
5. `AlterColumn` each to `nullable: false`.
6. Non-unique index per ownership column, then the three foreign keys.

`Down` reverses in the opposite order. No historical migration was edited. No operator classification
is required during upgrade, and no existing resource id is rewritten.

### On taking real foreign keys

This deviates from the house convention of string correlation between operator aggregates. That
convention avoids coupling aggregate lifecycles; `ResourceContexts` is a small ownership dimension, and
a restrictive FK buys a structural guarantee that a context owning resources cannot be removed out from
under them. The rationale is recorded in `AppertureDbContext`, in the migration, and here.

The FK is **backup, not the primary rule**: supported product behavior never deletes a ResourceContext
at all (§5).

---

## 3. Exactly which resources are context-owned

Direct required ownership, and nothing else:

```
Integrations.ResourceContextId         nvarchar(64) NOT NULL  FK → ResourceContexts(Id)  NO ACTION
Connections.ResourceContextId          nvarchar(64) NOT NULL  FK → ResourceContexts(Id)  NO ACTION
ScheduleDefinitions.ResourceContextId  nvarchar(64) NOT NULL  FK → ResourceContexts(Id)  NO ACTION
```

Deliberately **not** given a `ResourceContextId`: Bindings (pure edges — a third source of truth that
can drift from both endpoints), PlatformSettingValues, Notification Definitions/Subscriptions,
IntegrationAttentionRoutings, module definitions, IAM entities, Demo Business Data, and every
historical/runtime entity.

Ownership defaults:

| Resource | No context supplied | Explicit context |
|---|---|---|
| Integration | Shared | Must exist and be Active |
| Connection | Shared | Must exist and be Active |
| Standalone Schedule | Shared | Must exist and be Active |
| Integration-attached Schedule | **Inherits the Integration's context** | Must equal the Integration's, else rejected |

`Integration.ScopeKey` and `Connection.ScopeKey` were not read, written or reinterpreted. XML comments
on both records now state explicitly that ScopeKey is authorization and ResourceContextId is ownership.

---

## 4. Schedule ownership semantics

The rule lives in `SqlScheduleManagementService.CreateScheduleAsync` — the authoritative application
service — not in controllers. `ScheduleDefinition.ResourceContextId` is nullable in the domain record
where `null` means *not yet resolved*; a schedule loaded from persistence always carries a value.

* Standalone, omitted → Shared.
* Standalone, explicit → that context (must exist and be Active).
* Integration-attached, omitted → the Integration's context. **Never blindly Shared.**
* Integration-attached, explicit and equal → accepted.
* Integration-attached, explicit and different (including an explicit `shared` under an Application
  Integration) → rejected with a clear message.

`SqlScheduleRepository.ToDefinitionEntity` defaults an unresolved value to `shared` as a safety net for
direct-repository callers; that is documented as a net, not the rule.

A Schedule owns a direct column rather than deriving from its Integration because standalone schedules
are legal and have no Integration to derive from.

---

## 5. Lifecycle: archive-only, no hard delete

This tightens the design gate deliberately. CTX-2 will snapshot historical context by
`ResourceContextId` and resolve the row later for display. A foreign key on *current* ownership only
proves a context owns nothing right now; it cannot prove the context never owned anything after those
resources move. Therefore:

> ResourceContexts are archiveable, not hard-deletable.

* `IResourceContextService` and `IResourceContextRepository` expose **no delete member at all** — a
  reflection test enforces this.
* Retirement is `State = Archived`.
* Shared cannot be renamed, re-slugged, archived or deleted.
* Archiving is refused while the context owns any Integration, Connection or Schedule
  (`CountOwnedResourcesAsync`). CTX-3 may refine this.
* Archived contexts keep resolving by id; archiving hides them from creation and from
  `ListAsync(includeArchived: false)` only.

Supported operations: get by id, get by slug, list, create Application context, rename, change slug,
update description, archive, unarchive, and `ResolveForNewOwnershipAsync` (the shared default-and-
validate path used by all three resource create paths).

---

## 6. Binding same-context/Shared enforcement

The rule: *an Integration may bind a Connection in its own ResourceContext, or in Shared, and nothing
else.* Implemented once in `ResourceContextReferenceRule.CanReference` and enforced in two independent
places.

**Discovery** — `IBindingEligibilityService.GetEligibleConnectionsAsync` gained an
`owningResourceContextId` parameter. When supplied, ineligible-by-context Connections are excluded.
Callers that have an owning Integration now pass it: `BindingService.CreateBindingAsync`,
`IntegrationBindingsController`, `IntegrationDispatchReadinessEvaluator`,
`IntegrationConfigurationEvidenceBuilder`. The module-scoped legacy surface passes `null` explicitly,
because with no owning Integration there is no context to compare against.

**Authoritative mutation** — `BindingService.CreateBindingAsync` loads the Connection and applies the
rule itself before consulting eligibility, and returns a clear domain error naming both contexts.
Integrity does not depend on a discovery query.

The parameter was added to the existing signature rather than as a new overload, so every call site had
to be reviewed rather than silently keeping the unsafe form.

---

## 7. Resolution of the `GetBySlotAsync` ambiguity

### Callers found

`IBindingRepository.GetBySlotAsync(moduleId, slotName)` returned the **first instance-wide match** for
`(ModuleId, SlotName)` with no Integration filter. Two production callers:

1. `SlotBoundResolver.ResolveSlotAsync<T>(moduleId, slotName, integrationId)` — the fallback taken
   whenever `integrationId` is absent. Reached from `StepExecutionContext.ResolveSlotAsync` when a step
   runs without an Integration identity. (`IntegrationConnectionAccessor` and
   `CampusNexusCampusesOptionProvider` always pass an integration id and were never at risk.)
2. `BindingService.GetBindingAsync(moduleId, slotName)` — behind
   `GET`/`DELETE /api/modules/{moduleId}/slots/{slotName}/binding` (`BindingsController`). `POST` on
   that route was already 410 Gone.

With two Integrations of the same module, either path could return an arbitrary Integration's binding —
and once Applications exist, another Application's Connection.

### Chosen correction

Renamed to `GetUnownedBySlotAsync` and restricted the query to `IntegrationId IS NULL`:

```csharp
.FirstOrDefaultAsync(e => e.ModuleId == moduleId && e.SlotName == slotName && e.IntegrationId == null, ct)
```

A ResourceContext filter would not have helped: the resolver has no owning context to supply when it
has no Integration. The fix is to make the contextless lookup structurally incapable of selecting an
Integration-scoped binding, which is the invariant the epic actually requires:

> An Integration-scoped binding is never resolved without the Integration identity that disambiguates it.

The no-Integration fallback is preserved strictly for the legitimate global/contextless form that
genuinely exists: bindings with `IntegrationId = null`, which is exactly what
`BindingService.CreateCapabilityBindingAsync` creates and what pre-Integration legacy rows are. The
rename forces every caller to be re-read rather than silently inheriting new semantics.

### Compatibility impact

* Integration-scoped resolution (`GetBySlotAndIntegrationAsync`) is unchanged — the path every
  Integration-owned workflow step already takes.
* Genuinely global bindings still resolve without an Integration identity. Platform/module operations
  that rely on them keep working.
* `GET`/`DELETE /api/modules/{moduleId}/slots/{slotName}/binding` no longer see Integration-owned
  bindings. That surface was already superseded by `/api/integrations/{integrationId}/bindings`, and
  what it returned for an Integration-owned slot was arbitrary. The operator UI does not use it.
* A module step that resolves a slot with no Integration identity and relied on picking up an
  Integration-owned binding now gets `null` instead of an arbitrary one. That is the intended
  correction, not a regression: the previous value was not knowably correct.

---

## 8. API compatibility, defaults, optional filters, DTOs

**Creation contracts** gained an optional `ResourceContextId`; omitting it is unchanged behavior.
`CreateIntegrationRequest(Body)`, `CreateConnectionRequest(Body)`, `CreateScheduleRequestBody`,
`CreateIntegrationScheduleRequestBody`. Rejected: unknown context, archived context, Schedule context
mismatch (400 with a message).

**List endpoints** default to exactly the sets they returned before. `GET /api/integrations`,
`/api/connections`, `/api/schedules` accept an optional `contextId`:

* absent → no context filter (the default);
* present → filtering happens in SQL (`.Where(e => e.ResourceContextId == ...)` before materializing),
  not by loading the table and filtering afterwards;
* unknown context → **400**, rather than silently returning an empty set;
* the operator UI does not use the filter in CTX-1.

The repository and management-service interfaces expose the filtered overload with a default interface
implementation so existing in-memory test doubles keep working; the SQL implementations override it
with a real query, and that split is documented on each member.

**ID-based routes are untouched.** `/integrations/{id}`, `/connections/{id}`, `/schedules/{id}`,
`/runs/{id}` resolve by id and never require a ResourceContext.

**DTOs.** `IntegrationDto`, `ConnectionDto` and `ScheduleDetailDto` expose `ResourceContextId` plus a
`ResourceContextRef { Id, Name, Slug }`; `ScheduleDto` exposes `ResourceContextId` only. Resolution
fetches the whole (small) ResourceContext dimension **once per request** and maps in memory — no
per-row lookup. A test asserts `ListAsync` is called exactly once for a 25-row list and `GetAsync`
never.

---

## 9. Authorization non-change

CTX-1 added no `context:{id}` scope key, no context-scoped roles or grants, no scoped user-role
assignments, and no new IAM behavior. `ScopeKeyConstants` and `ResourceTypeConstants` are unchanged;
tests assert no `Context` member appeared in either. `Integration.ScopeKey` / `Connection.ScopeKey`
semantics are untouched, and the governed identity each resource reports is still derived from ScopeKey
alone. Instance-level grants continue to cover every resource, because `instance` still contains every
scope. OperatorCapabilities remains instance-oriented.

---

## 10. Deliberately deferred

**CTX-2** — historical snapshots on OperationRuns, WorkflowInstances, start requests, ScheduleRuns,
PlatformEvents, NotificationInstances, AttentionIncidents, OperationGroups, OperationItems, AuditLogs.
Nothing in CTX-1 obstructs the accepted principle (resolve context once at dispatch/start and propagate
the snapshot forward; never derive history from current ownership): ownership is readable from the
Integration/Schedule at dispatch time, and because contexts are archive-only, a recorded id always
resolves back to a row for display. Do not denormalize name/slug into history.

**CTX-3** — Applications UI and rail placement (the four-domain rail is unchanged; no fifth item),
resource movement with dependency pre-validation, context-scoped authorization, UI context filtering,
and a ResourceContext management controller/API (not added, because no CTX-1 consumer needs one).

**Unchanged by design in CTX-1** — Platform Settings (no ResourceContext scope kind; Integration-scoped
values keep deriving context through their Integration), SecretStore and Connection secret references
(ownership never moves or copies secret material — asserted by test), Notifications/Attention (NTF-C
not reopened; correlation through globally unique `IntegrationId` stands), Business Data
(`DemoBusinessRecordProjection`, Demo Customers, `SharedRecordIdentity`, OperationItem behavior all
untouched; no BusinessRecord table; Demo Customers were **not** assigned to Shared).

**Module-owned persistence** — not retrofitted. Integration ids remain globally unique, so module
tables keyed by `IntegrationId` remain valid. Confirmed hazards carried forward for SVC/DAT planning:

* `Unisa.UnisaFundSourceCampusMaps` — unique `SchoolFundId` with no Integration or context column; two
  Applications cannot map the same school fund.
* `Sample.ApiPolling.ApiPollingSyncStates` — unique `ScopeKey` where the literal `"sample"` fallback is
  an instance-global singleton.
* `Proof.ProofStudentReviewSnapshots` — unique `(IntegrationId, CnsStudentId)`; safe.
* `DemoBusinessRecordProjectionService` — instance-wide `businessKey`; two contexts' customers would
  merge. Accepted, deferred to DAT.
* `NotificationDefinitions` unique `(Scope, IntegrationId, DisplayName)` filtered to user-created — only
  bites if a product requirement demands duplicate names across Applications. No change required now,
  since current uniqueness already includes `IntegrationId`.

---

## 11. Validation evidence

| Suite / build | Result |
|---|---|
| `Apperture.UnitTests` (full, unfiltered) | **1986 passed**, 0 failed (baseline 1881 + 105 new) |
| `Apperture.Management.Ui.Shared.Tests` | **761 passed**, 0 failed |
| `Apperture.ArchTests` | **38 passed**, 0 failed |
| `Apperture.IntegrationTests` (SQL Server) | **199 passed, 1 failed** — see below |
| `Apperture.Management.Api` build | OK |
| `Apperture.Management.Host` build | OK |
| `Apperture.RuntimeHost.Windows.WorkerHost` build | OK |
| `Apperture.Persistence.SqlServer` build | OK |
| `Apperture.Management.Ui.Shared` / `.Ui.Client` / `.Management.Client` builds | OK |
| Full solution build | Compiles clean; blocked on file locks only — see below |

CTX-1 changes no operator-facing UI, so no browser smoke was required. The existing UI still builds.

The full-solution build reports no compiler error in any project. It fails only with MSB3021/MSB3027
file-copy locks on `Apperture.Management.Ui.Server`, whose output DLLs are held by a running instance
of that server (PID 57488). Every other project in the solution built, and `Ui.Server`'s own
dependencies (`Ui.Shared`, `Ui.Client`, `Management.Client`) build clean individually. The running
process was left alone rather than killed.

Baselines were captured on this branch before any production change and matched the expected HUB-1
figures exactly: 1881 / 761 / 38.

### The one integration failure

`ManagementHostGovernanceHttpTests.Post_sftp_test_connect_persists_and_updates_readiness_across_unverified_success_failed`
fails its own precondition: *"Epic 59A requires controlled SFTP endpoint at 127.0.0.1:2222. Set
APPERTURE_TEST_SFTP_HOST/APPERTURE_TEST_SFTP_PORT and start the test SFTP container."* Verified that
nothing is listening on 127.0.0.1:2222. This is a missing local test container, unrelated to CTX-1 and
to any file this epic touched.

### Pre-existing environment repair

The integration suite initially showed a large cascade of `ManagementHost` fixture failures. Root cause
was stale local database state, not CTX-1: `Apperture_BootstrapHttpTests` was stuck at
`20260909120000_Iam5_EntraDirectoryProvisioningAndAccessRequests` because it held 14 Entra
`IdentityLinks` rows with no `TenantId`, which makes the **pre-existing**
`20260909180000_Iam5_EntraTenantScopedIdentityUniqueness` migration refuse to apply by design. CTX-1's
migration sorts after it and was never reached. Those 14 invalid rows were removed — exactly the
remediation that migration's own error message prescribes, and rows the Bootstrap fixture truncates at
test start anyway. `Apperture`, `Apperture_IntegrationTests` and `Apperture_ManagementGovernanceTests`
had already migrated through CTX-1 cleanly against populated data.

### Migration evidence

`ResourceContextMigrationSqlServerTests` builds a throwaway catalog, migrates to
`20260910170000_NtfC_DropLegacyAttentionNotificationTables` (asserting the `ResourceContextId` column
genuinely does not exist yet), inserts two Integrations, two Connections and two ScheduleDefinitions
(one Integration-attached, one standalone), then migrates to latest and asserts:

* the reserved Shared row is seeded, with exactly one reserved context in the table;
* every pre-existing Integration, Connection and ScheduleDefinition carries `ResourceContextId = 'shared'`;
* no id was rewritten, and a pre-existing `ScopeKey` of `environment:prod` survived untouched;
* all three `ResourceContextId` columns are `NOT NULL`;
* `IX_{table}_ResourceContextId` exists on each of the three tables;
* a `NO ACTION` foreign key to `ResourceContexts` exists on each;
* `UX_ResourceContexts_Slug` exists and is unique;
* `DELETE FROM ResourceContexts WHERE Id = 'shared'` fails — the FK is structural backup for the
  archive-only rule even against raw SQL.

### New test coverage (105 tests)

* `ResourceContextSlugTests` — normalization table, empty-result rejection, idempotence, truncation,
  and that the reserved set is exactly `shared` + `system`.
* `ResourceContextServiceTests` — Shared identity and seeded shape; Shared cannot be renamed,
  re-slugged or archived; no delete member exists on either contract (reflection); Application creation;
  duplicate names allowed and duplicate slugs rejected; reserved and unusable slugs rejected; archive
  blocked while owning resources; archive/unarchive round-trip; archived contexts still resolve by id;
  `ResolveForNewOwnershipAsync` defaults to Shared and rejects unknown/archived.
* `ResourceContextReferenceRuleTests` — same context, Shared, cross-Application, and Shared-cannot-reach-
  into-an-Application.
* `ResourceOwnershipTests` — Integration/Connection defaults, explicit-active, unknown and archived
  rejection; ScopeKey unaffected; ownership does not touch the secret store; all five Schedule ownership
  cases including explicit-Shared-under-an-Application rejection; list defaults versus filtered.
* `BindingResourceContextRuleTests` — eligibility across two Applications plus Shared, Shared Integration
  excluded from Application-only Connections, no-owning-context discovery, and four mutation cases.
* `RuntimeSlotResolutionAmbiguityTests` — two Integrations of the same module cannot resolve each
  other's Connection; the contextless lookup returns nothing when every binding is owned (and performs
  no connection lookup at all); a genuinely global binding still resolves.
* `ResourceContextApiCompatibilityTests` — legacy JSON create bodies still bind; explicit ids bind; list
  without/with `contextId`; unknown `contextId` → 400; DTO ownership exposure with a single dimension
  query; create forwards the optional id.
* `ResourceContextAuthorizationNonChangeTests` — no `context:` scope-key form, no `ResourceContext`
  resource type, governed identities ignore ownership, default ownership does not alter visibility.

---

## 12. Corrections applied to the design gate

The design gate remains the basis for this epic. Three points were tightened during implementation and
are recorded here rather than rewritten out of that document:

1. **Hard delete removed from supported behavior** (gate §2.4 listed hard delete as permitted for an
   unused Application). A FK only proves *current* emptiness; CTX-2 needs every recorded id to resolve.
   Archive-only, with the FK as structural backup. See §5.
2. **Reserved slugs narrowed** to `shared` and `system` (gate §2.3 also reserved `apps`, `system`, `new`
   and `api`). Route-prefix terms are not reserved until a slug-addressed route defines them.
3. **`GetBySlotAsync` closed in CTX-1, not deferred** (gate §10-R2 flagged it as "close before CTX-3"),
   and closed by requiring the Integration identity rather than by adding a context filter — the
   resolver has no owning context to supply on that path. See §7.

Additionally, the gate's wording conflict on list filtering is resolved as the prompt directed: CTX-1
adds **no UI** context filtering, but does expose the optional server-side `contextId` query parameter
CTX-3 will need. See §8.

---

## 13. CTX-2 readiness

* `ResourceContextId` is readable on Integration, Connection and ScheduleDefinition at dispatch time,
  which is where CTX-2 resolves context once and propagates the snapshot forward.
* ResourceContext rows are never physically deleted, so a historical `ResourceContextId` always resolves
  back to a row for its current display name. Do not denormalize name/slug into historical records.
* Historical snapshot columns should be plain nullable strings with no FK: pre-CTX-2 history genuinely
  has no recorded context and must read as unknown rather than be back-dated to Shared.
* No CTX-1 decision forecloses CTX-3 movement.

CTX-2 was not started.

---

## 14. Acceptance criteria

| Criterion | Status |
|---|---|
| `ResourceContexts` exists | ✅ |
| Shared seeded as reserved `"shared"` | ✅ |
| Integrations, Connections, ScheduleDefinitions have required ResourceContext ownership | ✅ |
| Existing rows backfill to Shared | ✅ (proved on a populated SQL Server database) |
| Global resource IDs/routes unchanged | ✅ |
| Existing create clients remain compatible | ✅ |
| Integration and Connection default to Shared | ✅ |
| Integration-attached Schedules inherit Integration context | ✅ |
| Standalone Schedules default to Shared | ✅ |
| Cross-context Integration→Connection binding rejected unless Connection is Shared | ✅ |
| Binding rule enforced in both eligibility and mutation | ✅ |
| Context-blind runtime resolution cannot select another Integration's binding | ✅ |
| Current global list behavior unchanged by default | ✅ |
| ResourceContext and authorization ScopeKey remain distinct | ✅ |
| No context IAM, UI, movement, history snapshots, Services, Resource Catalog, or Business Data work started | ✅ |
| Full validation green | ✅ except one pre-existing environment-dependent SFTP test (§11) |
