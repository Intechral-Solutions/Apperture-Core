# Services — design gate

Date: 2026-09-23
Status: **Design gate complete and approved (2026-09-23). Implemented 2026-09-24 (SVC-1…SVC-8, branch `services-connection-workspace`, not merged) — see §35, [ADR-0044](../../../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md) and the [closure](../../../audits/services-connection-workspace-closure.md).** The text below is the design as approved and is left as written; §35 records where implementation differs from it.
Audited HEAD: `2fa08573ccf01222257526a482394b1170a9e37f` (`master`, clean tree).

> **Update (2026-09-23): Components implementation is complete** ([closure](../../../audits/components-catalog-closure.md)).
> Nothing in this gate was amended: the Connectors tab was kept and its rows link to `/components/connector/{id}`
> (SVC-4 still owns its retirement), `/connections/new?connectorDefinitionId=` now exists (A2), and the Component
> catalog shipped unchanged by Services. **Services implementation (SVC-1…SVC-8) is unblocked and has not started.**
>
> *(That was the state when the gate was approved. Implementation is now complete — see §35.)*

Product rulings **D-S1 through D-S4** were reviewed and **approved** on 2026-09-23 and are recorded as decided in
§32. Four consistency corrections (C1–C4) were applied at the same time: the provider seam was reduced to one
coherent minimal design (§12), the SQL safety contract was tightened to forbid dynamic identifier SQL (§20.1), the
audit implementation quirk was **re-verified against current code** and the findings corrected (§21), and a stale
ResourceContext matrix row was annotated as superseded (§10).

Required inputs consumed in full: [`components-design-gate.md`](./components-design-gate.md) §23 (handoff) and §19
(Resource-relationship prohibitions), the boundary rulings in [`README.md`](./README.md), and near-term item 5 of
[`pre-release-product-roadmap.md`](../../../00-overview/pre-release-product-roadmap.md).

Everything below is grounded in code at that commit. Where a prior planning note, an ADR, or the stated product
hypothesis disagrees with the code, **the code wins** and the disagreement is stated rather than smoothed over.

This document deliberately separates four things and labels them throughout:

- **Code fact** — verified in the repository at this commit.
- **Selected architecture** — this gate's approved position, grounded in those facts.
- **Hypothesis rejected** — something the brief proposed that the audit does not support.
- **Product ruling** — a product call, now decided and recorded in §32.

---

## 1. Executive recommendation

**Answer to the central design question: B — approved.** "Service" is the architectural and roadmap term for this
capability body. It is **not** a domain abstraction, not an entity, not a route, not a permission, not a type, and
**not an operator-facing noun** (D-S4). Operators continue to work with Connections, Connection detail, and
connector-specific tools.

**Approved v1 scope is S-B** (D-S1): the evolved Connection workspace foundation, the provider/tool seam, and a
**SQL Server structure-browser exemplar** — platform-authored closed metadata operations only. An arbitrary SQL
query console is **not** approved, is **outside every SVC plan**, and would require its own security/design gate
(§20.2, §27.2).

The smallest truthful architecture the code supports:

| # | Ruling | Grounding |
|---|---|---|
| 1 | **No Service entity, no Service table, no Service id.** Identity, where one is needed, **is `ConnectionId`**. | Nothing in the audit requires independent identity, lifecycle, ownership, configuration, authorization, references or history (§9). |
| 2 | **No `/services` route and no `/services` list.** `/connections/{id}` *is* the workspace; `/connections` *is* the list. | `InventoryQueryService.MapConnection` emits `/connections/{id}`; HSK-D and Config Inventory both depend on it; `ManagementBreadcrumbResolver` roots the trail at `Connections`; `ResourceContextViewRoutes.ScopedListPaths` contains `/connections` (§15, §16, §25). |
| 3 | **Cardinality is 1:1 by construction**, because the Service *is* the Connection's own page. Tools are tools, not Services. | No evidence anywhere for multiple Services per Connection (§8). |
| 4 | **Context derives from `Connection.ResourceContextId` at point of use.** No `Service.ResourceContextId`. | There is no Service row to carry one; moving a Connection moves its page because it is the same page (§10). |
| 5 | **No `service.read` exists in the derived Connection-workspace architecture.** The base workspace ships on existing `connection.*` permissions; remote structure reads add exactly one action, `connection.browse`. | Aggregation grants nothing; the Components gate declined `component.read` for the identical reason (§11). |
| 6 | **Executable capability already exists in the Management Host** — all three connectors and the full resolution stack are registered there today. The gap is **not plumbing**; it is safety, authorization and UI. | `Apperture.Management.Host/Program.cs:62–65` (§3). |
| 7 | **No new Client/Browser domain concept.** ADR-0026's `ClientDefinition` stays dead. | Zero references confirmed again at this commit (§13). |
| 8 | **No generic browse/query abstraction.** Typed, per-connector contracts only. | The three connectors do not share a browse shape; one of them has none at all (§19). |
| 9 | **No capability advertisement to Components in v1.** | Recommending "none" costs nothing and keeps the Component catalog valid when no tool exists (§14). |
| 10 | **The `/connections` Connectors tab retires during Services implementation**, not during Components. | Components D2 deliberately delegated the fate here; the catalog gains a real navigable home, and `/connections` should then mean configured Connections only (§17). |

**The one genuinely new seam**, and the only place a "provider" belongs: a **connector-supplied, typed, optional
tool surface**, keyed on `ConnectorDefinitionId`, following the ADR-0035 precedent of registering from the connector
package's own `AddApperture…Connector` extension. For v1's single provider it is **one contract plus the smallest DI
resolution** — no registry class, no contributor interface, no startup sanity hosted service (§12, C1).

**Scope, as approved.** The motivating example — "a SQL Server Connection that could expose a browser/query
experience" — is **two different features** with very different risk, and only the first is approved:

- **Structure browsing** (schemas, tables, views, columns) — **APPROVED for v1**. Buildable today, safely, from a
  closed set of platform-authored parameterized metadata queries. Needs one new action, `connection.browse`.
- **Arbitrary SQL execution** — **NOT approved, and not a later tab in the same plan.**
  `PlatformSqlConnectorClientFactory` hands back a raw `SqlConnection`; the platform has no read-only enforcement
  and cannot acquire one (§4.3). It is materially more privileged than any permission the platform issues (§4.5)
  and sits against ADR-0027 §5. **It is a separate future capability requiring its own security/design gate**
  (§20.2, §27.2).

Also outside v1: **row/content preview**, DML, DDL, and any remote mutation (§27.1).

**"Service" does not enter operator-facing copy in v1** (D-S4). It remains the architectural/roadmap term. No
relabelling of Connections as Services, no `/services` route, no "Service detail".

---

## 2. Current Connection architecture (Task 0)

### 2.1 Domain — code fact

`Connection` (`src/Apperture.Core/Apperture.Core.Domain/Connector/Connection.cs`):

```
Connection(ConnectionId Id, ConnectorDefinitionId ConnectorDefinitionId, string Name,
           IReadOnlyDictionary<string,string> Config, IReadOnlyDictionary<string,string> SecretRefs,
           string? ScopeKey, string? OwnerId,
           ConnectionState State = Active,
           ConnectionVerificationStatus LastVerificationStatus = NeverTested,
           DateTimeOffset? LastVerificationAtUtc = null, string? LastVerificationSummary = null)
  : IConnectionResource
{ ResourceContextId ResourceContextId { get; init; } = ResourceContextIds.Shared; }
```

- **`ScopeKey` is authorization scope and is explicitly unrelated to `ResourceContextId`** — the record's own doc
  comment says so ("Do not conflate the two"). Confirmed again here.
- `GetIdentity()` returns `GovernedResourceIdentity(ResourceTypeConstants.Connection, Id.Value, ScopeKey ?? "instance", OwnerId)`.
- **`ConnectionState` has exactly two members: `Active`, `Retired`.** No `Inactive`, no `Disabled`, no `Draft`.
  Retired connections are excluded from binding eligibility.
- `IConnectionResource : IGovernedResource` is a one-line marker interface. **It is not a precedent for
  `IComponentResource`** and does not weaken Components §19: it carries no catalog semantics, no actions and no
  lifecycle — it exists solely so authorization can read an identity.
- Id is `Guid.NewGuid().ToString("N")[..24]` — **opaque, not human-readable**, unlike every v1 Component's native id.

### 2.2 Persistence — code fact

`SqlConnectionRepository` / `ConnectionEntity`. `GetVisibleAsync(scopeKey, resourceContextId?)` pushes the **Context
filter into SQL** and then applies scope containment **in memory** (`IsScopeContained`). `IConnectionRepository` also
exposes `GetByIdForUpdateAsync` (row lock for ownership writes) and `SetResourceContextIdAsync` (single-column write;
`UpdateAsync` deliberately never touches that column).

**There is no `GetByConnectorDefinitionIdAsync`.** The Components gate's CMP-4 correctly identifies it as new work.

### 2.3 Config, secrets and verification — code fact

- `Config` holds non-secret values verbatim. `SecretRefs` maps a field key to a **secret id (GUID)**, validated on
  write (`ValidateIncomingSecretRef`). Secret *values* never live on the Connection.
- Material is merged only inside the resolution pipeline (`ResolvedConnectionMaterialBuilder`), producing
  `ResolvedConnectionMaterial`, whose `ToString()` is redacted by design.
- Verification: one `IConnectionVerifier` per connector, selected by `ConnectionVerificationService`, which refuses
  when the definition is missing or lacks `SupportsConnectionVerification`, and reports `Unavailable` when the
  capability is claimed but no verifier is registered. All three connectors ship a verifier.

### 2.4 Lifecycle and ownership — code fact

Create → Active. Update (name / config / secret rotation). Retire (→ `Retired`). Move between ResourceContexts via
`IResourceOwnershipMoveService` under `IResourceOwnershipMutationGate`, with preview plus re-validation inside the
gated transaction. **There is no un-retire, no delete, and no Connection archive.**

### 2.5 APIs — code fact

`ConnectionsController` (`/api/connections`): `GET` (list, `?contextId=`), `GET {id}`, `POST`, `PATCH {id}`,
`DELETE {id}` (retire), `POST {id}/test-connect`, `GET {id}/ownership/move-preview`, `POST {id}/ownership/move`.

`ConnectionDto` carries `Config` (raw values), `SecretRefs` (keys → GUIDs), `State`, `SupportsVerification` (computed
from the in-memory registry), the three verification fields, `UsedIn`, `ResourceContextId` and a `ResourceContextRef`.

**Pre-existing N+1:** `List` calls `_bindingRepository.GetByConnectionIdAsync(...)` **once per connection** to build
`UsedIn`. The Components gate already flagged this and forbade reusing the endpoint for usage counts. Confirmed here.

### 2.6 Management UI — code fact

- `/connections` (`ConnectionsPage.razor`, 290 lines) — two tabs, Connections and Connectors, with `?tab=` and
  `?context=`. Its subtitle already tells the truth: *"browse connector definitions (connection shape only — not
  executable client operations)"*.
- `/connections/{id}` (`ConnectionDetail.razor`, 433 lines) — a **Details** block (Name, Connector definition id,
  State, Scope, Owner, Verification support, Last verification status/time, verification summary), a raw
  `Key = Value` **Config dump**, secret **field names only**, a **Used in** block, and a toolbar of Back · Refresh ·
  Test connection · Edit · Move to another context… · Retire. Plus `OwnershipChip`, `MismatchNotice`,
  `MoveOwnershipDialog` and `ConfirmActionDialog`.
- `/connections/new` (`ConnectionNew.razor`, 268 lines) — supports `?context=`. **It does not support
  `?connectorDefinitionId=`** (§31).

### 2.7 Navigation — code fact, and a real finding

**`/connections` appears nowhere in `ControlPanelMenuDefinitions`.** Neither does `/schedules`.
`RouteToSectionResolver` maps `/connections` to the **Config** rail section, but Config's menu has no Connections
leaf. `DomainMenuCapabilityTokens.ViewConnections` exists and `MenusController.HasCapability` resolves it — but it is
**attached to no menu item**.

This is deliberate, not a bug. ADR-0040 rule 3: *"Connections and schedules keep their own models and URLs; they are
discovered from Inventory and Integration links, **not promoted to rail domains**."* The NAV-1 review records the
orphaned tokens explicitly. `ManagementBreadcrumbResolver` correspondingly roots the trail at a first-class
`Connections` crumb, with a comment refusing Config/Inventory ancestry.

**Consequence for this gate:** operators reach a Connection from Config Inventory, an Application's Resources list,
an Integration's connections page, or a bookmark. Any "Service" entry point inherits exactly that discoverability,
and **no amount of renaming fixes it** — that belongs to the later navigation gate (§26).

### 2.8 Authorization — code fact

| Operation | Gate | Where |
|---|---|---|
| List | `connection.read` on `Connection` @ **instance** | `ConnectionManagementService.ListConnectionsAsync` |
| Get / detail | `connection.read` on the **resource identity** | `GetConnectionAsync` |
| Create | `connection.create` @ requested scope | `CreateConnectionAsync` |
| Update | `connection.update` on the identity | `UpdateConnectionAsync` |
| Retire | `connection.delete` on the identity | `RetireConnectionAsync` |
| **Test-connect** | **`connection.read`** on the identity | `VerifyConnectionAsync` |
| Move context | ownership move service + mutation gate | `ResourceOwnershipMoveService` |

Two findings that matter a great deal to §11:

1. **`ResourceContextId` is not an authorization boundary.** Listing evaluates `connection.read` once at instance
   scope and then filters by `ScopeKey` containment; `ScopeKey` is `null → "instance"` for every Connection created
   through the UI. The `?contextId=` filter is a **view filter, not a permission**. So "hidden Connection ⇒ hidden
   Service" reduces, today, to "holds `connection.read`, or does not".
2. **Test-connect is already gated at `connection.read`**, with an explicit code comment: *"Diagnostic test-connect
   is available to anyone who can read the connection; it only updates verification metadata."* That is a real
   precedent — **`connection.read` already authorizes an outbound network connection to an operator-configured
   remote host using stored secrets** — and it is bounded precisely because the probe is fixed, takes no operator
   input, and returns no remote data.

`connector.manage` exists in `ActionConstants` and is seeded by `PlatformPermissionSeeder`, but is **evaluated
nowhere**. It is reserved, per `connector-resource-family.md`. Do not repurpose it for Services.

### 2.9 Audit — code fact, and a gap

**`ConnectionManagementService` contains no audit call of any kind** (zero matches for `IAuditService` / `_audit`).
Connection **create, update, secret rotation, retire and test-connect are entirely unaudited**. By contrast
`IntegrationManagementService`, `PlatformSettingsService`, `NotificationDefinitionService`,
`ResourceContextManagementService` and `ResourceOwnershipMoveService` all append `AuditRecord`s — including
connection **ownership moves**, which *are* audited.

So today a Connection's ownership move is audited but its secret rotation is not. A genuine pre-existing gap (§21).

### 2.10 Task 0 — direct answers

1. **What is a Connection today?** A persisted, ResourceContext-owned, instance-scoped governed resource holding the
   *instance configuration* (config values plus secret references) for one connector kind, plus a point-in-time
   verification result. Explicitly **not** the live client.
2. **Which operator workflows are already "Service-like"?** Four, all on `/connections/{id}`: **test-connect** (a
   real outbound operation against the external system), **verification status/summary** (health), **settings
   authoring against connector-declared fields**, and **"Used in"** (operational relationship). That is already four
   of the seven capabilities the Service hypothesis lists.
3. **What is missing that motivates Service?** Three things, and only one is new in kind:
   (a) *tools that touch the remote system beyond a fixed probe* — genuinely absent;
   (b) *recent activity* — absent, and **unbuildable today because §2.9 records nothing**;
   (c) *presentation polish* — config rendered as raw keys rather than connector-declared display names, the
   connector id rendered as dead text rather than a link, and no `TechnicalDetails` usage despite UIX-6.
4. **What must stay specialized?** Secret provisioning and rotation; the ownership move flow (gate, preview,
   re-validation); binding / `UsedIn` projection; retire semantics; connector-field validation. None of these belongs
   to a "Service" concept — all are Connection domain behaviour with real invariants.

---

## 3. Current executable Connector architecture (Task 1)

### 3.1 The two registries — code fact

| Concern | Contract | Implementation | Populated by |
|---|---|---|---|
| **Metadata** | `IConnectorDefinitionRegistry` | `StubConnectorDefinitionRegistry` (control plane, singleton, `ServiceCollectionExtensions.Governance.cs:124`); `InMemoryConnectorDefinitionRegistry` (worker) | `SeedWellKnownConnectorDefinitionsHostedService`, then `ConnectorDefinitionContributorsHostedService` |
| **Executable** | `IConnectorClientFactoryRegistry` | `ConnectorClientFactoryRegistry` — `ConcurrentDictionary<(string Id, Type Type), object>` | `ConnectorClientFactoryRegistrationHostedService`, applying every `IConnectorClientFactoryContributor` |

Resolution: `DefaultConnectionResolver.ResolveAsync<T>(Connection)` → definition lookup → `GetFactory<T>(id)` →
`ResolvedConnectionMaterialBuilder.BuildAsync` (resolves every `SecretRef` through `ISecretResolver`) →
`factory.Create(connection, material)`. `SlotBoundResolver` wraps that for
`IRuntimeSlotResolver.ResolveSlotAsync<T>(moduleId, slotName)`.

### 3.2 The decisive availability finding — code fact

**Both hosts register the complete executable stack, including all three connectors.**

- `src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs:62–65` —
  `AddAppertureCampusNexusConnector()` · `AddAppertureSftpConnector()` · `AddPlatformSqlConnector()` ·
  `AddConnectorResolution()`
- `src/Apperture.RuntimeHost/.../WorkerHost/Program.cs:91–92, 116–118` — the same set.

So `IConnectionResolver`, `IConnectorClientFactoryRegistry` and all three factories are **resolvable from the
Management Host DI container right now**. `IConnectionResolver` is registered `Scoped` there and is currently
**reached by nothing except `SlotBoundResolver`** — available and unused.

**Therefore: exposing connector capability to operators requires no new runtime plumbing, no new host, and no
cross-process hop.** This is the single most consequential fact in the audit, and it cuts both ways — the cost of
building a tool is low, which is exactly why the gating question is safety and authorization, not feasibility.

### 3.3 The doctrine that constrains it — code fact

[`runtime-connection-resolution.md`](../../runtime-connection-resolution.md) states:

- *"Runtime dependency resolution is **slot-bound only** … There is no connection-by-id or arbitrary global resource browsing."*
- *"**Binding is the gate**."*
- **Forbidden:** *"Resolving by connection id from module code; browsing arbitrary global connections; bypassing the binding gate."*
- but **Who may request resolution** includes *"Control-plane initiated execution paths where the host has registered the resolution stack."*

Read precisely: the prohibition is scoped to **module code** and the **runtime** path. A management-plane,
operator-initiated, per-Connection operation is not what that rule forbids — and `VerifyConnectionAsync` is the
existing proof, since it already creates live clients by Connection id with no binding.

**But the doctrine still bites in one place.** A Service tool is a **second non-slot-bound resolution path**, and
unlike verification it would be **operator-parameterized** and would **return remote data**. That difference — not
the resolution mechanism — is the whole security argument in §11 and §20.

ADR-0027 §5 adds the honesty constraint: *"If an operator can trigger behavior through the platform, it is via
module-contributed workflows, operations, or other explicit control surfaces — not via a generic connector
'operation' catalog."* A **typed, purpose-built, connector-specific** tool is an explicit control surface and is
compatible. A **generic** "run this against that connection" surface is exactly what ADR-0027 removed.

### 3.4 Per-connector determination

| | **Platform SQL** | **SFTP** | **Campus Nexus** |
|---|---|---|---|
| Client type | `IDbConnection` (raw `SqlConnection`) | `ISftpFileClient` | `ICampusNexusApiClient` |
| Assembly | `Apperture.ConnectorResolution` | `Apperture.Connectors.Sftp` | `Apperture.Connectors.CampusNexus` |
| Registered in Management Host | **Yes** | **Yes** | **Yes** |
| Mgmt host can construct it | **Yes** | **Yes** | **Yes** |
| Already supports browse | **No** — raw connection only | **Partly** — `ListDirectory` | **No** |
| Already supports read | Yes, by executing arbitrary SQL | Yes — `DownloadFile` | Yes — `QueryAsync<T>`, needs a caller-supplied CLR type |
| Already supports mutation | **Yes, unrestricted** | **Yes** — `RenameFile` | **Yes** — `ExecuteCommandAsync` |
| New runtime plumbing needed | None | None | None |
| **Safe for interactive use as-is** | **No** (§4) | **No** (§5) | **N/A** (§6) |

**Lifetime and disposal — code fact.** Every factory returns a caller-owned instance; the doc is explicit that the
runtime *"does not take ownership or dispose on the caller's behalf."* `ISftpFileClient` and `ICampusNexusApiClient`
are `IDisposable`; `SftpConnectorClientFactory.Create` **connects eagerly inside `Create()`**, so merely constructing
one opens an SSH session. Any request-scoped tool must dispose deterministically per request; **no pooling, caching
or session reuse exists** (explicitly deferred in the same doc).

**Architectural boundary — code fact.** `tests/Apperture.ArchTests/ConnectorResolutionDependencies.cs` forbids
`Apperture.ConnectorResolution` from referencing `Apperture.Connectors.*`. Combined with ADR-0035's *"
`IConnectorDefinitionContributor` remains metadata-only and must not register executable services"*, this fixes where
any Service provider may live: **inside the connector package**, registered from its own `AddApperture…Connector`
extension. Not in a central Services assembly, and never as a field on `ConnectorDefinition`.

---

## 4. SQL capability audit (Task 2)

### 4.1 What exists — code fact

`PlatformSqlConnectorClientFactory : IConnectorClientFactory<IDbConnection>` builds a `SqlConnectionStringBuilder`
from `Server`, `Database`, optional `UserId`, `Password`, `TrustServerCertificate` (**defaults to `true`**) and
`Encrypt`, and returns **`new SqlConnection(...)`** — unopened, untyped beyond `IDbConnection`, unrestricted.

`PlatformSqlConnectionVerifier` opens it, runs `SELECT 1` with `CommandTimeout = 15`, and maps outcomes to
`ConnectionVerificationResult`.

### 4.2 The realistic inventory

| Capability | Available today? | Evidence |
|---|---|---|
| Connection verification | **Yes** | `PlatformSqlConnectionVerifier`, fixed `SELECT 1` |
| Raw client creation | **Yes** | factory returns `SqlConnection` |
| Schema metadata | **No** | **zero** matches for `INFORMATION_SCHEMA`, `sys.tables` or `GetSchema(` anywhere in `src/` |
| Database / table / view discovery | **No** | as above; `Database` is pinned by the Connection's own config |
| SQL execution | **Technically yes, structurally no** | `IDbConnection.CreateCommand()` is available to any holder; no platform surface exposes it |
| Result materialization | **No** | nothing maps a `DataReader` to a transportable shape |
| Cancellation | **Partial** | the verifier passes a token; the factory has no async surface |
| Timeout | **One, hard-coded** | `CommandTimeout = 15` in the verifier — **the only timeout in the entire connector stack** |
| Row limits | **No** | no concept exists |
| Transaction semantics | **No** | nothing opens a transaction |
| Secret handling | **Yes, sound** | `ResolvedConnectionMaterial`, redacted `ToString`, never returned to callers |
| **Read-only enforcement** | **None, and not obtainable** | see §4.3 |

### 4.3 Read-only enforcement — the load-bearing negative

**The platform cannot make a SQL connection read-only.** `ApplicationIntent=ReadOnly` only routes to an availability
replica and does not restrict DML. Isolation levels do not restrict DML. The connection's SQL login has exactly the
rights its administrator granted it, and the platform never sees or constrains them.

There are only two honest mechanisms, and only the first is the platform's to control:

1. **Constrain what the platform is willing to send** — a fixed, closed set of parameterized queries. Read-only by
   construction, because no operator-supplied SQL ever reaches the server.
2. **Tell the operator to configure a read-only login** — advisory only, unverifiable, and therefore not a control.

### 4.4 Separation into four buckets

| Bucket | Items |
|---|---|
| **Already available** | Test-connect; the raw client; secret resolution. |
| **Small reusable extension** | A closed set of parameterized metadata queries against `INFORMATION_SCHEMA` (schemas → tables/views → columns), each with an explicit `CommandTimeout`, an explicit row cap, and cancellation. Roughly 150 lines plus a DTO. |
| **Substantial new feature** | Row/content preview (`SELECT TOP n` over an identifier-validated table), result-set materialization, paging, type mapping, and the audit and data-exposure decisions that come with returning business data to a browser. |
| **Unsafe without a separate security design** | **Operator-supplied SQL of any kind.** Also: writes, DDL, stored-procedure invocation, multi-statement batches, and anything that would need `connection.update`-or-higher semantics against a remote system the platform does not govern. |

### 4.5 The privilege question, answered explicitly

**Is interactive SQL materially more privileged than `connection.read` or `connection.update`? Yes — and not by a
small margin.**

- `connection.read` today reveals: the connector kind, `Server`, `Database`, `UserId`, which fields are secret-backed,
  and whether a fixed probe succeeded. It reveals **no remote content**.
- `connection.update` lets an operator change platform-side configuration and rotate secrets. It does **not** let
  them read remote data.
- Arbitrary SQL grants **everything the remote login can do**, including reads of every table, and writes if the
  login permits them. It is not a superset of `connection.update`; it is an orthogonal and much larger power.

Even **metadata-only** browsing exceeds `connection.read`, because it discloses the remote system's structure.
Row preview exceeds it much further, because it discloses remote business data.

**Hypothesis rejected:** that a SQL browser can ride on `connection.read`. It cannot, and §11 prices it accordingly.

---

## 5. SFTP capability audit (Task 3)

### 5.1 What exists — code fact

```csharp
public interface ISftpFileClient : IDisposable
{
    bool IsConnected { get; }
    IEnumerable<ISftpFile> ListDirectory(string path);
    void DownloadFile(string remotePath, Stream output);
    void RenameFile(string oldPath, string newPath);
}
```

`SshNetSftpFileClient` is a thin pass-through to `Renci.SshNet.SftpClient`.
`SftpConnectorClientFactory.Create` builds a `ConnectionInfo` from `Host`, `Port` (default 22), `Username` and either
`PrivateKeyPem` (+ optional passphrase) or `Password`, then **calls `client.Connect()` eagerly inside `Create()`**.

### 5.2 Determination per candidate experience

| Experience | Supported today | Read or mutation | Assessment |
|---|---|---|---|
| **Browse directories** | **Yes** — `ListDirectory` | Read | The only connector with a genuine, ready browse primitive. |
| **Inspect file metadata** | **Yes** — `ISftpFile` carries name, size, timestamps, attributes | Read | Comes free with listing; needs a DTO at the boundary (§5.3). |
| **Download** | **Yes** — `DownloadFile(path, Stream)` | Read, **content-bearing** | Exfiltration surface. Streams remote bytes to the operator. |
| **Upload** | **No** — not on the interface | Mutation | Would need a new client method. |
| **Rename / move** | **Yes** — `RenameFile` | **Mutation** | Present and usable today. |
| **Delete** | **No** — not on the interface | Mutation | Would need a new client method. |
| **Create directory** | **No** | Mutation | Would need a new client method. |
| **Verification** | **Yes** — `SftpConnectionVerifier` | Read | Connects and reports `IsConnected`. |

### 5.3 Blockers for interactive use — code fact

1. **The whole interface is synchronous and takes no `CancellationToken`.** `ListDirectory`, `DownloadFile` and
   `RenameFile` all block. Calling them on a request thread blocks it for the duration of a network round trip with
   no way to abort.
2. **No timeout anywhere.** `SftpConnectorClientFactory` sets none, and `SftpClient.Connect()` blocks on its default.
3. **`ISftpFile` is a third-party type** (`Renci.SshNet.Sftp`). It must never appear in a management API contract;
   any tool has to map it to a platform DTO — which also means `Apperture.Connectors.Sftp` cannot be referenced by a
   central Services assembly (§3.4), so an SFTP tool provider must live in the SFTP package.
4. **No path confinement exists.** The connector declares no root/base-path field, so "browse" means the entire
   filesystem the SSH account can reach. The remote account's own permissions are the only boundary.
5. **Eager connect** means constructing the client *is* the network operation, so a failed browse and a failed
   connect are the same failure and must be distinguished in copy.

### 5.4 Does the abstraction generalize beyond SQL?

**Partly, and instructively.** SFTP and SQL both admit a "navigate a hierarchy, then look at a tabular thing at the
leaf" framing. But the hierarchies are not the same kind of object: SQL's is **fixed-depth and heterogeneously
typed** (schema → table|view → column), SFTP's is **arbitrary-depth and homogeneous** (directory → entries). The leaf
payloads differ more: SQL columns are a typed schema; SFTP entries are file metadata.

So SFTP confirms that **more than one connector can have a browse-shaped tool**, and simultaneously shows that
**their contracts should not be unified** (§19).

---

## 6. Campus Nexus capability audit (Task 4)

### 6.1 What exists — code fact

```csharp
public interface ICampusNexusApiClient : IDisposable
{
    Task<ApiCommandResponse> ExecuteCommandAsync(ApiCommand apiCommand, CancellationToken ct = default);
    Task<T?> ExecuteCommandAsync<T>(ApiCommand apiCommand, CancellationToken ct = default);
    Task<IReadOnlyList<T>> QueryAsync<T>(ApiQuery apiQuery, CancellationToken ct = default);
    Task<T?> QueryFirstAsync<T>(ApiQuery apiQuery, CancellationToken ct = default);
    string ColumnInFilter(string columnName, IEnumerable<string> values);
    string ColumnInFilter(string columnName, string commaSeparatedValues);
}
```

`ApiQuery(string EntityCollectionName, string ODataQuery = "$top=10", string? SelectField = null)`.
`ApiCommand` posts to `api/commands/{Module}/{EntityName}/{Operation}` with a JSON payload.
`CampusNexusCampusQueries` is the only concrete usage: a fixed `Campuses` OData query.

### 6.2 Determination

| Question | Answer |
|---|---|
| Discoverable collections / endpoints? | **No.** `EntityCollectionName` is a caller-supplied string. No `$metadata` call, no service-document read, no enumeration of any kind. |
| Query / read operations? | **Yes, but not browsable.** `QueryAsync<T>` requires the caller to supply a **CLR type `T`** at compile time for deserialization. There is no untyped/dynamic result path. |
| Fixed domain operations? | **Yes.** `CampusNexusCampusQueries.ListActiveCampusesAsync` and the verifier's single-row campus probe. Modules add their own typed queries. |
| Generic browse experience? | **No, and not buildable without a new client.** |

### 6.3 Why this is the decisive counterexample

The generic-browser framing collapses here on two independent grounds:

1. **Nothing to enumerate.** A browser needs a starting list. Campus Nexus offers none; the operator would have to
   already know the collection name, which defeats the purpose of browsing.
2. **The result contract is compile-time typed.** Even given a collection name, `QueryAsync<T>` cannot return
   anything without a `T`. A generic browser would have to add an untyped path to the client — i.e. build a new
   capability, not surface an existing one.

**Campus Nexus therefore earns its role: it proves that "Service" must not mean "file/database browser".** A model
in which every Connection gets browse/query would be a model that is simply false for one of the three connectors
that exist. Whatever ships must be correct when a connector supplies **no tools at all** — which is the normal case
(§22).

---

## 7. Service definition (selected architecture)

> A **Service** is the operator-facing workspace for **one configured Connection**: what it is, how it is
> configured, whether it is healthy, where it is used, and — **only when its connector supplies them** — a small set
> of typed, read-only tools for inspecting the external system it points at.

**"Service" is the architectural and roadmap term for this capability body. It is not an operator-facing noun**
(D-S4). Operators see Connections, Connection detail, and connector-specific tools.

Boundaries, each grounded:

- **A Service is not a thing; it is a page.** It has no record, no id of its own, no lifecycle and no ownership. It
  is the rendering of a Connection plus whatever its connector offers (§9).
- **A Service is not an execution dependency.** Integrations bind to Connections. Nothing routes through a Service
  (§24).
- **A Service is not a Component.** Components are catalog metadata; a Service is operation over one configured
  instance. The link between them is a pure construction (§23).
- **A Service is not guaranteed to have tools.** Zero of three connectors expose an operator tool today. The base
  workspace must be complete and honest with no tools at all.
- **"Service" is a product word, not a code word.** There are **245 distinct `*Service` type names** in `src/`, where the suffix
  universally means "application service". A domain type named `Service`, `ServiceDefinition` or `IServiceProvider`
  would be as damaging here as a bare `Component` would have been next to Blazor's `ComponentBase` — and worse,
  because `IServiceProvider` is a BCL type. **No new type may be named `Service*` or `*Service` for this concept.**
  The approved architecture avoids the problem entirely by creating no such type.
- **No relabelling** (D-S4): Connections are not renamed Services, `/connections` is not renamed `/services`, and
  Connection detail is not renamed Service detail. There is no `/services/{id}` route.

---

## 8. Cardinality (Task 5)

**Selected: exactly one per Connection — trivially, because the Service *is* the Connection's page.**

Because the recommendation collapses Service onto `/connections/{id}`, cardinality is not a modelling choice at all;
it is a consequence. There is one page per Connection because there is one Connection per id.

Evaluated and rejected:

| Alternative | Verdict |
|---|---|
| Exactly one derived Service per Connection | **Selected**, as a consequence of §15. |
| Zero-or-one (only when a provider exists) | **Rejected.** It would make the *page* conditional, orphaning `/connections/{id}` for connectors with no tools — i.e. all of them today. The *tools section* is zero-or-one; the workspace always exists. |
| Multiple Services per Connection | **Rejected. No evidence of any kind.** |

**"Could one SQL Connection expose separate Database Browser, Query and Admin Services?"** No. Those are three tools
over one configured endpoint. They would share identity (`ConnectionId`), lifecycle (the Connection's),
configuration (the Connection's config and secrets) and context (the Connection's). What differs is only
*authorization* — and authorization differentiates **capabilities**, which the platform already models as actions on
a resource, not as separate resources. Splitting them into Services would create three identities that must be kept
in sync with one row, for no gain.

Nothing in the current code forces multiple. The strong prior survives the audit **and is strengthened** by the fact
that the Service turns out not to be a separate object at all.

---

## 9. Persistence / identity (Task 6)

**Selected: Option A — nothing persisted. Identity is `ConnectionId`.**

Option B was tested against its own criteria and failed every one:

| Option B would require | Audit finding |
|---|---|
| Independent **identity** | `ConnectionId` is already unique, already routed, already referenced by `InventoryQueryService`, HSK-D, breadcrumbs and bindings. A second id would need reconciliation and would break those links. |
| Independent **lifecycle** | `ConnectionState` is `Active`/`Retired` and is the only lifecycle in evidence. A Service has no state a Connection does not already have (§22). |
| Independent **ownership** | `Connection.ResourceContextId` is the only ownership, enforced through a mutation gate. A second owner column could drift (§10). |
| Independent **configuration** | Tool behaviour derives from the connector; connection material lives on the Connection. Nothing is left over. |
| Independent **authorization** | New permissions attach to `Connection` as the resource type (§11). No new resource type is needed to carry them. |
| Independent **references** | Nothing refers to a Service. Bindings refer to Connections (§24). |
| Independent **history** | None exists — Connections are not even audited today (§2.9, §21). |

Option C (hybrid) is strictly worse for the same reasons the Components gate gave: it buys the reconciliation problem
without removing the need to read the authoritative source on every request.

**No Service GUID. No Service slug. No `(ConnectionId, tool)` composite identity** — tools are addressed within the
page, not as identities, because §8 establishes there is nothing to address.

**Reversibility.** Should a future requirement genuinely demand a persisted Service, `ConnectionId` becomes its
primary key and nothing else changes. Removing a premature table is the expensive direction.

---

## 10. ResourceContext (Task 10 / Task 7)

**Selected: derived from `Connection.ResourceContextId` at point of use. No `Service.ResourceContextId`. Binding
constraint from the Components gate honoured without exception.**

Audited for insufficiency; none found:

- `ResourceContextId` is a first-class `init` property with a `Shared` default, written only through
  `SetResourceContextIdAsync` inside a gated transaction — a single, well-guarded source of truth.
- Move behaviour is already correct by construction: because the workspace **is** the Connection's page, a move
  changes the page's context with no synchronisation step, no cache and no second write. `OwnershipChip` and
  `MismatchNotice` on `ConnectionDetail.razor` already render from the freshly-loaded Connection after a move.
- **Important correction to a common assumption:** `ResourceContextId` is **not** an authorization boundary
  (§2.8). It determines display, ownership and archive-blocking — not visibility. Any Services test asserting
  "Context change alters who can see it" would be asserting something the platform does not do.

**One stale living-doc annotation applied (C4).** The CTX design gate's resource matrix carried the row
`| **Future Services** | Direct | SVC | Nothing in ResourceContext changes to accommodate them |`, which classified
future Services as a **directly Context-owned resource**. A minimal supersession note was added to that document's
existing supersession block — [`ctx-design-gate.md`](../resource-contexts/ctx-design-gate.md), item 4 — recording
that Service is a derived Connection workspace, not an independently owned Resource, and that the backing
**Connection** remains the direct ResourceContext-owned resource. The row's own rationale ("Nothing in
ResourceContext changes to accommodate them") holds, and holds more strongly than written.

A full sweep of `docs/01-architecture/`, `docs/02-decisions/` and `docs/00-overview/` found **no other** wording
classifying Services as Context-owned. No historical implementation plan or closure record was rewritten, and CTX
architecture is not otherwise reopened.

**Future cross-context grouping compatibility.** All nine constraints in the brief are satisfied, and satisfied
*vacuously* for most of them, which is the strongest form:

| Constraint | Status |
|---|---|
| 1. Ownership derives solely from the backing Connection | Yes — there is no other source. |
| 2. No domain/department/grouping field | Yes — no Service record exists to hold one. |
| 3. No cross-context Service ownership | Yes — same reason. |
| 4. Identity independent of shell View | Yes — `/connections/{id}` carries no context token. |
| 5. Routes not Context-specific | Yes — `?context=` exists only on the **list**, which is pre-existing behaviour. |
| 6. Stable APIs/links a future surface can aggregate | Yes — `/api/connections/{id}` and `/connections/{id}` are unchanged and already consumed by Inventory and HSK-D. |
| 7. Authorization from the Connection model; grouping never grants access | Yes — §11 adds no aggregate permission. |
| 8. No pre-built cross-context operations | Yes — none proposed. |
| 9. Call out anything that would prevent later aggregation | **One item, §26.** |

---

## 11. Authorization (Task 8)

### 11.1 Base workspace — no change

Everything on the workspace today ships on existing permissions: `connection.read` (view, test-connect),
`connection.create`, `connection.update` (edit, rotate secrets, move), `connection.delete` (retire). **No new
permission is required for the base Service experience.** This is the direct analogue of the Components gate's
"no new permission" ruling, and for the same reason: an aggregate permission would either be redundant or would
grant what the family gate refuses.

**Durable architectural rule (approved wording, D-S2):**

> No `service.read` exists in the derived Connection-workspace architecture. A Service-specific permission would
> require a future, independently justified Service authorization boundary.

The same holds for `service.manage`, `service.browse` and any generic tool permission: there is no Service resource
type to attach them to, and none is created. Authorization is expressed as **actions on `Connection`**.

### 11.2 Where the line actually falls

The brief asks for the distinctions to be drawn explicitly. They are:

| Operation | Discloses | Correct gate |
|---|---|---|
| Viewing Connection metadata (name, connector, host, database, field names) | Platform-side configuration | **`connection.read`** — unchanged, already correct |
| Health / verification (test-connect) | Reachability and auth success; **no remote content** | **`connection.read`** — unchanged; see the precedent in §2.8 |
| **Browsing remote structure** (schemas, tables, columns, directory names) | The external system's **shape** | **New permission required.** Not `connection.read`. |
| **Reading remote records or file content** | The external system's **data** | **Higher still.** Out of v1 scope. |
| Running operator-supplied queries | Everything the remote login can do | **Out of scope entirely** (§4.5, §20) |
| Downloading content | Data, plus an exfiltration channel | Out of v1 scope |
| Mutating the remote system (rename, delete, upload, commands) | Changes the external system | Out of v1 scope; would need its own design |
| Administrative commands | As above, amplified | Out of scope |

### 11.3 Approved permission model (D-S2)

**S-B is approved, so exactly one new action is added:**

```
connection.browse   on   ResourceTypeConstants.Connection   @ instance
```

- One action, not a family. It means *"inspect, read-only, the structure of the external system this Connection
  points at."*
- It is **additive to `connection.read`**, never an alternative: a caller must hold both, because the tool is
  rendered inside a page that already requires `connection.read`. Aggregation grants nothing.
- Registered through `PlatformPermissionSeeder` alongside the existing connector-family actions, with
  `ResourceFamily.Connector`.
- **Not granted by default to any role other than platform administrator.** It must be an explicit grant, because
  its whole purpose is to be a higher bar than `connection.read`.
- **Content/row preview is deliberately excluded from `connection.browse`.** Row/content preview is outside
  Services v1 entirely; if it is ever wanted it gets its own decision and, most likely, its own action — structure
  and data are different disclosures, and collapsing them would silently widen the first grant.

**Explicitly not added** (D-S2): `service.read`, `service.browse`, any generic tool permission, and any arbitrary
SQL execution permission.

**Rejected:** reusing `connection.update` for browsing. It is the wrong axis — `update` is a platform-side write
permission, and an operator authorized to rename a Connection is not thereby authorized to read a remote schema.

**Rejected:** reusing the reserved `connector.manage`. It is scoped to `ConnectorDefinition`, is evaluated nowhere,
and is documented as reserved for definition administration.

### 11.4 Non-leakage rules

- A caller without `connection.read` sees no workspace — the existing `ForbiddenState` path already handles this.
- A caller with `connection.read` but not `connection.browse` sees the workspace **with the tools section entirely
  absent** — not a disabled control, and not an empty state, because an empty state would assert "this connector has
  no tools", which the caller has not earned the right to know. This mirrors the Components gate's usage-block rule.
- Tool availability must never be reported through an endpoint that a caller lacking `connection.browse` can reach.

---

## 12. Provider / adaptor architecture (Task 9)

### 12.1 Is a provider abstraction needed at all?

**Not for the base workspace.** It renders from `ConnectionDto` plus the connector definition, both already
available.

**Yes, minimally, for the approved SQL exemplar** — because the alternative is a hard-coded
`if (connectorDefinitionId == PlatformSql)` in a Razor page, which is where the second connector's tool would then
also have to go.

### 12.2 Selected shape — minimal, and it is the whole mechanism (C1)

```csharp
// Apperture.Core.Application.Services — contracts only, no executable behaviour
public interface IConnectionToolProvider
{
    ConnectorDefinitionId ConnectorDefinitionId { get; }
    ConnectionToolAvailability Availability { get; }   // local, static; never a remote probe
}

// Typed capability, implemented only by connectors that genuinely have it:
public interface ISqlStructureBrowser : IConnectionToolProvider { /* typed methods */ }
```

- **Registered from the connector package's own `AddApperture…Connector` extension**, following the ADR-0035
  precedent (§2). Not from `ControlPlane.Security`, which must not reference connector assemblies; not from
  `IConnectorDefinitionContributor`, which ADR-0035 §1 keeps metadata-only.
- **Resolved through the smallest DI mechanism**: the registered `IEnumerable<IConnectionToolProvider>` set, matched
  on `ConnectorDefinitionId` — the same `NativeId` the Components gate routes on (§23 Q3 constraint honoured).
- **That is the entire seam.** One contract, one typed capability interface, one keyed match.

**For Services v1, with one SQL provider, the following are explicitly NOT built (C1):**

| Not built | Why |
|---|---|
| A provider **registry class** | One entry does not justify a registry; the DI set *is* the registry. |
| A provider **contributor interface** | ADR-0035's contributor exists because verifiers have multi-type registration needs. A single `AddSingleton<IConnectionToolProvider, …>()` has none. |
| A provider **startup sanity hosted service** | `ConnectionVerifierRegistrationSanityHostedService` guards a *metadata/implementation mismatch* (`SupportsConnectionVerification` claimed with no verifier). No equivalent mismatch exists here, because §14 adds **no capability advertisement** — there is no claim for a sanity check to falsify. |

**When a second real tool provider exists**, extract registration and validation infrastructure **from the two
concrete implementations**, rather than speculating now. That is the same discipline the Components gate applied to
its own section framework, and §19 applies to browse contracts.

### 12.3 What must not be built

- **No generic command/action object.** No `Execute(string command)`, no `Invoke(toolId, JsonDocument payload)`, no
  descriptor-driven dynamic form. The three connectors share no stable contract (§19), and ADR-0027 removed exactly
  this shape once already.
- **No plugin framework.** No manifest, no discovery, no ordering, no versioning, no lifecycle.
- **No executable surface on `ConnectorDefinition`.** Components §23 Q3 constraint; ADR-0035 §1.
- **No registry, contributor interface or sanity hosted service before there are two implementations.** v1 ships one
  tool, so a keyed match over `IEnumerable<IConnectionToolProvider>` is the whole mechanism — one interface and a
  `FirstOrDefault`. Building infrastructure for a single entry is precisely the speculative machinery the Components
  gate rejected for its own section framework. **This is the settled position; §12.2 states it identically (C1).**
- **Campus Nexus must remain a fully valid Connector and Connection while supplying no tool provider at all.** The
  no-provider path is the normal path, not a degraded one (§22).

---

## 13. Client / Browser ruling (Task 10)

**Selected: A — no. Clients remain runtime implementation details behind typed, connector-owned code.**

- **Code fact:** `ClientDefinition` has **zero references** in `src/` and `tests/`, re-confirmed at this commit.
- ADR-0027 supersedes ADR-0026 and states the platform must not expose *"generic client definitions, client
  operation metadata, or operator-facing surfaces that imply the platform can execute or configure arbitrary
  connector methods."*
- The product need is **an experience**, not an entity. §12's typed capability interfaces satisfy it without any
  operator-visible "Client" concept, without a registry of operations, and without a catalog the operator browses.

**Options B, C and D are rejected.** B (a non-persisted provider/API abstraction *named* Client) would reintroduce
the vocabulary ADR-0027 deliberately removed, for no benefit over §12's naming. C (an operator-visible
Client/Browser definition) is ADR-0026 restored and is explicitly forbidden by the approved rulings.

---

## 14. Component capability-advertisement ruling (Task 11)

**Selected: no advertisement in v1.**

Reasoning:

- If no tools ship (S-A), there is nothing truthful to advertise.
- If only the SQL browser ships (S-B), the advertisement would be a single boolean, true for exactly one of seven
  Components, on a page the operator has usually arrived at *from* a Connection anyway.
- Every mechanism has a cost and this one has no demonstrated demand. "No advertisement" also means **zero
  amendments** to the approved Components design (§31), which is the cheapest possible outcome for CMP-1.

**Recorded for when it is wanted** (do not build now): a **detail-time only** derived fact on
`ComponentDetailDto.Connector`, computed from the **local, static** provider registration — an in-memory lookup keyed
by `ConnectorDefinitionId`, no I/O, no client construction, no remote probe. This satisfies every constraint in the
brief: it is connector-family-specific rather than a cross-family `Capabilities[]`; it puts no executable behaviour on
`ConnectorDefinition`; it never touches the list path (so no per-Component lookup and no N+1); and a Component
remains fully valid when no provider exists.

**Rejected:** a connector-family flag on `ConnectorCapabilities`. The `[Flags]` enum is a *definition metadata*
concept describing connection shape; tool availability is a *host composition* fact that can differ between the
Management Host and a worker. Encoding it in the definition would make the definition lie in any host that did not
register the provider.

---

## 15. Service UI / route model (Task 12)

**Selected: A — `/connections/{id}` evolves into the workspace. No separate `/services/{id}` route.**

### 15.1 Why A

| Criterion | A (evolve `/connections/{id}`) | B (separate `/services/{id}`) | C (link out to tool pages) |
|---|---|---|---|
| Conceptual clarity | One page per Connection | **Two pages that both look like "the detail page for this Connection"** — the exact failure the brief warns against | Adds a third surface |
| Duplicated surfaces | None | Header, identity, ownership chip, state, verification, Used-in — all duplicated or arbitrarily split | Partial |
| Breadcrumbs | Works today; `Connections` is already a first-class root crumb | Needs a new trail; risks reasserting the Config/Inventory ancestry NAV-1 fixed | Extra depth |
| Links / bookmarks | Every existing link stays valid | **Breaks or forks** `InventoryQueryService`'s `/connections/{id}`, HSK-D rows, `IntegrationConnections.razor` drill-ins | Preserved |
| HSK-D Resource Explorer | One target, unchanged | Must choose one — and either choice makes the other page unreachable from ownership views | Unchanged |
| Integration hub | `IntegrationConnections.razor` already links to `/connections/{id}` | Must be repointed or left inconsistent | Unchanged |
| Movement / context UX | `OwnershipChip`, `MismatchNotice` and `MoveOwnershipDialog` already live there | Must be duplicated or split from the page that owns the action | Unchanged |
| Future grouping redesign | One stable identity to link into | Two identities for one object | Unchanged |

**Decisive:** `InventoryQueryService.MapConnection` emits `/connections/{id}` as the canonical Href, and both Config
Inventory and the HSK-D Application/Shared Resources explorer render it. A separate `/services/{id}` would either
orphan that link or require changing an ownership-model surface to point at a page that does not own the object.
Neither is acceptable for a presentation change.

### 15.2 Shape of the evolved page

A single page, sections in this order, each present only when it has something true to say:

1. **Header** — name, connector display name, state, `OwnershipChip`, `MismatchNotice`. Existing.
2. **Overview / health** — verification support, last status, last verified, summary. Existing; deserves promotion
   from a metadata row to a legible status treatment.
3. **Configuration** — connector-declared **display names** instead of raw config keys; secret fields shown as
   present/absent, never valued. Improvement over the current raw dump.
4. **Used in** — existing, unchanged.
5. **Tools** — **only when the connector supplies a provider and the caller is authorized** (§11.4). Absent
   otherwise, not empty.
6. **`TechnicalDetails`** (UIX-6) — connection id, connector definition id, scope key, owner. Currently the id is
   dumped in the header; UIX-6 has a component for exactly this.

No new dialog, drawer, panel or overlay — the anti-patterns playbook and `Uix6InteractionSystemTests` forbid it.

---

## 16. List / discovery model (Task 13)

**Selected: `/connections` already is the Service list. No `/services` list. No new list.**

- A derived `/services` list would be **row-for-row identical** to `/connections` — same rows, same names, same
  states, same links — because cardinality is 1:1 (§8). The brief asks for strong justification; there is none.
- `/connections` is already registered in `ResourceContextViewRoutes.ScopedListPaths`, already supports `?context=`,
  and already carries the Shared-connections notice. Recreating that on a second route is pure duplication.
- Service belongs in the Connection/Integration operational realm, not under Components/Modules — and `/connections`
  is exactly where that realm already lives.

**The real discovery problem is not a missing list; it is a missing entry point** (§2.7, §26). `/connections` has no
rail item. That is ADR-0040's deliberate choice and the later navigation gate's to revisit. Services must not
work around it by inventing a route with a rail item — that would promote Connections to a rail domain by the back
door, contradicting ADR-0040 rule 3.

---

## 17. `/connections` Connectors-tab ruling — FINAL (Task 14 — Components D2 delegated here)

**Approved: B, with explicit timing — retire the tab during Services implementation, not during Components.**
This closes the handoff Components D2 deliberately left open.

### Components implementation behaviour, now (unchanged from D2)

**Keep the tab. Link its rows to `/components/connector/{nativeId}`.** Exactly as CMP-5 already plans. Components
implementation **does not remove or redirect the tab**, is **not** blocked on this retirement, and must not wait
for it.

### Eventual cleanup, and why

Once `/components` ships, the Connectors tab becomes the **second** list of the same three rows — and the inferior
one, since the catalog has richer detail, a real navigable home under `Config → System → Components → Catalog`, and
a capability token wired into the menu. Meanwhile `/connections` is the one surface whose name promises configured
Connection Resources and which currently shows something else in a tab.

**Retire it in Services implementation**, replacing it with two cheaper links that serve the actual needs:

1. On `/connections`, a single inline link in the page intro: *"Browse the connector catalog"* → `/components?kind=connector`.
2. On each Connection workspace, the connector name as a link → `/components/connector/{connectorDefinitionId}` (§23).

**Why not A (keep permanently):** two lists of three rows, diverging over time, with the richer one hidden behind a
menu the operator may never associate with connections.

**Why not C (repurpose the area for Service/Connection tools):** tools belong on the Connection they act on, not on a
list page. A tab of tools with no selected Connection has no meaning.

### Resulting responsibility split (approved)

- **Components** = discover and manage **Connector definitions**.
- **Connections** = configured **operational resources and their workspaces**.

### Plan ownership — recorded exactly

**The Connectors-tab retirement is owned by SVC-4** (§30), which also adds the two replacement links. It is gated
only on Components CMP-2/CMP-3 having shipped `/components`; it carries no dependency on SVC-6/SVC-7 and can ship
independently of the SQL exemplar.

---

## 18. Base Service experience (Task 15)

Audited against `ConnectionDetail.razor` as it stands. **The honest finding: the base experience is ~80% built, and
what is missing is mostly polish plus one genuine gap.**

| Candidate element | Present today? | Recommendation |
|---|---|---|
| Name / status | **Yes** | Keep. |
| Connector Component link | **No** — rendered as dead monospace text | **Add** (§23). Cheap and high value. |
| ResourceContext / Application | **Yes** — `OwnershipChip` + `MismatchNotice` | Keep unchanged. |
| Connection fields / settings | **Partly** — raw `Key = Value` dump in a code block | **Improve**: render connector-declared `DisplayName` and `Description`. The metadata is already on `ConnectorDefinitionCatalogDto` and already fetched for the edit form. |
| Secret-backed configuration presentation | **Partly** — key names only | **Improve**: present/absent per field. **Never** a value, never a ref GUID in operator copy. |
| Verification / health | **Yes** — support, status, time, summary, `Test connection` | Keep; promote from a metadata row to a legible status block. |
| Used In | **Yes** | Keep unchanged. |
| **Recent activity** | **No — and not a Services v1 deliverable** | **Deferred (§21.3).** SVC-2 closes the *write* gap, but the audit query model has no Resource column and no resource filter, so Connection-scoped retrieval is not efficiently supportable today. Requires separate future work. Do not invent an activity feed from `LastVerificationAtUtc` alone. |
| Diagnostics | **Partly** — verification summary only | Sufficient for v1. |
| Technical details | **No** — raw id in the header | **Add `TechnicalDetails`** (UIX-6 compliance). |
| Retirement / lifecycle | **Yes** — Retire + `ConfirmActionDialog`; Move dialog | Keep unchanged. |

### 18.1 The conclusion this task was meant to force

**This table is the evidence for §1 ruling 1.** A "base Service experience" designed from scratch would reproduce
`ConnectionDetail.razor` almost exactly. That is not a sign that Service is a distinct surface — it is a sign that
**the Connection detail page has been the Service workspace all along**, and the product question is whether to
improve it, not whether to build a second one beside it.

Accordingly: **do not build a second page.** Improve this one. Duplicating Connection management into a "Service"
page would create precisely the two-detail-pages problem the brief warns about, and would fork the ownership,
verification and move affordances away from the page that owns them.

---

## 19. Rich browse/query capability model (Task 16)

**Selected: no common abstraction. Typed, per-connector contracts only.**

The brief's test is *"at least two concrete connector families with a genuinely shared browse shape."* Applying it
honestly:

| | SQL | SFTP | Campus Nexus |
|---|---|---|---|
| Hierarchical browse | Fixed-depth, heterogeneously typed (schema → table\|view → column) | Arbitrary-depth, homogeneous (directory → entries) | **None** |
| Tabular query | Would require new code and operator SQL | N/A | Typed-only, needs a compile-time `T` |
| Metadata discovery | Possible via `INFORMATION_SCHEMA` (new) | Comes free with listing | **None** — no `$metadata`, no enumeration |
| Resource inspection | Column types, nullability, keys | File size, timestamps, attributes | N/A |
| Command / action | Arbitrary SQL (out of scope) | Rename (mutation) | `ExecuteCommandAsync` (module-owned) |

**Two families share a superficial silhouette and nothing else.** Their node types differ, their depth semantics
differ, their leaf payloads differ, and the third family has no browse at all. The Components gate rejected a
section-framework for two families on exactly this reasoning; the same discipline applies here with more force,
because here a wrong abstraction leaks into a security boundary.

**Therefore:**

- Keep the concepts distinct in code and in copy: *hierarchical browse*, *tabular query*, *metadata discovery*,
  *resource inspection*, *command/action*. Do not let one type span them.
- **Never** flatten to `Execute(string command)` or a generic JSON payload API — ADR-0027 removed that model once.
- If a second browse-shaped tool ever ships, **re-evaluate then, with two real implementations in hand.** An
  abstraction extracted from two working implementations is cheap; one designed ahead of them is a liability.

The **only** thing shared across connectors is the *provider lookup* (§12) and the *UI slot* (§15.2 item 5) — not
the tool contract.

---

## 20. External-operation safety (Task 17)

Analysis for the approved v1 operation (**SQL structure browse**), then the deferred set.

### 20.1 SQL structure browse — binding safety contract (C2)

**The v1 contract contains no dynamic SQL of any kind.** Every statement is platform-authored and fixed at compile
time; every externally supplied selection is a **value bound through `SqlParameter`**. Because the browser reads
*metadata rows* from `INFORMATION_SCHEMA` (schema name, table name and column name are **column values there, not
identifiers in the statement**), no user-authored identifier ever needs to appear in command text.

| Dimension | Requirement |
|---|---|
| Read-only vs mutation | **Read-only by construction.** A closed, platform-authored set of parameterized metadata queries. No operator SQL reaches the server, ever. |
| **Statement construction** | **No dynamic SQL is built.** The statement set is closed and literal. No string concatenation, no interpolation, no `sp_executesql`, no identifier quoting — **`QUOTENAME` is not part of the v1 contract**, because no v1 operation embeds a user-authored identifier in SQL syntax. |
| **Parameterization** | Schema names, table names and similar selections are **values supplied through `SqlParameter`** and compared against `INFORMATION_SCHEMA` columns. No operator value is concatenated into command text. |
| Secrets exposure | Resolved material stays inside the pipeline; never returned, never logged, never in an error message. Existing `ResolvedConnectionMaterial` discipline is sufficient **if** exception messages are mapped rather than surfaced raw. |
| Query limits | Fixed `TOP n` on every metadata query. No unbounded result set. |
| Result limits | Hard row cap plus an honest "showing the first N of M" — the same discipline HSK-D and the Components gate use. |
| Timeout / cancellation | **Explicit `CommandTimeout`** on every command (the verifier's 15 s is the precedent and the *only* existing timeout in the stack) plus the request's `CancellationToken` threaded through `OpenAsync`/`ExecuteReaderAsync`. |
| Audit | **Required** — see §21. |
| Destructive confirmation | N/A; nothing is destructive. |
| Concurrency | One client per request, disposed deterministically. No pooling, no session reuse (none exists). |
| Remote permissions | The remote login's rights are the real boundary. The UI must report a permission denial as the remote system's answer, not as a platform error. |
| SSRF / network boundary | **Not a new exposure.** `connection.read` already triggers outbound connections to operator-supplied hosts via test-connect (§2.8). The host is fixed by the stored Connection, never supplied per request. The new exposure is **structure**, not reach. |

**Escape hatch, deliberately closed.** If implementation discovers that some metadata operation genuinely requires
an identifier embedded in SQL syntax, that is a **design/safety finding to be raised**, not a licence to widen the
query mechanism. The correct response is to re-scope the operation or bring it back through this gate — never to
introduce dynamic SQL quietly because one query needed it.

### 20.2 Why arbitrary SQL is refused

- No read-only enforcement is achievable platform-side (§4.3).
- Every safety dimension above becomes unbounded: no row cap is meaningful against a `WHILE` loop, no timeout
  protects against a blocking write, no parameterization exists when the whole statement is the parameter.
- It is materially more privileged than any permission the platform currently issues (§4.5).
- It sits against ADR-0027 §5's operator-honesty rule.

**Approved ruling (D-S1):** interactive/arbitrary SQL execution is **a separate future capability requiring its own
security/design gate**. It is **not part of Services v1**, it is **not inside any SVC plan**, and it must **not** be
described as a later tab or a follow-on phase of the SQL structure browser. This gate does not design it.

SVC-6 and SVC-7 are scoped so that nothing else in Services depends on a query console ever existing (§30).

### 20.3 Deferred operations and their blocking requirements

| Operation | Blocking requirement before it could be considered |
|---|---|
| SQL row/content preview | A data-disclosure decision, its own permission, and per-read audit including the table read. |
| SFTP browse | Async/timeout wrapping of a synchronous, token-less client (§5.3); a DTO boundary for `ISftpFile`. |
| SFTP download | All of the above, plus an exfiltration decision, size limits, and per-download audit. |
| SFTP rename / delete / upload | Mutation of an external system: confirmation, audit, and a genuine authorization design. Delete and upload do not exist on the client at all. |
| Campus Nexus anything | A new untyped client capability (§6.3). Not a surfacing exercise. |

---

## 21. Audit / provenance (Task 18)

### 21.1 The pre-existing gap — approved for closure (D-S3)

**Code fact:** Connection create, update, secret rotation, retire and test-connect emit **no audit records**
(§2.9), while Integration, Platform Settings, Notification Definition, ResourceContext and connection **ownership
moves** all do.

It would be indefensible to add remote-system operations to a resource whose own lifecycle is unaudited.
**Approved: close it as part of Services work, explicit and bounded — SVC-2.** This is **not** a platform-wide
audit-normalization epic; it touches `ConnectionManagementService` and nothing else.

Actions: `connection.created`, `connection.updated`, `connection.secret_rotated`, `connection.retired`,
`connection.verified`, in one new `ConnectionAuditActionNames` constants class following house convention.

### 21.2 Re-verified implementation constraints (C3) — the historical hazard is **still live**

The CTX-3B finding was re-audited against `SqlAuditService` at this commit rather than assumed. **It still holds,
in full**, and two further constraints were found:

| # | Verified finding | Evidence |
|---|---|---|
| 1 | **A non-null `AuditRecord.Resource` is string-appended into `Details`.** `details = (details ?? "") + "; " + $"Resource={record.Resource}"` — which **corrupts a JSON `Details` payload**. | `SqlAuditService.cs:23–24` |
| 2 | **`Resource` is not persisted at all.** `AuditLogEntity` has **no Resource column**; the append initializer never writes one. | `AuditLogEntity.cs:6–36`, `SqlAuditService.cs:26–46` |
| 3 | **`Resource` is always read back as `null`.** Both `QueryAsync` and `GetByIdAsync` hard-code `Resource: null`. | `SqlAuditService.cs:67, 100` |
| 4 | **There is no Resource filter.** `ApplyFilters` supports actor, action prefix, time range, outcome, moduleId, scheduleId, runId, resourceContextId and context classification — **and nothing resource-scoped**. | `SqlAuditService.cs:111–154` |

The live code already documents hazard 1: `ResourceOwnershipMoveService` passes `Resource: null` with an in-line
comment stating that a non-null Resource *"would corrupt this record's JSON Details payload … resourceType/resourceId
already live in the JSON itself."*

**Binding consequences for SVC-2:**

- **Pass `Resource: null`.** Do not rely on `AuditRecord.Resource` for anything — it is neither persisted nor
  filterable.
- **Connection identity lives in structured JSON `Details`**, following the `ResourceMoveAuditDetails` precedent
  (a serialized record with `resourceType` and `resourceId`). State this explicitly in the implementation plan.
- **Details must remain valid JSON.** Because hazard 1 only triggers on a non-null `Resource`, passing null is
  sufficient — no change to `SqlAuditService` is required or authorized.
- **`ResourceContextId`** is populated from the already-loaded Connection, per `AuditRecord`'s CTX-2 rule
  ("never inferred here from other ids").
- **No audit-schema migration.** The current schema is adequate for closing the gap, and a migration must not be
  introduced merely to enable a future panel.

### 21.3 Recent Activity is **not** a Services v1 deliverable — stated honestly

**SVC-2 does not make efficient Connection-scoped Recent Activity available, and this gate does not claim it does.**
With no Resource column and no resource filter (findings 2 and 4), retrieving "audit rows for connection X" would
require either a `LIKE` scan over the free-text `Details` column or a new indexed column plus migration.

Therefore:

- **Closing the Connection audit gap: approved (SVC-2).**
- **Shipping a Connection Recent Activity panel: NOT part of Services v1** — deferred, and dependent on separate
  future work to the audit query model. §18 lists it as blocked for this reason.

### 21.4 What Services must audit

| Interaction | Audit? | Why |
|---|---|---|
| Opening the workspace | **No** | Operator UI navigation. Not audit-worthy; would flood the log. |
| Expanding a section, switching a tab | **No** | Same. |
| Connection verification (test-connect) | **Yes** | An outbound operation using stored secrets. Currently unaudited — closed by SVC-2. |
| **Remote structure browse** | **Yes, at a deliberate granularity** | An external-system read. See below. |
| Remote record / file content read | **Yes**, if ever built | Data disclosure. |
| File download | **Yes**, if ever built | Exfiltration channel; record path and byte count. |
| Remote mutation | **Yes**, if ever built | Changes an external system. |

**Browse granularity (approved).** Record enough to establish that **an operator performed a remote metadata
operation against a given Connection** — actor, connection id, the operation performed, outcome, and row count.
Design the granularity so that **routine tree expansion does not generate useless audit noise**: a browse session or
a distinct metadata operation is the right unit, **not every node expansion**. Implementation chooses the exact
unit within that constraint and states its choice.

**Audit records must never contain** (D-S3): secret values; resolved credentials; remote content or result
payloads. Row **counts** are permitted; row **contents** are not.

**No generic "Service audit subsystem."** Reuse `IAuditService`, the existing `*AuditActionNames` constant-class
convention, and the existing timeline/query surface. One new constants class at most.

---

## 22. Lifecycle (Task 19)

**Selected: the Service has no independent lifecycle. Every state derives.** The strong prior survives the audit
intact — and trivially so, since the Service is the Connection's page.

| Situation | Workspace behaviour |
|---|---|
| Connection **Active** | Full workspace. Tools offered when a provider exists and the caller is authorized. |
| Connection **Retired** | Workspace renders read-only. Edit/Move/Retire already hidden by existing capability+state checks. **Tools hidden**, not disabled — a retired Connection is excluded from binding eligibility and should not be driving live operations. |
| **Connector definition missing** | Workspace still renders the stored config (it is platform data). `SupportsVerification` is already `false` via `IsVerificationSupported`. Tools hidden. **Known rough edge:** `UpdateConnectionAsync` throws `InvalidOperationException` → the controller maps it to `404`, so Edit fails obscurely. Worth an explicit notice: *"The connector for this connection is not registered in this host."* |
| **Provider missing** (connector present, no tool) | **The normal case** — three of three connectors today. Tools section simply absent. Nothing degraded, nothing announced. |
| **Module disabled / unavailable** | **No effect.** All three connectors are platform-supplied connector packages with `ModuleId = null`; connector definitions are seeded by hosted services independent of module state. This mirrors the Components gate §17 finding exactly. |
| Connection **moves ResourceContext** | Workspace moves implicitly — same page, freshly loaded Connection. `OwnershipChip` and `MismatchNotice` already handle it. No Service-side action. |
| **Secrets invalid** | Surfaces as a verification failure (`"Could not resolve connection secrets: …"`). Tools must fail the same way and must not leak the underlying exception text. |
| **Verification failed** | Tools remain **offered**, not blocked. Verification is a point-in-time fact, not a gate — the current UI already treats it that way, and blocking on a stale failure would be wrong. Show the last failure prominently. |

---

## 23. Components relationship (Task 20)

**Binding rule honoured exactly: `ComponentKey(ComponentKinds.Connector, connection.ConnectorDefinitionId)`.** A
pure construction. No join, no persisted edge, no new column, no second source of connector identity.

### UI treatment

Replace the current dead `MetadataItem Label="Connector definition" Monospace="true"` with a **link carrying the
connector's display name**:

- **Label:** `Connector`
- **Text:** the connector's `Name` (e.g. *Platform SQL*)
- **Href:** `/components/connector/{connectorDefinitionId}`
- **The raw id moves into `TechnicalDetails`** — ADR-0041 tier 3 (visible copy uses display names; identifiers in
  technical details), and UIX-6's `TechnicalDetails` component.

**Wording ruling:** use **"Connector"**, not *"Provided by Platform SQL"*. On Component detail, *"Supplied by"*
already means **module provenance** (Components §12). Reusing that phrasing for connector identity on the Connection
workspace would collide two different provenance statements in the operator's vocabulary.

**Getting the display name costs nothing — code fact.** `ConnectionsController` **already injects
`IConnectorDefinitionRegistry`** and already calls `.Get(connectorDefinitionId)` for `IsVerificationSupported`. Add
`ConnectorDisplayName` to `ConnectionDto` from that same in-memory dictionary hit. **Zero additional I/O**, including
in the list loop.

**Graceful degradation:** when the definition is not registered, render the raw id with no link (never "Unknown
connector", never a broken link) — the same discipline Components §17 applies to unresolvable module ids.

**Sequencing:** this link targets a route CMP-2/CMP-3 create (now shipped, so the link is available). Had Components
not shipped, the display name would have rendered **without** the link. The `ConnectorDisplayName` addition is independently valuable and independently shippable.

---

## 24. Integration relationship (Task 21)

**No change of any kind. Bindings stay `Integration → Connection`.**

- **Code fact:** `Binding(ModuleId, SlotName, ContractType, ConnectionId?, CapabilityId?)`; runtime resolution is
  `IRuntimeSlotResolver.ResolveSlotAsync<T>(moduleId, slotName)`; binding is the gate. Nothing consults a Service and
  nothing may start to.
- **No `Integration → Service → Connection` chain**, persisted or logical. A Service is a page; pages are not in
  execution paths.
- **Audited for benefit:** no existing integration workflow would gain from a Service link. The useful links already
  exist in both directions — `IntegrationConnections.razor:79` drills into `/connections/{id}`, and the workspace's
  "Used in" block links back to `/integrations/{id}`.
- **Permitted and free:** because the workspace *is* `/connections/{id}`, every existing Integration → Connection
  link automatically lands on the improved page. No link changes are required anywhere.
- `BindingEligibilityService` is untouched. `ConnectionState.Retired` exclusion is untouched.

---

## 25. HSK-D / Resource relationship (Task 22)

**Selected: no second Resource row. `/connections/{id}` stays the canonical target.**

- **Code fact:** `InventoryQueryService.MapConnection` emits `Href = $"/connections/{id}"`, consumed by both Config
  Inventory and the HSK-D Application/Shared Resources explorer.
- **Code fact:** HSK-D §2 lists what is deliberately **not** listed: *"Runs, Audit events, Notification definitions,
  Modules, System/Not-recorded pseudo-contexts, and Components."* A Service must join that list implicitly — but
  because it is not an object at all, there is nothing to exclude. **The cleanest possible outcome.**
- **No extra ResourceContext ownership row**, because there is no second owned object.
- **Moving the Connection moves the workspace implicitly** — same row, same Href, same page.

**Useful links, all already correct:** Resource Explorer row → `/connections/{id}` (unchanged) → connector link →
`/components/connector/{id}` (§23). That is a clean three-hop path from ownership to configuration to catalog, with
**no new route and no new identity**.

**HSK-D's ownership semantics are preserved exactly.** "Referenced is not owned" is untouched: a Shared Connection
bound by an Application Integration still belongs to Shared, and its workspace belongs to Shared with it.

---

## 26. Navigation (Task 23)

**Selected interim placement: none. `/connections` and `/connections/{id}` stay exactly where they are.**

- **No new rail domain.** `SectionOwnerConstants.KnownRailDomains` stays closed. ADR-0040 rule 3 explicitly refuses
  to promote Connections, and NAV-1 closed the rail.
- **No route tied to the shell View.** `?context=` remains a **list** filter only; the detail route carries no
  context token.
- **No new menu item in this gate.** Services is not the right vehicle for reopening the rail.

### 26.1 The finding the later gate must inherit

**`/connections` is not reachable from the rail at all** (§2.7). Operators arrive only via Config Inventory, an
Application's Resources list, an Integration, or a bookmark. This is a deliberate ADR-0040 consequence, but it is
also the single biggest practical obstacle to the Service *experience* being discovered — and **renaming the page
cannot fix it**.

**This is the §10 constraint-9 callout:** it does not prevent later cross-context aggregation (identity and APIs are
stable), but any future grouping surface that wants to present "the services in my operational area" will be linking
to a route the rail itself does not expose.

### 26.2 Constraints the later navigation / grouping gate inherits

1. `/connections` and `/connections/{id}` are **stable identity**. Reorganize entry points freely; do not change
   these routes, the API, or Connection ownership.
2. **`DomainMenuCapabilityTokens.ViewConnections` already exists and is already resolved** by
   `MenusController.HasCapability` — attaching a Connections leaf later needs **no new permission and no new token**.
3. Do not make Connection routes Context-specific. A future grouping surface must link to identity, not to a view.
4. Grouping membership must never grant access; authorization stays on `Connection` (§11).
5. If Connections is ever promoted, `RouteToSectionResolver` already maps it to `Config` and
   `ManagementBreadcrumbResolver` already roots a first-class `Connections` crumb — both are ready.
6. Nothing in Services introduces a persisted organizational relationship, a domain field, or a cross-context
   operation. There is nothing here to unpick.

---

## 27. Services v1 scope — APPROVED S-B (Task 24)

### 27.1 Classification

| Classification | Items |
|---|---|
| **Required Services foundation** | Connector display name + Component link (§23). Connector-declared field display names and secret present/absent presentation (§18). `TechnicalDetails` adoption (UIX-6). Verification/health promoted to a legible block. Lifecycle/fallback correctness (§22). Connectors-tab retirement + replacement links (§17). |
| **Prerequisite gap-closing** | Connection lifecycle + verification audit (§21). Explicit, bounded, and a precondition for any remote operation. |
| **Approved v1 exemplar** | **SQL structure browser** — read-only, platform-authored closed metadata query set, all selections bound as `SqlParameter`, **no dynamic SQL**, explicit timeouts, row caps, cancellation, audited, behind the new `connection.browse` action. |
| **The provider/tool seam** | One contract plus the smallest DI resolution — **no registry class, no contributor interface, no sanity hosted service** (§12, C1). |
| **Future capability (not v1)** | Arbitrary SQL / query console — **separate security/design gate**. SQL row/content preview. SFTP browse (needs async/timeout wrapping first). Connection **Recent Activity** panel (needs audit-query work beyond SVC-2, §21.3). A provider registry (needs a second provider). Capability advertisement to Components (§14). |
| **Unsafe or unjustified now** | SFTP download, upload, rename, delete. Campus Nexus browse. Any generic command/action framework. Any persisted Service entity. Any `service.*` permission. A `/services` route or list. Any DML, DDL or remote mutation. |

### 27.2 The SQL boundary, stated as the approved ruling (D-S1)

**Approved — in v1:**

- Platform-authored, **closed** metadata operations only.
- All externally supplied schema/table selections bound as **`SqlParameter` values**.
- No dynamic SQL construction; no user-authored identifiers in command text (§20.1, C2).

**Not approved — and not a later tab in the same implementation plan:**

- **No user-authored SQL.**
- **No arbitrary statement execution.**
- **No row/content preview.**
- **No DML or DDL.**
- **No remote mutation.**

**Purpose of S-B:** to validate the Service/tool model with **one useful, safely constrained exemplar** — not to
open a path to a query console. An arbitrary SQL/query-console capability remains **future work requiring its own
security/design gate**, and SVC-6/SVC-7 are scoped so that nothing else in Services depends on it ever existing.

---

## 28. Performance (Task 26)

| Path | Cost, as designed |
|---|---|
| `/connections` list | **Unchanged.** Adding `ConnectorDisplayName` is an in-memory `ConcurrentDictionary` hit per row against a registry the controller already injects and already calls. **No new I/O.** |
| Connection workspace load | One `GET /api/connections/{id}` (existing) + one binding query (existing). **No client creation. No remote call.** |
| Provider resolution | **Local and static** — a keyed lookup over DI-registered providers. No I/O, no probe, no client construction. |
| Tool availability display | From that local lookup. **Never** by attempting a connection. |
| Remote browse | **Only on explicit operator invocation.** One client, one or two bounded queries, deterministic disposal. |
| Secret resolution | **Only** inside a tool invocation or verification. Never on page load, never on list. |

**Explicitly forbidden** (assert these):

- Creating a remote client to render a list or a page.
- Any network call during ordinary list rendering.
- One remote capability probe per Connection.
- N+1 provider discovery — provider lookup is by key, never by enumeration per row.
- Caching or pooling remote clients. None exists, and inventing it here would be an invalidation and
  credential-lifetime problem for no measured benefit.

**Note the existing hazard, unchanged:** `ConnectionsController.List` is already N+1 on bindings (§2.5). Services
neither fixes nor worsens it; it is recorded so no Services plan reuses that endpoint for anything new.

---

## 29. Test plan (Task 25)

New: `tests/Apperture.UnitTests/ControlPlane/ConnectionWorkspaceTests.cs`,
`tests/Apperture.Management.Ui.Client.Tests/` page wiring, extensions to existing
`ConnectorDefinitionCatalogReadServiceTests` neighbours, and `tests/Apperture.ArchTests/` for boundaries.

### Derivation

| # | Assertion |
|---|---|
| DV1 | A Connection yields exactly one workspace; there is no second detail route. **Source assertion: no `@page` beginning `/services`.** |
| DV2 | **No persisted Service row and no Service table** — source/migration assertion that no `Service*` entity or migration is introduced. |
| DV3 | Workspace identity is stable across reload: `/connections/{id}` round-trips to the same Connection. |
| DV4 | Moving a Connection between ResourceContexts changes the workspace's effective Context with **no Services-side write** — assert only the Connection row changed. |
| DV5 | **No type named `Service`, `ServiceDefinition`, or `IServiceExperience*`** is introduced for this concept (the §7 naming rule; mirrors Components G3). |

### Authorization

| # | Assertion |
|---|---|
| AZ1 | No `connection.read` ⇒ workspace forbidden (existing `ForbiddenState` path). |
| AZ2 | `connection.read` only ⇒ full base workspace, **tools section absent** (not empty, not disabled). |
| AZ3 | *(S-B)* `connection.read` + `connection.browse` ⇒ tools present. |
| AZ4 | *(S-B)* `connection.browse` **without** `connection.read` grants nothing — the workspace is still forbidden. Aggregation never grants access. |
| AZ5 | *(S-B)* The browse endpoint itself re-evaluates `connection.browse` server-side; a direct API call without it returns 403. Not UI-only gating. |
| AZ6 | Edit/Move require `connection.update`; Retire requires `connection.delete` — regression guards on existing behaviour. |
| AZ7 | Tool availability is not disclosed through any endpoint reachable without `connection.browse`. |

### Provider resolution

| # | Assertion |
|---|---|
| PR1 | *(S-B)* A SQL Connection resolves the SQL structure-browser provider. |
| PR2 | An SFTP Connection resolves **no** provider in v1, and the workspace renders normally. |
| PR3 | A Campus Nexus Connection resolves **no** provider, and the workspace renders normally. |
| PR4 | An **unknown** `ConnectorDefinitionId` falls back safely: stored config still renders, verification unavailable, tools absent, explicit notice, **no exception**. |
| PR5 | Provider resolution performs **zero I/O** — strict-mock assertion that no client factory and no secret resolver is touched during page/DTO composition. |

### Components

| # | Assertion |
|---|---|
| CP1 | The connector link is derived as `ComponentKey(ComponentKinds.Connector, connection.ConnectorDefinitionId)` — constructed, never looked up from a join. |
| CP2 | **No persisted Service→Component edge and no new column** on `Connection` or `ConnectorDefinition`. |
| CP3 | **The Components list path performs no Service/provider lookup** — strict no-call assertion (Components P2 applies equally). |
| CP4 | `ConnectorDisplayName` is sourced from the in-memory registry; the list path issues **no additional query per row**. |
| CP5 | An unregistered connector renders the raw id with **no link** and no fabricated name. |

### Lifecycle

| # | Assertion |
|---|---|
| LC1 | Retired Connection ⇒ workspace read-only; **tools hidden**; Edit/Move/Retire absent. |
| LC2 | Missing connector definition ⇒ config renders, `SupportsVerification` false, tools absent, notice shown. |
| LC3 | Unavailable provider ⇒ base workspace unaffected. |
| LC4 | Verification failure ⇒ tools still offered; last failure displayed. |
| LC5 | Invalid secrets ⇒ failure is reported without leaking the underlying exception text or any material key/value. |

### ResourceContext

| # | Assertion |
|---|---|
| RC1 | Workspace Context always equals `Connection.ResourceContextId` at read time. |
| RC2 | **No independent Context field exists** anywhere in the Services surface. |
| RC3 | Shared and Application-owned Connections both render correctly, with `OwnershipChip` and `MismatchNotice` behaviour preserved. |
| RC4 | Context is **not** treated as an authorization boundary — a caller with `connection.read` sees Connections in every Context, as today (guards against inventing a rule the platform does not have). |

### UI

| # | Assertion |
|---|---|
| UI1 | `/connections/{id}` is directly linkable and bookmarkable; the breadcrumb trail stays rooted at `Connections` with **no Config/Inventory ancestry** (NAV-1 F-03 regression guard). |
| UI2 | **No duplicate/conflicting Connection detail state** — exactly one route renders Connection detail. |
| UI3 | HSK-D Resource Explorer's Connection row lands on the workspace: `InventoryQueryService.MapConnection` still emits `/connections/{id}`. |
| UI4 | Config Inventory's Connection Href is unchanged. |
| UI5 | `IntegrationConnections.razor` drill-in still resolves. |
| UI6 | UIX-6 compliance on the changed page — no native confirm, no arbitrary z-index, no new panel/drawer (`Uix6InteractionSystemTests`). |
| UI7 | **No secret value and no secret-ref GUID** appears in operator-facing copy; secret fields render present/absent only. |
| UI8 | *(after SVC-4)* The Connectors tab is gone and the catalog link resolves to `/components?kind=connector`. |

### Safety — SQL structure browser (C2)

| # | Assertion |
|---|---|
| SF1 | **Every externally supplied schema/table selection is bound as a `SqlParameter`** — assert the parameter collection, not just the absence of a crash. |
| SF2 | **The statement set is closed.** Assert the exact, literal command texts the browser can issue; any text outside that set is a test failure. |
| SF3 | **No command text is built by concatenation or interpolation, and no identifier quoting is used** — an injection-shaped schema/table name flows through as an inert *parameter value* that simply matches no metadata row. There is no quoting path to test because there is no dynamic-SQL path. |
| SF4 | Every command sets an explicit `CommandTimeout`. |
| SF5 | The request `CancellationToken` aborts an in-flight browse. |
| SF6 | Row caps are enforced and the truncation is reported honestly ("first N of M"). |
| SF7 | **No DML, DDL, multi-statement batch, arbitrary statement or row/content read can be issued** — implied by SF2, asserted separately because it is the load-bearing prohibition. |
| SF8 | Remote permission denial surfaces as the remote system's answer, not as a platform 500. |
| SF9 | The client is disposed on every path, including exceptions and cancellation. |
| SF10 | Browse audit is appended at the approved granularity with actor, connection id, operation, outcome and row count — **no returned content**, and **not one record per node expansion** (§21.4). |

### Audit (SVC-2)

| # | Assertion |
|---|---|
| AU1 | Create, update, secret rotation, retire and verify each append exactly one `AuditRecord`. |
| AU2 | `ResourceContextId` on the record comes from the loaded Connection, never inferred (CTX-2 rule). |
| AU3 | **No secret value, secret ref, resolved credential or remote content appears in `Details`.** |
| AU4 | **`Resource` is passed as `null` on every new record** (C3 finding 1) — a non-null `Resource` would be string-appended into `Details` by `SqlAuditService` and corrupt the JSON. |
| AU5 | **`Details` parses as valid JSON** for every new record, and carries connection identity structurally (`resourceType`/`resourceId`), following `ResourceMoveAuditDetails`. |
| AU6 | Existing connection **ownership-move** audit is unchanged (regression guard on `ResourceOwnershipMoveService`). |
| AU7 | Opening the workspace appends **nothing**. |
| AU8 | **No audit-schema migration is introduced** by SVC-2 (source/migration assertion). |

### Architecture

| # | Assertion |
|---|---|
| AR1 | `Apperture.ConnectorResolution` still references no `Apperture.Connectors.*` (existing `ConnectorResolutionDependencies`, extended if a provider lands there). |
| AR2 | `ControlPlane.Security` and `Management.Api` acquire **no new** compile-time reference to a connector implementation assembly. |
| AR3 | No executable member is added to `ConnectorDefinition` or `IConnectorDefinitionContributor` (ADR-0035 §1). |
| AR4 | `RuntimeVisibleDevMetadataGuardTests` extended to any new operator-facing copy — no `SVC-`/epic labels. |

---

## 30. Implementation decomposition (Task 27)

Sonnet-sized, independently testable and committable, reflecting the approved **S-B** path. **Nothing here is
authorized to start** by this gate alone; Components implementation, which had to come first, is now complete.

**Grouping:**

- **Base Connection-workspace foundation** — SVC-1, SVC-3, SVC-5
- **Audit closure** — SVC-2
- **Connectors-tab retirement** — SVC-4
- **Provider seam + SQL structure browser** — SVC-6, SVC-7
- **Documentation closure** — SVC-8

| Plan | Group | Scope | Depends on | Est. |
|---|---|---|---|---|
| **SVC-1 — Connector identity on the Connection surface** | Foundation | `ConnectorDisplayName` on `ConnectionDto` from the already-injected registry; workspace renders the connector by display name; raw ids move into `TechnicalDetails`. Graceful degradation for an unregistered connector. Tests: CP4, CP5, UI7 (partial), DV3. | — | **Small** |
| **SVC-2 — Connection lifecycle audit** *(closes a pre-existing gap, §21; explicit and bounded — not an audit-normalization epic)* | Audit | `IAuditService` calls in `ConnectionManagementService` for create/update/secret-rotation/retire/verify; one `ConnectionAuditActionNames` constants class; **`Resource: null`**; connection identity in **structured JSON `Details`**; `ResourceContextId` per CTX-2; **no schema migration**. Tests: AU1–AU8. | — | **Small** |
| **SVC-3 — Workspace presentation** | Foundation | Connector-declared field display names and descriptions; secret fields as present/absent; verification promoted to a legible health block; `TechnicalDetails` adoption. Tests: UI6, UI7, LC2, LC4. | SVC-1 | **Medium** |
| **SVC-4 — Connectors-tab retirement and catalog coherence** *(owns the D2 handoff, §17)* | Tab retirement | Remove the Connectors tab from `/connections`; add the catalog link and the per-Connection Component link; update page copy. Tests: UI8, CP1, CP2, UI1. | Components CMP-2/CMP-3 shipped | **Small** |
| **SVC-5 — Lifecycle and fallback correctness** | Foundation | Retired/missing-definition/missing-provider/verification-failure states; the unregistered-connector notice; ensure no path throws. **Campus Nexus stays fully valid with no provider.** Tests: LC1–LC5, PR2–PR4, RC1–RC4, DV1–DV5. | SVC-3 | **Small** |
| **SVC-6 — `connection.browse` and the tool seam** | Provider seam | New action in `ActionConstants` + `PlatformPermissionSeeder`; `IConnectionToolProvider` contract in Core.Application; **resolution via the registered provider set keyed by `ConnectorDefinitionId`**; authorized application service; tools slot rendered only when authorized. **No registry class, no contributor interface, no startup sanity hosted service** (C1). Tests: AZ2–AZ5, AZ7, PR1, PR5, AR1–AR3. | SVC-5 | **Medium** |
| **SVC-7 — SQL structure browser exemplar** | Provider seam | `ISqlStructureBrowser` in the SQL connector; **closed, platform-authored metadata query set** (schemas → tables/views → columns) with all selections bound as `SqlParameter` and **no dynamic SQL** (C2); explicit timeouts, row caps, cancellation, deterministic disposal; audit at the approved granularity; the browse UI. **Contains no query console, no row/content preview, no DML/DDL.** Tests: SF1–SF10. | SVC-6 | **Medium** |
| **SVC-8 — Documentation closure** | Docs | ADR for the Connection-workspace/Service-language ruling and the `connection.browse` model; update this gate's status; roadmap items 5/7; `connector-resource-family.md`; `runtime-connection-resolution.md` (record the management-plane operator path explicitly); glossary; closure audit. | all | **Small** |

**Order:** SVC-1 → SVC-2 → SVC-3 → SVC-5 → SVC-4 → SVC-6 → SVC-7 → SVC-8.
SVC-4 is placed after SVC-5 only because it depends on Components shipping; it is otherwise independent.

**Isolation guarantees (D-S1):**

- **Arbitrary SQL is explicitly outside every SVC plan.** No plan above contains, prepares for, or depends on a
  query console. A query console would be new work behind its own security/design gate.
- **SVC-6 and SVC-7 are isolated**: SVC-1…SVC-5 and SVC-8 deliver a complete, coherent Connection workspace with no
  dependency on the SQL exemplar, and none of them assumes a second tool provider will ever exist.
- **Recent Activity is in no plan** — it is deferred pending separate audit-query work (§21.3).

---

## 31. Components-gate amendments

**No settled Components decision is reopened.** The projection-only model, Component identity `(Kind, NativeId)`,
the origin/kind taxonomy, authorization aggregation and the non-1:1 Resource rule are all **confirmed** by this
audit, not merely left alone.

Three precise items, all small, none structural:

### A1 — Connectors-tab cleanup timing *(the allowed "change to Connectors-tab cleanup timing")*

Components **D2 behaviour is unchanged**: keep the tab, link its rows to `/components/connector/{nativeId}` in CMP-5.
This gate now supplies the eventual fate D2 delegated: **the tab retires in Services implementation (SVC-4)**, after
`/components` ships. **Components implementation does not wait for this and must not be slowed by it.**

### A2 — Factual correction to Components §12 *(not a design change)*

Components §12 specifies *"Create a connection with this connector → `/connections/new?connectorDefinitionId=…`"*.
**Code fact: that query parameter does not exist.** `ConnectionNew.razor` supports only `?context=`; the connector is
chosen from a `<select>` bound to `_connectorDefinitionId` with no `[SupplyParameterFromQuery]`.

**CMP-3 may add the small supported preselection behaviour if still desired** — a `[SupplyParameterFromQuery]` plus
a preselect, roughly five lines — or link to `/connections/new` without the parameter. This is a CMP-3
implementation detail, not a design amendment; it is recorded only because the Components gate text currently reads
as though the capability already exists.

### A3 — Capability advertisement: **no amendment**

§14 recommends **no advertisement in v1**, so `ComponentDetailDto` and `ComponentSummary` are **unchanged** and CMP-1
proceeds exactly as designed. The mechanism for later is recorded in §14 and requires no change now.

**Net effect on CMP-1: none.** Components implementation is unblocked by this gate.

---

## 32. Product rulings (decided)

**All four decisions were reviewed and approved on 2026-09-23. None is open.** They are recorded here as binding;
implementation bodies must not re-litigate them.

### D-S1 — Services v1 scope — **APPROVED: S-B**

Services v1 includes:

- the derived/evolved **Connection workspace foundation**;
- the **provider/tool seam**;
- a **SQL Server structure-browser exemplar**.

It **does not** include an arbitrary SQL query console.

The SQL structure-browser boundary is explicit and binding:

- platform-authored, **closed** metadata operations only;
- **no user-authored SQL**;
- **no arbitrary statement execution**;
- **no row/content preview**;
- **no DML/DDL**;
- **no remote mutation**.

**The purpose of S-B is to validate the Service/tool model with one useful, safely constrained exemplar.** An
arbitrary SQL/query-console capability remains **future work requiring its own security/design gate**, and must
**not** be described as a later tab in the same implementation plan (§20.2, §27.2, §30).

### D-S2 — Browse authorization — **APPROVED: one new action**

```
connection.browse   on   ResourceTypeConstants.Connection   @ instance
```

Semantics: **additive to `connection.read`**; scoped through the existing Connection authorization model; required
for reading remote structure/metadata; **not default-granted** merely because an operator can read Connection
metadata.

**Not added:** `service.read`, `service.browse`, generic tool permissions, or any arbitrary SQL execution
permission. Row/content preview is outside Services v1.

**Durable architectural rule (the approved wording):**

> No `service.read` exists in the derived Connection-workspace architecture. A Service-specific permission would
> require a future, independently justified Service authorization boundary.

### D-S3 — Connection audit gap — **APPROVED: close it as part of Services work (SVC-2)**

Brought into the existing audit model: **create, update, secret rotation, retire, connection verification /
test-connect**. Explicit and bounded — **not** a platform-wide audit-normalization epic.

Audit records must **never** contain secret values, resolved credentials, or remote content/result payloads.

For new Service tools: ordinary UI navigation is **not** audit-worthy; actual external-system operations **may
be**. For SQL structure browsing, record enough to establish that an operator performed a remote metadata
operation, at a granularity where **routine tree expansion does not create useless audit noise** (§21.4).

**Scope clarification, recorded honestly:** closing the audit gap is approved; **shipping a Connection Recent
Activity panel is not part of Services v1**. SVC-2 alone does **not** make efficient Connection-scoped Recent
Activity available — the current audit query model has no Resource column and no resource filter (§21.2), so that
panel needs separate future work and stays deferred (§21.3).

### D-S4 — Product terminology — **APPROVED: no new operator-facing noun**

"Service" is **not** introduced as an operator-facing noun in v1. Operators continue working with **Connections**,
**Connection detail/workspace**, and **connector-specific tools**. "Services" remains the architectural/roadmap term
for this capability body.

No relabelling of Connections as Services, of `/connections` as `/services`, or of Connection detail as Service
detail. **No `/services/{id}` route.**

---

## 33. Risks

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| RS1 | **"Service" acquires a code type**, colliding with 245 `*Service` application-service types and with BCL `IServiceProvider`. | **High** — the noun invites it | §7 forbids it; test DV5 asserts it; the recommended architecture creates no such type at all. |
| RS2 | **Scope creep from structure browsing into arbitrary SQL.** "We already have the connection open; just let them type a query." | **High** — it is a small-looking diff with a large blast radius | D-S1 refuses it and forbids describing it as a later tab; SF2/SF7 assert the statement set is closed; the action is named `browse`, not `query`; §30 keeps it out of every SVC plan. |
| RS3 | **A tool ships gated only on `connection.read`**, because that is what test-connect uses and it looks like the precedent. | **High** | §11.2 draws the line; AZ5 asserts server-side re-evaluation; §2.8 explains why verification's gate is bounded and browsing's is not. |
| RS4 | **A second detail page appears** — `/services/{id}` built "just for the tools". | Medium | §15 rules it out; UI2/DV1 assert a single route; §25 shows Inventory and HSK-D would be orphaned. |
| RS5 | **Remote client created on list or page render**, adding network latency and credential use to ordinary browsing. | Medium — `IConnectionResolver` is already injectable in the Management Host | PR5 is a strict no-call assertion; §28 forbids it explicitly. |
| RS6 | **Secrets or raw exception text leak** through tool error messages. | Medium | UI7, LC5, AU3; map exceptions rather than surfacing them; `ResolvedConnectionMaterial` already redacts `ToString`. |
| RS15 | **A non-null `AuditRecord.Resource` is passed in SVC-2**, corrupting JSON `Details` via `SqlAuditService.cs:23–24`. | **High** — it is the natural-looking field to populate | C3 re-verified the hazard at this commit; AU4/AU5 assert `Resource: null` and valid-JSON `Details`; `ResourceOwnershipMoveService` is the in-code precedent. |
| RS16 | **Recent Activity is promised on the strength of SVC-2**, though the audit query model cannot retrieve by connection id. | Medium | §21.3 states the limitation; §18 marks it deferred; it appears in no SVC plan. |
| RS7 | **A generic command/action framework is introduced** to "support future tools". | Medium | §12.3 and §19 forbid it; ADR-0027 removed this exact shape once; AR3 guards the definition contract. |
| RS8 | **SFTP browse is added casually**, ignoring that the client is synchronous, token-less and timeout-less. | Medium | §5.3 records the blockers; SFTP is classified "future capability" with named prerequisites (§27.1). |
| RS9 | **The Connectors tab is retired during Components implementation**, adding unrelated regression risk to CMP-5. | Low-Medium | §17 and §31 A1 fix the timing in Services (SVC-4); Components behaviour is explicitly unchanged. |
| RS10 | **Provider registry built for one provider**, reproducing the speculative machinery both gates reject. | Medium | C1 settles §12.2, §12.3 and SVC-6 on one wording: one contract plus the registered provider set keyed by `ConnectorDefinitionId` — no registry class, no contributor interface, no sanity hosted service until a second provider exists. |
| RS11 | **Someone treats `ResourceContextId` as an authorization boundary** and writes a Service rule the platform does not implement. | Medium | §2.8 and §10 state it plainly; RC4 asserts current behaviour. |
| RS12 | **Connection audit is skipped** and a remote-read capability ships on a resource whose own lifecycle is unrecorded. | Medium | D-S3 approves closure; SVC-2 sequenced before SVC-6/7; AU1–AU8. |
| RS13 | **The discoverability problem is mistaken for a naming problem** and a rail domain is added to "make Services findable", contradicting ADR-0040 rule 3. | Medium | §16 and §26; the finding is handed to the navigation gate with the constraint list in §26.2. |
| RS14 | **CMP-3's `?connectorDefinitionId=` deep link silently does nothing**, because the parameter does not exist. | **High if unrecorded** | §31 A2 names the exact file and the five-line fix. |

---

## 34. Readiness

**Both design gates are complete and approved. No product decision remains open.** D-S1–D-S4 are recorded as
decided in §32.

**Settled and grounded in code at `2fa0857`:** that Service is not an entity; that identity is `ConnectionId`; that
cardinality is 1:1; that Context derives from the Connection; that no `service.*` permission exists in this
architecture; that clients stay runtime details; that no generic browse abstraction is justified; that the workspace
is `/connections/{id}`; that `/connections` is the list; that Integrations, HSK-D and Bindings are untouched; that
the Connectors tab retires in Services (SVC-4); and that Components needs no design amendment.

**Components implementation was the next implementation body and has since been delivered (see the update under the
status line).** §31 confirms no amendment to any approved Components
decision; the only Components-side action is a five-line factual correction inside CMP-3's own scope (A2), and the
Connectors-tab timing ruling (A1) explicitly leaves CMP-5's planned behaviour unchanged. The roadmap sequencing —
Components design gate → Services design gate → **Components implementation** → Services implementation — is now
satisfied through both gates; CMP-1 was unblocked and Components implementation is now complete.

**Services implementation is ready to plan but not started; its Components-implementation gate is now satisfied.** SVC-1…SVC-8
are defined in §30; §11.3, §20.1, §21.2 and §29's safety and audit matrices are the binding constraints on them.

**Nothing in this gate is implemented.** No Services code, no schema, no permission, no nav change, no route. The
product rulings are approved; the Services implementation body is not begun (Components implementation, which this
gate confirmed, is complete).

> *(The two paragraphs above describe the state when the gate was approved. Implementation is now complete — see §35.)*

---

## 35. Implementation record (2026-09-24)

**Services v1 (S-B) is implemented as approved.** SVC-1 → SVC-2 → SVC-3 → SVC-5 → SVC-4 → SVC-6 → SVC-7 → SVC-8 were delivered in that
order on one branch, each with targeted tests and an independent review (SVC-6 and SVC-7 reviewed on Opus with a security brief).
Full account, evidence and residuals: [`services-connection-workspace-closure.md`](../../../audits/services-connection-workspace-closure.md);
decision record: [ADR-0044](../../../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md).

Every ruling in §32 (D-S1…D-S4) and every binding constraint in §11, §12, §20.1, §21 and §29 held. Where the code turned out to differ from
what this gate recorded, **the code won and the difference is stated**:

- **§12 / AR2** — `ControlPlane.Security` *already* referenced `Apperture.Connectors.CampusNexus` (for an unrelated option provider), so
  the boundary is enforced as **no new connector reference** with that baseline pinned; `Management.Api`, `Core.Application` and
  `ConnectorResolution` reference none. The tool seam itself uses no connector type.
- **§12.2** — the SQL provider lives in `Apperture.ConnectorResolution` (the SQL connector has no separate package) and is registered by a
  management-only extension, `AddPlatformSqlStructureBrowser`, called by the Management Host alone; runtime workers do not compose it.
- **§22** — the "Edit/Move/Retire already hidden for Retired" statement was not true at the audited commit (only Retire was). SVC-5 made
  it true and also hides Test for retired Connections.
- **§20.1 / §11.3** — implementation added two safety rules the gate did not spell out: a Connection with **no SQL login** is refused
  (it would sign in as the platform host's identity, so "the remote login's rights" would not bound what is shown), and at most **two**
  structure requests run at once per Connection (429 otherwise). Both came from the security review and are covered by tests.
- **§21.4** — browse audit records the operation, counts and outcome; it deliberately does **not** record the schema or table the
  operator selected, because that is echoed remote content. Each request writes one record, fail-closed (no record → no result).
  "Not every node expansion" is interpreted as: every *remote* request is one record, and repeat expansions are answered from the
  browser's per-node cache without a request (so without a record).
- **§21 / SF6** — the truncation notice reads "Showing the first N … More exist that are not shown", because the total is never queried
  (the fetch takes one row beyond the cap only to detect truncation), so "first N of M" is not available.
- **§29 / AR4** — `RuntimeVisibleDevMetadataGuardTests` now scans the workspace pages, the browser component and the presentation helper
  and forbids `SVC-` labels, in addition to the workspace source-text test.
- **§11.3** — the `connection.browse` grant reaches the platform administrator role through the startup repair set; break-glass sessions
  pass every check by design. A `CanBrowseConnections` capability (true only with `connection.read`) was added so callers without the
  grant are never sent to an endpoint that must refuse them.
- **§17 / A2** — `/components?kind=connector` is now honoured (address parse + navigation subscription; two requests on a direct load so an
  unseen kind can never influence a query).
- **§21.3** — Connection Recent Activity is **not** delivered, as approved.

Not started, as required: a query console, row/content preview, SFTP or Campus Nexus tools, a provider registry, capability advertisement to
Components, and the navigation / cross-context grouping body.
