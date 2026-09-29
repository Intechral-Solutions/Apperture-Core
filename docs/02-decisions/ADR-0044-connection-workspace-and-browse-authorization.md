# ADR-0044: The Connection workspace, the "Service" language ruling, and `connection.browse`

## Status

Accepted (delivered 2026-09-24). Design and rulings: the
[Services design gate](../01-architecture/transitions/components-services-catalog/services-design-gate.md)
(approved 2026-09-23, rulings D-S1…D-S4). Implementation account and evidence:
[`services-connection-workspace-closure.md`](../audits/services-connection-workspace-closure.md).

**Addendum (SEC-1, 2026-09-29).** The request-forgery paragraph under *The SQL structure browser* records the measure these
endpoints shipped with: POST, a required JSON body and the `SameSite=Lax` cookie. That measure is **superseded as a
pattern** by the platform-wide browser request trust in
[ADR-0045](ADR-0045-browser-session-recovery-and-request-trust.md): every unsafe `/api` request, these included, now needs
the exact configured UI `Origin` and the platform request header, and explicit cross-site Fetch Metadata is refused. The
endpoints keep POST and the JSON contract; new endpoints must not copy the per-endpoint measure as their protection.

## Context

The roadmap called for a "Service" capability: an operator experience over one configured Connection (settings,
health, where it is used, and — for some connectors — tools for inspecting the external system). A design audit found
that nothing requires a second object. A Connection already has identity, lifecycle, ownership, configuration,
authorization and references; its detail page already carried four of the seven capabilities the Service idea listed.
Introducing a `Service` entity would have created a second identity to keep in sync with one row, and would have
collided with the 245 existing `*Service` application-service type names and with the BCL `IServiceProvider`.

Separately, the platform could already create a remote client for a Connection from the Management Host, but exposed
no operator surface for doing anything beyond a fixed connectivity probe. A tool that reads remote structure is a
new disclosure, and the audit trail for the Connection's own lifecycle had a pre-existing gap.

## Decision

### "Service" is architectural language, never a product noun or a type

The evolved operator workspace for one Connection **is** `/connections/{id}`. There is no Service entity, table, id,
`ResourceContextId`, lifecycle, route (`/services`, `/services/{id}`), list, permission (`service.read`,
`service.manage`, `service.browse`) or type (`Service`, `ServiceDefinition`, `IServiceExperience*`). Operators work with
**Connections**, the **Connection detail/workspace** and **connector-specific tools**. Identity is `ConnectionId`;
cardinality is 1:1 by construction because there is no second object. Reversibility is cheap: if a persisted Service is
ever independently justified, `ConnectionId` becomes its key and nothing else changes.

The rejected alternative — a derived-but-named `Service` type or route "for the tools" — would have forked the detail
page, orphaned the links that Config Inventory, the Application Resources explorer and the Integration hub already
emit, and pre-empted the later navigation decision.

### Ownership is the Connection's, and is not authorization

The workspace reads `Connection.ResourceContextId` at the point of use. No independent Context field exists, so moving
a Connection moves its workspace with no synchronisation step. **ResourceContext is not an authorization boundary**:
`connection.read` is evaluated at instance scope and a Context is a view and ownership concept. Nothing here treats it
otherwise. No department, domain, grouping or organisational-area field is added; a future cross-context organisational
lens remains a separate, deferred decision.

### The workspace

One page, in this order: header (name, connector, state, ownership, mismatch notice, actions); **Overview** with a
legible point-in-time **health** statement; **Configuration** rendered from connector-declared field names and
descriptions, with secret-backed fields reduced to *Set* / *Not set* (neither the value nor the reference is ever
rendered); **Used in**; **Tools** only when both a provider is registered in the host and the caller holds
`connection.browse`; and **TechnicalDetails** for identifiers. The connector is shown by display name, labelled
**Connector** (not "Provided by", which is reserved for Module provenance), linked to its Component page, and
degrades to the stored id with an explicit notice when the host does not register it. Verification is a point-in-time
fact and gates nothing; a retired Connection is read-only; an unregistered connector can be moved or retired but not
tested or edited.

The Connections list now lists Connections only. The duplicate Connectors tab is retired; discovery is the
**Components** catalog (`/components?kind=connector`), which now honours that query parameter, plus the Connector link
on each workspace.

### Authorization: one new action, additive

`connection.browse` on `Connection` at instance scope, in the Connector family. It means *inspect, read-only, the
structure of the external system this Connection points at*. It is **additive to `connection.read`** — a caller needs
both — and is never implied by `connection.read`, `connection.update` or any other grant. It is held by the platform
administrator role (added through the startup repair set) and, like every permission, by a break-glass session;
every other role needs an explicit grant. Rejected: reusing `connection.read` (test-connect is bounded because it is a
fixed probe returning no remote data; browsing is not), `connection.update` (the wrong axis), and the reserved
`connector.manage` (scoped to definitions). No `service.*`, generic tool, or SQL-execution permission exists. Reading
remote **rows or content** would be a different disclosure needing its own decision and action.

### The tool seam, minimally

`IConnectionToolProvider` (connector id plus a local, static availability) with typed capability interfaces
(`ISqlStructureBrowser`), registered from the connector package's own extension and found by a keyed match over the
registered set inside `ConnectionToolAccess`, the single authorized entry point. There is no registry class, contributor
interface, startup sanity service, generic capability list, `Execute(string)` or JSON command surface, and nothing
executable on `ConnectorDefinition` or `IConnectorDefinitionContributor`. Answering "which tools?" is local-only and
depends on no client factory, resolver or secret resolver. A refusal is identical whether or not a connector has a
tool, and no provider is consulted before both grants are held. Concrete connector assemblies are never referenced
from the control plane or the management API for this seam.

### The SQL structure browser

Structure only: schemas, then tables and views, then columns. The platform issues a **closed set of three fixed
`INFORMATION_SCHEMA` statements**; every operator-supplied selection reaches the server only as a bound
`DbParameter` **value**. There is no dynamic SQL — no concatenation, interpolation, `QUOTENAME`, `sp_executesql` or
identifier insertion — no user-authored SQL, no row or content read, no DML or DDL and no batch. If a future metadata
operation appears to need an identifier inside SQL text, that is a design finding to raise, not a reason to widen the
mechanism. Every request has an explicit connect and command timeout, an item cap with an honest truncation notice,
cancellation threaded through open, execute and read, and a client created inside the operator-invoked request and
disposed on every path; the platform pools and caches nothing. The remote login's own rights are the boundary of what
the tool can show, so a Connection with **no SQL login** (which would sign in as the platform host's own identity) is
refused before any client exists, and at most two requests run at once per Connection (a third is refused with 429 and
nothing is sent to the remote system). The remote system's own refusal or outage is an outcome with a fixed message,
never a platform error, and remote error text, connection strings and secrets never reach the UI, audit or logs. Only
the Management Host composes the browser; runtime workers do not. Because each structure request logs in to an external
system, consumes browse capacity and writes an audit record, the three requests are **POSTs with a required typed JSON
body**, never GETs that a cross-site navigation could trigger; only the local availability lookup stays a GET. The
platform has no antiforgery-token middleware, and none was added: the protection is the `SameSite=Lax` session cookie
(withheld from cross-site POSTs) together with the JSON-only body contract (`[Consumes("application/json")]` returns 415
and an absent body returns 400, both before the action runs, so a plain cross-site form cannot reach the provider or the
audit). A platform-wide antiforgery layer would be a separate decision. **Arbitrary SQL, a query console and row preview remain outside this decision** and would each need
their own security and design gate.

### Audit

The pre-existing gap is closed: Connection create, update, secret rotation, retire and test-connect each append one
record (action names in `ConnectionAuditActionNames`), with `Resource` deliberately `null` (the audit store appends a
non-null `Resource` to `Details` as plain text, corrupting JSON, and never persists it) and connection identity in
structured JSON `Details`. No secret value, secret reference, config value or remote text is ever recorded; a rotation
that fails after the secret was written, and a verifier that throws, are recorded as failed. Each **remote browse request**
appends exactly one record with counts and outcome and **never** the names or content returned or the selection made,
and is fail-closed: if the record cannot be written the result is withheld. Opening the workspace, expanding a
section or answering "which tools?" writes nothing; the browser fetches each tree node once so navigation does not
multiply requests or records.

### Not decided here

A **Connection Recent Activity** panel is not delivered: the audit query model has no Resource column and no resource
filter, so Connection-scoped retrieval is not efficiently supportable, and closing the write gap does not change that.
Capability advertisement to the Components catalog, SFTP or Campus Nexus tools, a provider registry (which needs a second
provider), and the navigation/grouping redesign are deferred.

## Consequences

- One Connection page serves every existing link unchanged; HSK-D still lists one Resource row per Connection.
- Binding persistence and runtime resolution are untouched; a Service (workspace or tool) is never an execution
  dependency.
- Operators need an explicit `connection.browse` grant to see or use the tool; upgrading grants it to the platform
  administrator role at the next start.
- The tool is deliberately incapable of being extended into a query console without new code that fails the pinned
  tests (`SqlStructureQueries.All` is asserted literally).
- Known residuals are recorded in the closure audit, including the caller-visible remote-derived text in verification
  summaries, the absence of a per-operator throttle or failed-login back-off on browse (concurrency is bounded per
  Connection only), the shared SQL factory's `TrustServerCertificate` default of true, and that labels for Configuration
  require `connector.definition.read`.
