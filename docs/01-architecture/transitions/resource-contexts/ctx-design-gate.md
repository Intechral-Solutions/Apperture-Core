# ResourceContext design gate (CTX-1 / CTX-2 / CTX-3)

Date: 2026-09-11
Status: **Design only.** No production code changed, no migration authored, no CTX work started.
Audience: the agent implementing CTX-1, and the reviewer approving this direction.

Evidence base: current code read this pass (entities, repositories, authorization, module DbContexts, migration conventions), plus `docs/01-architecture/transitions/application-contexts/current-inventory.md` §11–§12 (prior hazard catalogue, re-verified where load-bearing), ADR-0040, and the NAV-1 / HUB-1 closure reports.

---

## 1. Executive recommendation

**The ResourceContext direction is sound. Proceed to CTX-1 with three corrections.**

The model works because it adds a *dimension*, not an *identity*. Global 24-hex IDs stay canonical, `/integrations/{id}` keeps resolving, and after the CTX-1 backfill every resource sits in one reserved `Shared` context — so the product behaves exactly as it does today. That is the right shape for a first persisted ownership change.

Three corrections to the stated direction:

1. **Schedules must own a direct `ResourceContextId`, not derive one from their Integration.** Derivation is impossible for standalone schedules (which legally exist today) and impossible for `ScheduleRuns`, which carries no `IntegrationId` at all (verified). Derivation would also make "move an Integration" silently re-home its Schedules. Use a direct column plus an application-level invariant that an Integration-attached Schedule matches its Integration's context. (§3, §10-R5)

2. **`IBindingRepository.GetBySlotAsync(moduleId, slotName)` is the single most dangerous existing code path** and must be closed before two Applications can hold Integrations of the same module. It returns the *first* binding matching `(ModuleId, SlotName)` instance-wide with no Integration filter (verified in `SqlBindingRepository`), and `SlotBoundResolver` falls back to it when no integration id is present. Under multiple contexts this can silently resolve another Application's Connection. This is a correctness bug waiting on a trigger, not a theoretical one. (§10-R2)

3. **Enforce the reference rule in CTX-1 even though it is a no-op there.** "A resource may reference resources in its own context or in Shared" costs nothing while everything is in Shared, but retrofitting it *after* CTX-3 lets operators move resources means fixing already-invalid data. Ship the rule with the ownership columns. (§4)

Everything else in the brief's stated direction survives scrutiny unchanged.

---

## 2. Final ResourceContext entity contract

### 2.1 Fields

| Field | Type | Notes |
|---|---|---|
| `Id` | `nvarchar(64)`, PK | Reserved literal `"shared"` for Shared; `Guid.NewGuid().ToString("N")[..24]` for all others |
| `Name` | `nvarchar(256)`, required | Display name. **Not unique** — matches Integrations/Connections, which already permit duplicate display names |
| `Slug` | `nvarchar(128)`, required | Unique (case-insensitive). Address/display namespace, never identity |
| `Kind` | `nvarchar(32)`, required | String, not an enum column: `"Shared"` \| `"Application"`. Future kinds are additive with no migration |
| `State` | `nvarchar(32)`, required | `"Active"` \| `"Archived"` |
| `IsReserved` | `bit`, required, default 0 | True only for Shared. Mirrors the established `Roles.IsSystemRole` pattern |
| `Description` | `nvarchar(1024)`, null | Optional. CTX-3 wants it; cheap to add now, annoying to add later |
| `CreatedAtUtc` | `datetimeoffset` | |
| `UpdatedAtUtc` | `datetimeoffset` | |
| `RowVersion` | `rowversion` | Matches `Integrations` concurrency handling |

**Rejected: a metadata JSON column.** The brief is right to be suspicious. There is no CTX-1 consumer, and a generic bag becomes the de facto ResourceContext metadata store — the exact failure mode `platform-settings-option-providers-and-scopes.md` already warns about for Platform Settings. Add it when a second concrete consumer exists.

### 2.2 Why a literal `"shared"` ID rather than a generated one

Deterministic IDs already exist in this platform: `ScopeKeyConstants.Instance = "instance"`, `RuntimePlaneId "local"`, the Sample.ApiPolling `"sample"` scope fallback. A reserved literal:

- lets the CTX-1 migration `INSERT` the row and `UPDATE ... SET ResourceContextId = 'shared'` in the same deterministic step, with no "read back the generated id" dance;
- is readable in raw SQL during support, which matters because every existing row will carry it;
- gives code a single constant (`ResourceContextIds.Shared`) instead of a runtime lookup on the hot path.

The column stays `nvarchar(64)` like every other operator ID, so mixed literal/hex IDs are a formatting difference, not a schema one.

### 2.3 Slug semantics

- **Normalization:** lowercase; trim; collapse internal whitespace and `_` to `-`; strip characters outside `[a-z0-9-]`; collapse repeated `-`; trim leading/trailing `-`. Default-derived from `Name` at create time, then independently editable.
- **Uniqueness:** unique index, case-insensitive (SQL Server default collation is already case-insensitive; make it explicit in the index definition rather than relying on collation).
- **Reserved slugs:** `shared` is taken by the reserved row. Also reserve `apps`, `system`, `new`, and `api` so a future `apps/{slug}` addressing scheme cannot collide with its own route prefixes. Cheap to reserve now, breaking to reserve later.
- **Editable:** yes, for Application contexts. **No alias/redirect table.** IDs are canonical in every stored reference and every existing route, so a rename breaks only human-typed/bookmarked slug URLs — and slug URLs do not exist yet. Introducing alias history now would be building for a hypothetical.
- **Shared's slug is immutable** (`IsReserved` guard).

### 2.4 Lifecycle

| Operation | Application context | Shared |
|---|---|---|
| Create | CTX-3 | Never (migration-seeded only) |
| Rename (`Name`) | Allowed | Blocked (`IsReserved`) |
| Change slug | Allowed | Blocked |
| Archive (`State = Archived`) | Allowed **only when it owns no Active resources** | Blocked |
| Un-archive | Allowed | n/a |
| Hard delete | **Never once it has owned anything** — enforced structurally by FK `Restrict` (§5.2). An Application created and never used can be deleted; the FK permits it precisely because nothing references it | Blocked |

`Active` / `Archived` is sufficient. Do not build a state machine.

**Archived contexts keep resolving.** Historical rows referencing an archived context must still render its name — that is the whole point of never hard-deleting. Archiving hides a context from *creation* pickers and default *listing*; it does not invalidate references.

---

## 3. Ownership matrix

Classification: **System** (never context-owned) · **Direct** (owns a `ResourceContextId` column) · **Derived** (context obtained through an immutable reference) · **Snapshot** (CTX-2 historical capture) · **Deferred**.

| Resource | Class | CTX phase | Rationale |
|---|---|---|---|
| **Integration** | Direct, required | CTX-1 | The primary ownership anchor. Backfills to Shared |
| **Connection** | Direct, required | CTX-1 | Stays reusable and *not* Integration-owned. A generally reusable Connection belongs in Shared. Backfills to Shared |
| **ScheduleDefinition** | **Direct, required** | CTX-1 | See §1 correction 1. Invariant: if `IntegrationId` is set, context must equal that Integration's context |
| **Binding** | **Derived** (no column) | — | A pure edge between Integration and Connection. Its context *is* the Integration's. A column here is a third source of truth that can drift from both endpoints. Enforce the reference rule at bind time instead |
| **PlatformSettingValues** | **Derived / no change** | — | `PlatformSettingValueScopeKind` is `Instance \| Integration` only (verified). Integration-scoped values derive context via the Integration. **Do not add a Context scope kind in CTX-1** — there is no CTX-1 consumer, and it would make Platform Settings the ResourceContext metadata store |
| **NotificationDefinitions / Subscriptions** | Derived via `IntegrationId` | — | User-created definitions are always `Scope = integration` with an `IntegrationId`. Platform/module-scoped definitions are System-level. No column needed |
| **AttentionIncidents / IntegrationAttentionRoutings** | Derived via `IntegrationId` | — | Routing config is per-Integration. Incidents are historical → CTX-2 snapshot (§6) |
| **Modules / ModuleInstallationStates** | **System** | — | Installed module definitions are platform administration. Never context-owned |
| **Users / Roles / IdentityLinks / Entra** | **System** | — | IAM stays outside the hierarchy entirely |
| **Demo Business Data (`DemoBusinessRecordProjection`)** | **Deferred** | — | A read-time projection over `OperationItems`, not a stored aggregate. Manufacturing context ownership for a projection that DAT will delete is pure waste. Leave it globally transitional |
| **OperationRuns / WorkflowInstances / start requests / ScheduleRuns / PlatformEvents / NotificationInstances / OperationGroups / OperationItems / AuditLogs** | **Snapshot** | CTX-2 | §6 |
| **Future Services** | Direct | SVC | Nothing in ResourceContext changes to accommodate them |
| **Future Dashboards / Workflows / Business Data Collections** | Direct | DSH / WIZ / DAT | Same |

The matrix's shape is the load-bearing claim: **three Direct columns in CTX-1** (Integration, Connection, Schedule). Everything else derives, snapshots later, or stays System. Resist adding a fourth.

---

## 4. Reference rules

### 4.1 The rule

> A resource may reference resources **in its own context** or **in Shared**. It may not reference resources in any other context.

In CTX-1 every row is in Shared, so the rule is vacuously true and changes no behavior. That is exactly why it should ship in CTX-1: the enforcement point is written and tested while it cannot break anything.

### 4.2 Where to enforce first

**Integration → Connection bindings**, in `BindingEligibilityService` / the eligible-connections query that already backs the HUB-1 Connections tab. Extend eligibility from "contract type matches" to "contract type matches **and** (`connection.ResourceContextId == integration.ResourceContextId` or `connection.ResourceContextId == 'shared'`)".

Defer enforcement everywhere else. Schedule→Integration is already covered by the §3 invariant.

### 4.3 When a move invalidates an existing Binding

Movement does not exist until CTX-3, but the model must answer this or CTX-3 will have to invent it:

- **Do not cascade, do not silently unbind.** A binding that becomes cross-context is a *readiness* problem, not a data-integrity one.
- Surface it through the existing readiness machinery: add a readiness issue code (e.g. `binding_cross_context`) alongside the existing `binding_not_eligible` / `binding_contract_mismatch` codes. It renders in the HUB-1 Connections tab with a Fix-now action for free.
- CTX-3's move operation must *pre-validate* and refuse a move that would orphan bindings, rather than performing the move and leaving wreckage (§7.3).

---

## 5. CTX-1 exact scope

### 5.1 Schema

New table `ResourceContexts` per §2.1, plus:

```
Integrations.ResourceContextId         nvarchar(64) NOT NULL  FK → ResourceContexts(Id) ON DELETE NO ACTION
Connections.ResourceContextId          nvarchar(64) NOT NULL  FK → ResourceContexts(Id) ON DELETE NO ACTION
ScheduleDefinitions.ResourceContextId  nvarchar(64) NOT NULL  FK → ResourceContexts(Id) ON DELETE NO ACTION
```

Non-unique index on each `ResourceContextId` (CTX-3 filters by it).
Unique index on `ResourceContexts.Slug`.

**On adding real foreign keys**, which deviates from house style: this codebase deliberately uses string correlation rather than FKs *between operator aggregates* (`IntegrationId` on runs, schedules, notifications, etc.). That convention exists to avoid coupling aggregate lifecycles. A FK to a small dimension table is a different case, and it buys a structural guarantee that is otherwise application-code-only: **a context that owns anything cannot be deleted**. Recommend taking the FK, and stating the deviation in the CTX-1 commit so it is not read as drift.

### 5.2 Migration

One migration, named per convention (`{timestamp}_Ctx1_ResourceContextFoundation.cs` — matching `20260909180000_Iam5_EntraTenantScopedIdentityUniqueness.cs`). `Up` in order:

1. `CreateTable ResourceContexts`.
2. `InsertData` the reserved Shared row — `Id='shared'`, `Name='Shared'`, `Slug='shared'`, `Kind='Shared'`, `State='Active'`, `IsReserved=1`. **Seeding inside the migration is established precedent** — 9 existing migrations already use `InsertData`/`INSERT INTO` (verified). This is strictly better than a bootstrap hosted service here, because the backfill in step 4 depends on the row existing and must not race startup.
3. `AddColumn ResourceContextId` **nullable** on the three tables.
4. `Sql("UPDATE ... SET ResourceContextId = 'shared' WHERE ResourceContextId IS NULL")` on each.
5. `AlterColumn` each to `nullable: false`.
6. Add FKs and indexes.

Single migration, not two deploys: the control plane runs migrations at startup via `PersistenceMigrationHostedService`, and this is a single-instance platform — there is no rolling-deploy window where old and new code both write rows. A two-phase nullable-then-required split would buy nothing and leave a nullable column in the model snapshot in between.

`Down` drops FKs/columns/table in reverse. Do not edit historical migrations.

### 5.3 Repository / API behavior — deliberately almost nothing changes

- **List APIs (`GET /api/integrations`, `/connections`, `/schedules`): unchanged, return everything.** Verified why this is safe: `SqlIntegrationRepository.GetVisibleAsync` loads all rows and filters in memory by `IsScopeContained(row.ScopeKey ?? "instance", requestedScopeKey)`, and `IntegrationManagementService` always passes `ScopeKeyConstants.Instance`, which `ScopeContainmentEvaluator` treats as containing everything. Adding a column does not touch that path. **Accept an optional `contextId` query parameter that CTX-1 wires but the UI does not use** — it makes CTX-3 a UI-only change.
- **`GET /{id}` routes: unchanged.** IDs stay globally unique; never require a contextId to resolve one.
- **Create APIs: accept optional `ResourceContextId`, default to `'shared'` when absent.** Additive, compatible, and means CTX-3 needs no API change. Validate that the supplied context exists and is `Active`.
- **Response DTOs: expose `ResourceContextId` immediately, plus a small `ResourceContextRef { Id, Name, Slug }` on *detail* DTOs** (not list DTOs — that is where the N+1 risk lives, §10-R9). Doing this in CTX-1 avoids a second round of DTO churn in CTX-3 and costs one join.
- **No context filtering in any query. No move operation. No context-scoped authorization. No UI.**

### 5.4 Tests for CTX-1

- Migration: applies to a populated database; all pre-existing Integrations/Connections/Schedules land in `'shared'`; `Down` reverses.
- Reserved row: exists, `IsReserved`, cannot be renamed / re-slugged / archived / deleted through the service layer.
- Slug: normalization table-driven cases; uniqueness violation surfaces a clean error; reserved slugs rejected.
- FK: deleting a context that owns resources fails.
- Reference rule: a Connection in context A is not eligible for an Integration in context B; a Shared Connection is eligible for both. (Constructed in-memory — production data is all Shared.)
- Compatibility: list endpoints return the same set before and after; `GET /{id}` unaffected; existing Integration/Connection/Schedule tests unchanged and green.
- Schedule invariant: attaching a Schedule to an Integration in a different context is rejected.

### 5.5 Explicit CTX-1 non-goals

No context-scoped roles or grants. No `context:{id}` scope key. No move/reassign. No context filtering in list queries or UI. No Applications UI. No nested contexts (**no parent column** — there is no compelling schema reason, and adding one invites premature hierarchy logic). No Business Data changes. No module-table changes. No Platform Settings scope kind. No Services.

---

## 6. CTX-2 exact scope — historical provenance

### 6.1 The rule

> Historical context must not change when the current owning resource later moves.

This forces **snapshot**, not derivation, for anything that records an occurrence. Verified correlation columns per entity:

| Entity | `IntegrationId` | `ScheduleId` | Snapshot needed? |
|---|---|---|---|
| `OperationRuns` | ✓ | — | **Yes** |
| `WorkflowInstances` | ✓ | — | **Yes** |
| `WorkflowStartRequests` / `OperationStartRequests` | ✓ | — | **Yes** — this is where context is *resolved* |
| `ScheduleRuns` | **✗** | ✓ | **Yes — and it cannot derive at all.** No `IntegrationId` column exists |
| `PlatformEvents` | ✓ | ✓ | Yes |
| `NotificationInstances` | ✓ | ✓ | Yes |
| `AttentionIncidents` | ✓ | — | Yes |
| `OperationGroups` / `OperationItems` | ✓ | — | Yes |
| `AuditLogs` | **✗** (ModuleId / ScheduleId / RunId only) | ✓ | Yes, nullable — much audit is System-level and legitimately has no context |

`ScheduleRuns` and `AuditLogs` are the proof that derivation is not viable as a general strategy: neither can reach an Integration.

### 6.2 Snapshot representation

**A plain `ResourceContextId` string column, no FK, nullable.**

- **No FK**, because history must survive independently of the dimension table's constraints and must be writable on a hot path without a referential check.
- **No name/slug denormalization.** Names are mutable, but the context row is never hard-deleted once referenced (§2.4, enforced by the CTX-1 FK), so the ID always resolves for display. Snapshotting a name would freeze a *display* value while the ID still points at the live row — two sources of truth for one label, and the stale one wins on old rows. Resolve names at read time.
- **Nullable**, because pre-CTX-2 history genuinely has no recorded context and should read as "unknown", not be back-dated to Shared. Backfilling old runs to Shared would assert something the data does not support. (Backfilling *current ownership* to Shared in CTX-1 is fine — that is a true statement about the present. Backfilling *history* is not.)

### 6.3 Propagation

Resolve context **once, at dispatch/start**, and carry it forward:

```
start request (resolve context from Integration here, write it)
  → OperationRun / WorkflowInstance   (copy from start request)
    → OperationGroups / OperationItems (copy from run)
```

`ScheduleDispatchRunner` writes the Schedule's own context onto the start request. WorkerHost **must never re-query the current Integration's context** — that is precisely how a later move corrupts history. The existing one-run/one-execution architecture is unchanged; this is an extra field on records that already flow through it.

---

## 7. CTX-3 exact scope — Application experience

### 7.1 Surfaces

List/create/inspect ResourceContexts of `Kind = Application`; browse a context's resources; create resources into the selected context; view Shared.

**Shared appears as a sibling surface, not as an item in the Applications list.** It is a different `Kind` with different rules (immutable, cannot be created, cannot be archived). Listing it alongside user-created Applications invites operators to treat it as one and be surprised when every management action is disabled.

### 7.2 Rail placement — the one genuine open question

ADR-0040 deliberately fixed a four-domain rail (Control Center / Integrations / Business Data / Config) and rejected competing domains. Applications is a *cross-cutting organizational* surface, not a fifth resource domain, and the ADR's reasoning does not obviously settle where it goes. Candidates: a Config sub-section; a filter/switcher in the existing shell chrome; a fifth rail domain (requires amending ADR-0040). **This is a product IA decision and should be made by the same person who approved ADR-0040** — see §13.

### 7.3 Movement

Introduce it in CTX-3, not CTX-1. A move is an **operation with pre-validation**, never `UPDATE ContextId`:

| Moving | Must validate |
|---|---|
| Integration | Its bindings' Connections remain reachable (same-context or Shared) after the move; its attached Schedules move atomically with it (the §3 invariant) |
| Connection | Every Integration currently binding it remains able to reach it — i.e. moving a Shared Connection *into* an Application breaks every other Application's binding. This is the highest-risk move and should require explicit confirmation listing affected Integrations |
| Schedule | If attached to an Integration, it cannot move independently |

Refuse the move and report what would break; do not move-then-flag.

---

## 8. Migration hazard analysis

| Area | Hazard | Verdict |
|---|---|---|
| Unique indexes on `Integrations` / `Connections` / `Bindings` / `ScheduleDefinitions` | None beyond PK — duplicate display names already allowed | **Safe.** Backfill cannot violate anything |
| `PlatformSettingValues` unique `(Key, ScopeKind, ScopeId)` | No Context scope kind exists | **Safe**, because CTX-1 adds none |
| `NotificationDefinitions` unique `(Scope, IntegrationId, DisplayName)` filtered `Origin='userCreated'` | Blocks the same user-created definition name in two contexts | **Latent**, not CTX-1. Only bites if a product requirement demands duplicate names across Applications |
| `AttentionIncidents` unique `(IntegrationId, ConditionKey)` where Active; `IntegrationAttentionRoutings` unique `(IntegrationId, ConditionKey)` | Integration-scoped, integration IDs globally unique | **Safe** |
| `ScheduleRuns` unique `(ScheduleId, FireTimeUtc)` | Schedule IDs stay globally unique | **Safe** |
| Start-request `IdempotencyKey` uniqueness | Unchanged | **Safe** |
| `SqlBindingRepository.GetBySlotAsync(moduleId, slotName)` | Returns first instance-wide match, no Integration filter; `SlotBoundResolver` falls back to it | **Live hazard — see §10-R2.** Must be closed before multi-context Integrations of the same module exist |
| `Unisa.UnisaFundSourceCampusMaps` unique `SchoolFundId`, **no Integration or context column** | Two Applications cannot map the same school fund | **Module-owned hazard.** Not CTX-1's job; must be on the SVC/DAT planning list |
| `Sample.ApiPolling.ApiPollingSyncStates` unique `ScopeKey` (integration id, or literal `"sample"`) | The `"sample"` fallback is an instance-global singleton | **Latent**, sample module only |
| `Proof.ProofStudentReviewSnapshots` unique `(IntegrationId, CnsStudentId)` | Integration-scoped | **Safe** |
| `DemoBusinessRecordProjectionService` — instance-wide `businessKey` | Two contexts' customers would merge in the projection | **Accepted**, deferred to DAT (§3) |
| `ISecretStore` / Connection `SecretRefs` | Secrets are referenced by opaque ref, not by context | **Safe, and must stay so.** A Connection moving contexts must not move or copy secrets. Do not put secrets in ResourceContext |
| Test fixtures that truncate tables | FK `Restrict` imposes delete ordering | **Minor.** Flag for the implementer |

---

## 9. Authorization compatibility

**CTX-1 changes no authorization behavior.** Verified mechanics:

- `ScopeKeyConstants`: `instance` / `environment:{id}` / `module:{id}` / `connection:{id}` / `operation:{id}` / `module:{id}:slot:{name}`.
- `ScopeContainmentEvaluator`: exact match, `instance` contains everything, and prefix-plus-colon containment. Nothing else.
- `Integration.ScopeKey` / `Connection.ScopeKey` are nullable, default to `"instance"`, and feed `GetIdentity()` → authorization. **They are an authorization scope, not a product context, and CTX-1 must not read, write, or reinterpret them.**

A later epic adds `context:{resourceContextId}` as a new scope-key form. Two properties make that additive rather than breaking:

1. `instance` already contains every scope, so existing instance-level grants automatically cover all contexts. Introducing context scopes *narrows* for principals who are given narrow grants; it does not revoke anything from existing ones.
2. The scope-key format is a string convention with no enum to widen.

It will also need `ResourceTypeConstants.ResourceContext` for permissions *on contexts themselves* (who may create/rename/archive an Application). That is a CTX-3-or-later concern.

**The "resources disappear" check:** the only list paths are `GetVisibleAsync(scopeKey)` on Integrations and Connections, both of which filter on `ScopeKey` and are always called with `instance`. Adding `ResourceContextId` touches neither the filter nor the call. No list can shrink in CTX-1.

---

## 10. Red-team findings

**R1 — `GetVisibleAsync` loads every row and filters in memory.** Verified in `SqlIntegrationRepository` / `SqlConnectionRepository`. Harmless today; but when CTX-3 adds context filtering, layering it onto this pattern means a full table load per request. *Push context filtering into SQL when you add it; do not extend the in-memory filter.*

**R2 — `GetBySlotAsync` is context-blind (highest severity).** `FirstOrDefaultAsync(e => e.ModuleId == moduleId && e.SlotName == slotName)` — no Integration, no context, first match wins. `SlotBoundResolver` uses it whenever an integration id is absent. Two Applications with Integrations of the same module can resolve each other's Connections. *Close before CTX-3 enables real multi-context use: either require an integration id on that path, or filter by context.*

**R3 — Shared as hidden global coupling.** Real risk: "put it in Shared" becomes the path of least resistance and Shared turns into the old global namespace with extra steps. Mitigations: make Shared a deliberate placement (never a default for *new* Application resources once CTX-3 exists — only the CTX-1 backfill default); surface reuse in CTX-3 ("this Shared Connection is used by N Applications"); make moving *out* of Shared the operation that shows blast radius (§7.3).

**R4 — Redundant context columns.** Guarded by holding the line at three Direct columns (§3). Every additional column is a second source of truth that can disagree with the Integration's. The Binding case is the concrete temptation; resist it.

**R5 — Deriving context from Integration breaks history and standalone schedules.** Verified: `ScheduleRuns` has no `IntegrationId`; `AuditLogs` has none; standalone Schedules exist. This is why Schedules take a direct column and history takes snapshots.

**R6 — Slug rename hazards.** Contained: IDs are canonical in storage and routes, no slug-addressed URLs exist yet, so rename is safe *if* it happens before slug addressing ships. Reserve the route-prefix slugs now (§2.3).

**R7 — Reserved-row bootstrap.** Seeding in the migration (with `InsertData` precedent) avoids the startup race a hosted service would create with the backfill. The failure mode to avoid is seeding in a hosted service while the migration's backfill expects the row.

**R8 — List API behavior drift.** Explicitly guarded by §5.3 and a compatibility test (§5.4). The optional `contextId` parameter must default to "no filter", not to "Shared".

**R9 — Context-aware loading going N+1.** Real: a list of 200 Integrations each resolving its context name is 200 lookups. Mitigations: put `ResourceContextRef` on *detail* DTOs only; in CTX-3 lists, either join once or fetch the (small) context set and map in memory.

**R10 — `ScopeKey` confusion.** Two adjacent concepts (`connection:{id}` authz scope vs. a Connection's ResourceContext) will be conflated by a future reader. Mitigate with naming discipline (§11) and an explicit comment on `Integration.ScopeKey` / `Connection.ScopeKey` stating they are authorization scopes, unrelated to ResourceContext.

**Not found:** no evidence that global IDs are insufficient, that a reserved Shared row creates a migration problem, or that current authorization semantics conflict with ownership. Those concerns from the brief do not survive contact with the code.

---

## 11. Naming

**Keep `ResourceContext`.** Reviewed collisions:

| Existing | What it is | Collision? |
|---|---|---|
| `ResourceDescriptor` | Authz `(type, scopeKey)` pair | Adjacent vocabulary, no conflict |
| `ResourceTypeConstants` | Authz resource-type names | Will gain a `ResourceContext` member later; no conflict |
| `ScopeKey` / `ScopeKeyConstants` | Authz boundary strings | **Conceptually adjacent — the real readability risk (R10)** |
| Entra `AuthenticationContextId` | Conditional Access `c1`–`c25` | Unrelated |
| `IStepExecutionContext`, `IModulePlatformEmitContext`, `IntegrationConfigurationTestContext` | Per-execution ambient contexts | Semantically different ("context" as ambient state vs. as ownership namespace) |

Discipline to apply: never abbreviate to `Context` in code (always `ResourceContext`); never say "context" unqualified in XML docs near execution code; the UI says **Application** and **Shared**, never "ResourceContext". Do not name it `ApplicationContext` — that locks the model to one Kind, which is exactly what the Shared/Department kinds are meant to avoid.

---

## 12. Implementation ordering inside CTX-1

1. Domain: `ResourceContext` record, `ResourceContextId`, `ResourceContextIds.Shared`, `ResourceContextKinds`, slug normalizer **+ its unit tests** (pure logic, no schema).
2. Persistence: entity, EF configuration, the migration (§5.2), model snapshot.
3. Repository + management service: get/list/create/rename/archive with `IsReserved` guards.
4. Add `ResourceContextId` to the three domain records and their mappers; default to Shared on create.
5. DTO exposure + optional `contextId` create parameter + optional (unwired) list filter.
6. Reference rule in binding eligibility (§4.2) + Schedule/Integration context invariant.
7. Compatibility tests (§5.4) last, as the gate.

Steps 1–2 are independently reviewable and carry all the schema risk. If the epic is split, split there.

---

## 13. Acceptance criteria for CTX-1

- `ResourceContexts` table exists per §2.1; reserved Shared row (`Id='shared'`, `IsReserved=1`) is present after migration.
- `Integrations`, `Connections`, `ScheduleDefinitions` each have a required `ResourceContextId` with an FK to `ResourceContexts` and a non-unique index.
- Every pre-existing row in those three tables is in `'shared'` after migrating a populated database. No operator classification was required.
- No resource ID changed. `GET /api/integrations/{id}`, `/connections/{id}`, `/runs/{id}` behave identically.
- `GET /api/integrations`, `/connections`, `/schedules` return the same sets as before; the optional `contextId` parameter exists and defaults to no filtering.
- Create APIs accept an optional `ResourceContextId` and default to Shared; a non-existent or archived context is rejected.
- Detail DTOs expose `ResourceContextId` and a `ResourceContextRef`.
- Shared cannot be renamed, re-slugged, archived, or deleted via the service layer; a context owning resources cannot be deleted.
- Binding eligibility rejects a Connection whose context is neither the Integration's nor Shared; a Schedule cannot attach to an Integration in a different context.
- Slug normalization, uniqueness, and reserved-slug rejection are covered by tests.
- No context-scoped roles, grants, or scope keys were added. `Integration.ScopeKey` / `Connection.ScopeKey` semantics are untouched.
- Full unfiltered `Apperture.UnitTests`, `Apperture.Management.Ui.Shared.Tests`, and `Apperture.ArchTests` are green; the SQL Server integration suite passes including a migration-on-populated-database test.

---

## 14. Open questions

Only two genuinely require a product decision. Everything else in the brief is resolved above.

1. **CTX-3 rail placement for Applications** (§7.2). Interacts directly with ADR-0040's deliberate four-domain rail. Needs the ADR-0040 owner, not an implementer. **Does not block CTX-1** — no CTX-1 decision depends on it.

2. **Whether two Applications must ever hold user-created Notification Definitions with the same display name** (§8). If yes, the filtered unique index `(Scope, IntegrationId, DisplayName)` needs widening and that is best done as a deliberate follow-up rather than discovered. If no (the likely answer, since the index is already per-Integration), nothing changes. **Does not block CTX-1.**

Recommendation on a third question the brief raised — *should movement be in CTX-1?* — resolved, not open: **no.** CTX-1 establishes ownership; CTX-3 introduces movement with pre-validation. Nothing in the CTX-1 schema forecloses it.
