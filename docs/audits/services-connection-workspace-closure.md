# Services (Connection workspace) — implementation closure

Date: 2026-09-24
Design authority: [Services design gate](../01-architecture/transitions/components-services-catalog/services-design-gate.md)
(D-S1…D-S4), with the [Components design gate](../01-architecture/transitions/components-services-catalog/components-design-gate.md)
as production input.
Decision record: [ADR-0044](../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md).

**Current state (2026-09-29):** Services implementation (SVC-1…SVC-8) is **complete and on `master`.**

**Closeout-session record (2026-09-24):** Branch `services-connection-workspace` (eight plan commits plus review follow-ups; not merged, not pushed at that session).

**Status: Services implementation (SVC-1…SVC-8) is complete.** "Services" delivered no Service entity: it evolved the
existing Connection workspace, closed the Connection audit gap, retired the duplicate Connectors tab, and added one
optional connector tool — a SQL Server **structure** browser — behind one new action, `connection.browse`.

## What shipped

| Plan | Delivered |
|---|---|
| **SVC-1** connector identity | `ConnectorDisplayName` on the Connection read contract from the already-injected in-memory registry (no extra I/O, including in the list loop); the Connector shown by name, linked to `/components/connector/{id}` (link only when the caller can read the catalog); unregistered connector = stored id, no link, no invented name; raw ids into `TechnicalDetails`. |
| **SVC-2** lifecycle audit | `ConnectionManagementService` appends one record for create (inside its transaction), update, secret rotation, retire and test-connect (`ConnectionAuditActionNames`). `Resource` is null; identity is structured JSON `Details`; `ResourceContextId` is the loaded Connection's. Keys and status only — never a secret value, secret reference, config value or remote summary. A rotation that fails after the secret was written, and a verifier that throws, are recorded as failed; a failed row save detaches the rejected update so the failure record cannot commit it through the shared context. No schema change. |
| **SVC-3** workspace presentation | The existing page reshaped: header · Overview/health · Configuration · Used in · Tools slot · TechnicalDetails. Configuration renders connector-declared names and descriptions; secret fields are only *Set* / *Not set*. Health is a legible point-in-time fact. The definition lookup is best-effort and cannot fail the page. |
| **SVC-5** lifecycle and fallbacks | Retired = read-only with a notice (test/edit/move/retire hidden). Unregistered connector = notice; no test or edit, move/retire kept; health says "unregistered", not "unsupported", and keeps the last recorded result. Editing needs `connector.definition.read`. Failed save/test is a toast, never a whole-page error; a test that ran but did not verify no longer shows a success banner. Verifiers no longer echo exception text when secrets cannot be resolved (one fixed message); cancellation propagates. |
| **SVC-4** Connectors tab | The tab, its duplicate table and `?tab=` handling are gone; `/connections` lists Connections only, with an inline "Browse the connector catalog" link (shown to callers who can read definitions). `/components?kind=connector` now opens filtered on a direct load and on in-app navigation; unknown or unseen kinds are dropped exactly as other unrecognised filters are; plain `/components` is unchanged. |
| **SVC-6** permission and seam | `connection.browse` (Connection, instance, Connector family), granted to the platform administrator role via the repair set, additive to `connection.read`. `IConnectionToolProvider` + typed `ISqlStructureBrowser`; `ConnectionToolAccess` requires both grants before any provider lookup and refuses identically whether or not a tool exists; `GET api/connections/{id}/tools`; `CanBrowseConnections` capability; Tools section only when the caller may browse, a provider exists, and the Connection is Active. |
| **SVC-7** SQL structure browser | Three fixed, parameterized `INFORMATION_SCHEMA` statements (`SqlStructureQueries`, asserted literally); explicit connect/command timeouts, a 500-item cap with truncation notice, cancellation end to end, a client per operator-invoked request with pooling off and deterministic disposal; fixed safe outcomes for remote refusal/outage; three metadata-only POST endpoints (see the request-forgery hardening below); one fail-closed audit record per remote request (counts and outcome only); a UI that requests nothing until asked and fetches each node once. |
| **SVC-8** documentation | ADR-0044, this closure, the design-gate implementation record (§35), roadmap items 5/7, transitions README, connector-resource-family and runtime-connection-resolution, current-inventory, glossary, decisions index. |

## Invariants verified

- **No Service anything** — architecture tests pin no type named `Service`/`ServiceDefinition`/`IServiceExperience*`, no Service
  `DbSet`/entity, no `service.*` action, exactly one `/connections/{…}` detail route and no `/services` route, and no
  Service/Component/organisational-grouping property on `Connection`, its entity or its DTOs.
- **Ownership is the Connection's and is not authorization** — a single `ResourceContextId` column; the read contract exposes
  only that and its ref; a unit test drives `GetConnectionAsync` for a Shared and an Application-owned Connection and proves the
  authorization inputs are identical (instance scope) and carry no Context.
- **Components relationship is derived** — the link is `ConnectorDetailHref(ConnectorDefinitionId)`; reflection pins that no
  `Component`/`Service` property exists on `Connection`, `ConnectionEntity` or `ConnectorDefinition`.
- **`connection.browse` is additive and explicit** — evaluated against the **real** RBAC evaluator: read + create + update +
  delete + role.manage together still do not authorize it; only an explicit grant does; browse without read grants nothing (at
  the service, the endpoint and the capability flag). End to end through the real Management Host: a caller with read (even with
  create/update/delete) or browse alone gets a plain 403 on all four tool endpoints with no availability in the body.
- **Structure only, no dynamic SQL** — `SqlStructureQueries.All` is asserted as three exact literals; a regex test forbids
  DML/DDL/EXEC/QUOTENAME/`;`/`INTO`/`*`/any non-`INFORMATION_SCHEMA` source; injection-shaped names (quotes, `]`, `GO`, `EXEC
  xp_cmdshell`, a leading `'; DROP TABLE`) are proven to be inert parameter *values* both against recording ADO.NET doubles and
  against real SQL Server (row count unchanged); scalar and non-query execution throw in the double; the controller has no
  PUT/PATCH/DELETE action and no `sql`/`statement`/`command`/`query`/`payload` parameter (the three structure requests take
  only a typed request record holding the selected values).
- **Bounded and disposed** — command timeout asserted on every command; the cap and honest truncation on real and faked data;
  cancellation reaches the execute call and propagates as cancellation (including a real mid-`OpenAsync` cancel against a local
  server that never answers); connection, command and reader are disposed on success, failure and cancellation; `Pooling=false`
  and the connect timeout are pinned on a real `SqlConnection`; one client per request, nothing cached.
- **No network on render** — the list creates no client; the workspace and the tool render with a strict client and record zero
  structure calls; provider lookup depends on no client factory, resolver or secret resolver (constructor-type test plus a
  repository-call-log test).
- **Audit is safe** — one record per remote request; never the selected names, returned content, secrets or remote text; fail-closed
  (result withheld if the record cannot be written); failure and cancellation paths record once and preserve the original
  exception; requests refused before the provider is reached (authorization, validation, retired, unavailable, busy) write none, while
  a request the provider itself declines without contacting the database (no SQL login, unresolvable secrets) is recorded as failed; verified against the real SQL audit store (valid JSON,
  context column populated).
- **Integration Bindings, HSK-D and Inventory untouched** — no change to Binding persistence or resolution, `MapConnection` still
  emits `/connections/{id}`, and the workspace remains one Resource row.

## Rulings and deviations made during implementation

1. **The gate's "Security must not reference connector assemblies" was not true at the audited commit.**
   `ControlPlane.Security` already referenced `Apperture.Connectors.CampusNexus` (an unrelated option provider). The boundary guard is
   therefore a **no-new-reference** pin: the Security baseline is asserted exactly, `Management.Api`, `Core.Application` and
   `ConnectorResolution` reference none, project files are pinned too, and the seam's own types are scanned for any connector type.
2. **The SQL provider lives in `Apperture.ConnectorResolution`**, the SQL connector's home (there is no separate SQL connector package),
   and is registered by its own management-only extension `AddPlatformSqlStructureBrowser`, called by the Management Host; runtime
   workers do not compose an operator tool.
3. **The gate said Edit/Move/Retire were "already hidden by capability + state checks"; at the audited commit only Retire was.** SVC-5
   made it true and also hides Test for retired Connections.
4. **A browse request without a SQL login is refused.** A Connection with no User ID signs in with the platform host's own Windows
   identity; browsing it would let an operator enumerate the platform's own databases. It is refused before any client exists (the
   pre-existing verifier probe is unchanged).
5. **At most two structure requests run at once per Connection** (429 with `Retry-After`, nothing sent to the remote system).
6. **`CanBrowseConnections`** (server and client capability DTOs) was added so callers without the grant are never sent to an endpoint
   that must refuse them; it is true only together with `connection.read`.
7. **Audit for browse records the operation, not the selection**: the schema/table the operator chose is echoed remote content, so it
   is not recorded (the gate asked only for actor, connection, operation, outcome and counts).
8. **Direct loads of `/components?kind=…` make two requests** (unfiltered first, to learn the caller's visible kinds, then filtered), so
   an unseen kind can never influence a query. Implemented by parsing the address, not `[SupplyParameterFromQuery]`, so it is
   testable and follows in-app navigation with the subscription attached before any await and detached on disposal.
9. **A failed verification's toast severity** is now a warning (it used to read as success); a save/test *error* is a toast and no
   longer replaces the workspace.

## Security review record

Independent read-only reviews ran per plan; SVC-6 and SVC-7 were reviewed on **Opus** with a security brief. No Critical finding in
any plan.

| Plan | Finding | Resolution |
|---|---|---|
| SVC-2 | Rotation written to the secret store then failing validation left no record; detaching a secret ref not counted as a change | Failure path records `secret_rotated`/failed; ref add/remove/repoint counts (keys only); tests |
| SVC-3 | Definition fetch could fail the page (non-HTTP errors); guaranteed-403 request | Catch-all best-effort + skipped without `connector.definition.read` |
| SVC-4 | Location subscription attached after two awaits (missed events, leak past disposal) | Subscribed first, `_disposed` guard, single-flight sync loop; race and disposal tests |
| SVC-5 | Save/test errors replaced the workspace; stale actions after a failed reload; misleading "not supported" for unregistered connectors | Toasts; `Loaded` requires no error; distinct copy |
| SVC-6 (Opus) | No Critical/Important. Break-glass wording; real-evaluator and scoped-identity tests; bypassable arch guards | Wording; real-evaluator tests; scoped test; pinned csproj references, single-consumer and no-registry guards |
| SVC-7 (Opus) | **I1** no-UserId browses as the host identity; **I2** unbounded concurrency; **I3** transport bounds untested; M1 resolver timeout misreported; M2 provider `ArgumentException` skipped audit / echoed text; M5 node stuck on timeout; M6 mid-open cancel untested; M7 worker composed the tool | All fixed as ruling 2, 4 and 5 above plus tests (real `SqlConnection` bounds, loopback blackhole cancel, fixed 400 message, retryable nodes) |

### Whole-branch review (Opus, against both gates)

No Critical finding. Two Important findings were fixed: **(I-1)** a failed Connection row save left the rejected update tracked in the
context shared with the audit store, so the "failed rotation" record could commit an update the caller was told had failed
(`SqlConnectionRepository.UpdateAsync` now detaches the row on a failed save; test fails without the fix); **(I-2)** the metadata guard
(`RuntimeVisibleDevMetadataGuardTests`) now scans the workspace pages, the browser component and the presentation helper and forbids
`SVC-` labels. Minor items fixed: `(max)` column type label, audit wording for provider-declined requests, stale registration/summary/
supersession text in the architecture docs and ADR-0043, and §35 now records the audit-granularity and truncation-wording
interpretations. Not changed: a repeat visit to `/components?kind=connector` after manually changing the Kind filter is a no-op
(accepted).

### Request-forgery hardening (post-review stabilization)

The three structure requests were GETs although each logs in to an external system, consumes browse capacity and writes an audit
record, and the `SameSite=Lax` session cookie is sent on cross-site top-level GET navigations. They are now POSTs with a required
typed JSON body (`[Consumes("application/json")]`, `[FromBody]` request records); the client sends POST JSON and the operator's
schema and table travel in the body, not the URL. Only the local availability lookup remains a GET. **The platform has no
antiforgery-token mechanism to reuse** (no `AddAntiforgery`/`UseAntiforgery`/validation attribute anywhere), and none was invented:
the protection is the Lax cookie (not sent on cross-site POSTs) plus the JSON-only body, so a cross-site form, link or no-cors fetch
is refused with 415/400 by the framework before the action, the provider or the audit. Authorization is unchanged (`connection.read`
and `connection.browse` on every request). Limits: SameSite is enforced by the browser and cannot be exercised by the header-based
test host, so the integration tests prove the method and content-type contract, not the cookie; same-site (sibling-origin) attackers
are outside this protection, as for every other state-changing endpoint on the platform.

A focused independent review of this change (Opus) found no Critical or Important issue; its Minor items were applied (controller
remark wording, response disposal, a guard that only the tools controller can reach the tool-access service). It also observed, as
pre-existing and platform-wide, that the `UiDev` CORS policy (credentials allowed from `localhost:9001`) is registered in every
environment; that is unchanged and recorded below.

## Accepted residuals

- **The `UiDev` CORS policy is applied in every environment** (Management Host), allowing credentialed requests from
  `http(s)://localhost:9001`. Pre-existing, affects every endpoint equally, not changed here; restricting it to development or making
  the origins configurable is the follow-up.
- **Verification summaries carry remote-derived text** (a `SqlException` message can name a login or host; SFTP/Campus Nexus messages
  can name a URL). It is persisted on the Connection and visible to `connection.read` holders. Pre-existing and not secret material;
  only the secret-resolution failure text was normalised (SVC-5).
- **Failed-login lockout is not prevented.** Repeated browse (or test) attempts with a wrong stored password can lock a SQL login
  whose server enforces a lockout policy. Concurrency is bounded per Connection; there is no per-operator throttle or failure
  back-off.
- **`TrustServerCertificate` defaults to true** in the shared SQL factory so dev/Docker SQL works. Set it to false in production.
  Browse inherits the default.
- **Connectors labels in Configuration need `connector.definition.read`**; without it the stored keys are the labels.
- **The capability flag is instance-scoped**: a grant of `connection.browse` only at a narrower scope would be honoured by the server
  but the UI would not offer Tools.
- **404-before-403 on an unknown Connection** is an existence oracle identical to `GET /api/connections/{id}`; ids are random.
- **Break-glass sessions pass every check by design**, so they can browse. Upgrading grants `connection.browse` to the platform
  administrator role at the next start.
- **A tool provider set is built before authorization** (scoped, constructor-only; the secret resolver is already built on every
  connection API call for the verifiers).
- **The existing `/connections` list N+1 on bindings** is unchanged; nothing Services added worsens it.
- **Connection Recent Activity is not delivered** (audit query model has no Resource column/filter); the lifecycle and browse write
  gaps are closed, retrieval is future audit-query work.
- **The implementer performed no browser smoke**; the operator smoke and the automated-only items are recorded under Browser smoke below.

## Validation

At the request-forgery hardening commit (re-validated in full; the run at `296b52f` had 2786 UnitTests and 259 IntegrationTests):

- `dotnet build Apperture.slnx` — succeeded, 0 errors (pre-existing warnings only), isolated `--artifacts-path`, single node.
- `Apperture.UnitTests` — **2814 passed**, 0 failed, 0 skipped.
- `Apperture.ArchTests` — **38 passed**, 0 failed, 0 skipped.
- `Apperture.Management.Ui.Shared.Tests` — **1348 passed**, 0 failed, 0 skipped.
- `Apperture.Management.Ui.Client.Tests` — **193 passed**, 0 failed, 0 skipped.
- `Apperture.IntegrationTests` — **262 passed**, 0 failed, 0 skipped, in 8 min 5 s.

**SQL was genuinely exercised**, independent of "Skipped: 0" (integration tests can soft-skip): the new suites use hard-failing
fixtures (`RequireFixtureAsync` / `SqlAvailable` assertions), so they cannot pass without SQL Server; the run took a realistic
six minutes; and the test databases' last-write markers advanced across the run (`Apperture_IntegrationTests` last user update
`22:53:27` → `23:49:48`; `Apperture_ManagementGovernanceTests` `23:34:18` → `23:43:04`; server UTC `23:50:09`, all 2026-09-24, re-measured after the request-forgery change), read
through the container with credentials taken from the environment, never placed on a command line. New real-SQL and real-host
evidence: `ConnectionLifecycleAuditSqlServerTests` (2), `PlatformSqlStructureBrowserSqlServerTests` (6),
`ManagementHostConnectionToolsHttpTests` (13, including the request-forgery contract: GET 405, non-JSON POST 415/400, no audit) — including a real 403 matrix, real audit rows, a real login refusal
(`RemoteDenied`), a real closed-port outage (`RemoteUnavailable`), a hostile schema that changes nothing, and composition of exactly
one provider in the real host.

## Browser smoke

### Operator smoke record

I (the implementer) performed no browser smoke: no browser automation was available against a running stack built from this code.
The **operator then exercised the feature by hand** on a stack running this branch, and reported:

- SQL structure browsing worked end to end — schemas, tables and columns loaded.
- The browser requests were confirmed, in the browser's network view, to be **POST with a JSON body**.
- Good and bad Connection outcomes produced the expected UI (successful browse; the fixed refusal message on a bad login).
- Successful and failed browse attempts each produced an audit record.
- The UI appearance was considered **acceptable for the current pre-overhaul stage**; it is not the intended final browsing
  experience.

**Covered by automated tests only** (not manually smoked, and not an incomplete smoke):

| Behaviour | Why it was not exercised by hand | Automated coverage |
|---|---|---|
| A third concurrent structure request returns **429** | The shipped UI naturally prevents multiple simultaneous browse submissions | `ConnectionToolInvocationTests` (per-Connection limiter; the controller answers 429 with `Retry-After`), `ManagementClientSqlStructureTests` and `SqlStructureBrowserRenderTests` (client and UI handling of 429) |
| A principal holding only `connection.read` is refused (403) | The product has no custom-role creation interface to make such a principal | `ConnectionBrowsePermissionTests` (real RBAC evaluator), `ManagementHostConnectionToolsHttpTests` (real host 403 matrix) |
| A principal holding only `connection.browse` is refused (403) | Same | Same |

Those automated tests are unchanged and must not be weakened.

Before the operator smoke, the page and tool markup had been verified by rendering the **real** Blazor components
(`ConnectionDetail`, `ConnectionsPage`, `ComponentsList`, `SqlStructureBrowser`) through Blazor's own renderer with mocked clients
and asserting on the produced HTML and on client-call logs (`ConnectionWorkspaceRenderTests`, `CatalogNavigationRenderTests`,
`SqlStructureBrowserRenderTests`).

### Operator checklist (record of what was asked for)

**Connections list** — Connectors tab is gone; "Browse the connector catalog" opens `/components` already filtered to Connector.

**Workspace** — Connector shown by name and linking to its Component; raw ids only under *Technical details*; friendly
labels and descriptions; secrets only *Set*/*Not set*; Health shows status, last tested time and result; *Used in* unchanged;
ownership chip, mismatch notice and Move unchanged; a retired Connection shows the notice and no Test/Edit/Move/Retire.

**Fallbacks** — a Connection whose connector is not registered shows the notice and still loads; a failed test leaves the page
intact (warning toast); a connector with no provider shows no Tools section.

**SQL browse** (operator with `connection.read` **and** `connection.browse`, a SQL-login Connection) — Tools appears; *Browse
structure* loads schemas; expanding loads tables/views; expanding a table loads columns; there is no data view and no SQL box; a
wrong password shows the fixed refusal message with *Try again*; collapsing/expanding repeats no request.

**Authorization** — an operator with only `connection.read` sees no Tools section and gets 403 from the tool endpoints (automated
end to end); with both grants can browse.

**Appearance** — dark, light, narrow width; no overflow in the configuration grid or the column table.

## Future roadmap readiness

The navigation / cross-context organisational-grouping body has **not** started. Nothing here adds a department, domain, grouping or
organisational-area field, a Context-specific route, or a cross-context operation; `/connections` and `/connections/{id}` remain
stable identity for that later body to link to.
