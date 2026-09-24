# Components / Resource Catalog / Services — approved boundary rulings

Date: 2026-09-18 (post-CTX roadmap reset)
Status: **Approved product/roadmap rulings, not yet a design gate.** This is a short planning note
recording the conceptual boundary future design work must start from — it is deliberately not a full ADR
and does not specify implementation. The Components design gate and the Services design gate (see
[`pre-release-product-roadmap.md`](../../../00-overview/pre-release-product-roadmap.md)'s near-term
execution order) will each produce their own ADR when they run.

> **Superseded in part (2026-09-23): both design gates have now run and are approved.**
> See [`services-design-gate.md`](./services-design-gate.md). Where this note and that gate differ, **the gate
> wins** — it is grounded in a code audit at `2fa0857`. The Services hypothesis this note recorded is **confirmed
> in substance and sharpened in one respect**: a Service is not merely "derived initially", it is **not an object
> at all**. It is the evolved operator workspace for one Connection, rendered at `/connections/{id}`. There is no
> Service entity, no Service GUID, no `/services` route and no `/services` list; identity is `ConnectionId`;
> cardinality is 1:1 by construction. Integrations continue Binding directly to Connections, and HSK-D continues
> listing one Connection Resource rather than a second Service row. Services v1 scope (**S-B**), the
> `connection.browse` authorization ruling, the Connectors-tab retirement timing and the terminology ruling are
> recorded as decided in that gate's §32.
>
> **Superseded in part (2026-09-22): the Components design gate has run and is approved.**
> See [`components-design-gate.md`](./components-design-gate.md). Where this note and the gate differ, **the
> gate wins** — it is grounded in a code audit at `f3ec369`, this note in pre-audit planning. The gate's §1
> records the one deliberate departure: this note's suggested `ComponentDefinition` +
> `I*Registry`/`I*Contributor` triad was **not** adopted, because the audit found both v1 families already have
> authoritative registries and a Component record would own nothing. The Components model is a **read-time
> projection** (gate §7). This note's other rulings — Origin-based eligibility, the retired Resource Catalog,
> specialized registries staying specialized, the non-1:1 Resource relationship, Services' boundary, the
> sequencing, and the "never a bare `Component` type" naming constraint — are all **carried forward unchanged**.
> *(At the time of this block the Services design gate had not yet run; it has since — see the block above.)*
> Everything this note says about Services was then a starting hypothesis, supplemented by the Components gate's
> §23 handoff.

**Not a target architecture and not implementation-ready.** Like
[`application-contexts/current-inventory.md`](../application-contexts/current-inventory.md) before it, this
records rulings gathered so the later design gates can start from decided ground rather than re-litigating
it. Runtime source of truth remains the code.

---

## Why this exists

A discovery pass (2026-09-18) audited the current Module/Connector/NotificationDefinition/Connection
architecture, the historical intent behind "Resource Catalog" (ADR-0040, never implemented), and whether any
"Service" concept already exists (it doesn't — confirmed greenfield). The findings required correcting one
part of the original product hypothesis and clarifying a boundary question before Components or Services
work can safely start. This note records the resulting rulings.

## Components

**"Components" is the approved operator-facing product term.**

- **Module** = the package/install/distribution boundary. Keeps its existing, real (if shallow) install/
  enable lifecycle (`ModuleInstallationStateEntity`).
- **Component** = a reusable, operator-visible capability supplied either by the System/platform itself or
  by a Module. *(Wording corrected 2026-09-22: "by an installed Module" was misleading — catalog membership
  does not depend on Module install/enable state. See gate §6 and §17.)*
- Internally, do **not** use a bare, ambiguous `Component` class. Follow the codebase's existing
  `*Definition`/`*Descriptor` naming convention — the recommended shape is `ComponentDefinition` (or a more
  qualified variant), paired with an `I*Registry`/`I*Contributor` pair mirroring the existing
  `ConnectorDefinition`/`IConnectorDefinitionRegistry`/`IConnectorDefinitionContributor` triad. A bare
  `Component` type would collide with Blazor's own `ComponentBase`/"Razor component" vocabulary in the UI
  project specifically.
  **Gate outcome (2026-09-22):** the `ComponentDefinition`/registry/contributor triad was **not** adopted —
  the audit showed both v1 families already have authoritative registries, so it would have been the universal
  registry this same note forbids below. The naming constraint itself holds in full: the gate's types are
  `ComponentSummary`, `ComponentDetail`, `ComponentKey`, `ComponentKinds`, `ComponentOrigins`,
  `IComponentCatalog`, `IComponentCatalogProvider` — no bare `Component`. See gate §1 and §7.

### Components v1 scope

Do not invent every hypothetical Component kind immediately. The strongest real initial kinds, backed by
existing descriptor/provenance models:

- **Connector** — already has a stable descriptor (`ConnectorDefinition`) and a real contributor→registry
  pattern, though no persisted table (the SQL table was dropped in Epic 53; it's host-seeded in memory).
- **NotificationDefinition** — the strongest existing precedent. Its `(Origin, Scope, ModuleId, SourceKind,
  Mutability)` tuple already cleanly distinguishes System built-in from Module-contributed from
  user-created. **Component eligibility follows `Origin`, not the presence of the model:**
  - `Origin = PlatformBuiltIn` — may appear as a **System Component**.
  - `Origin = ModuleBuiltIn` — may appear as a **Module-contributed Component**.
  - `Origin = UserCreated` — **not** a Component catalog entry. A user-created NotificationDefinition is
    operator-authored configuration/content, not a reusable platform capability, even though it shares the
    same underlying `NotificationDefinition` model. Sharing a model does not imply sharing catalog
    membership.

  The Component catalog may adapt the existing `NotificationDefinition` model as an input, but eligibility
  must respect this `Origin` boundary. The exact catalog adapter/schema is left to the Components design
  gate — this note only fixes the eligibility rule, not the mechanism.

The catalog architecture should be **extensible** to future kinds, but v1 ships with these two.

**Client/Browser**: the product idea (an operator-facing capability to browse/query an external system
through a Connection) remains valid, but the old ADR-0026 `ClientDefinition` model was deliberately removed
(ADR-0027, Epic 52/53) — zero code references remain anywhere in the repository. **Do not resurrect it, and
do not require it as a Components-v1 kind.** If a browsing/query capability is wanted, it is designed from
scratch during the **Services** design gate (see below) and may later be supplied *by* a Component, but it
is not itself a Components-v1 deliverable.

**Custom Metrics**: defer. No real product-level metric model exists today (only internal OTel host
instrumentation and a no-op module telemetry counter). Do not create a Metric Component kind merely to
populate the taxonomy — keep the catalog extensible enough to add one later, when a real requirement
appears.

### Module detail

Modules become subordinate within the Components area eventually. Module detail should ultimately show:
metadata; installation/version/state; Components contributed; Integrations using the Module. The near-term
housekeeping item ("Integrations using this Module," HSK-A) is **not throwaway work** for this — it should
use a reusable API/query (extending `IIntegrationRepository.GetVisibleAsync` with an optional `moduleId`
filter, the same shape as the existing `resourceContextId` filter), since that query is a plain
property-equality filter on `Integration`, unrelated to whatever Module→Component renaming eventually
happens to the registry/lifecycle layer.

**Ruling (post-HSK-A): the "Integrations using this Module" section is intentionally global with respect
to the shell View/Context, not View-scoped.** It lists every Integration using the Module that the current
operator is authorized to see, across all ResourceContexts — the shell View filter is deliberately not
applied. Reasoning: Module is a system/catalog/distribution concept, not an Application-owned resource; the
section answers "where is this Module in use," which the active View would make incomplete; and Modules are
expected to live under the global Components area eventually, reinforcing this. Authorization/visibility
still applies normally — "global" means unfiltered by View, not unfiltered by access control. Carry this
forward as-is when Module detail is reshaped during Components implementation.

## Resource Catalog — retired as a standalone product/roadmap concept

Historical ADR/docs are preserved as history; nothing is deleted or rewritten to look obvious in hindsight.
Going forward, its two historical responsibilities split:

- **Metadata/discoverability side → Components.** Components receives the catalog/discoverability
  responsibility needed to expose reusable, operator-visible capabilities. **Important:** do not physically
  collapse every existing registry into one universal registry just because Resource Catalog once
  envisioned registry unification. Permission descriptors (`IResourceDescriptorRegistry`), setting
  definitions (`IPlatformSettingDefinitionRegistry`/`IIntegrationConfigurationRegistry`), connector
  definitions (`IConnectorDefinitionRegistry`), etc. may remain specialized registries. Components adapts
  and aggregates the relevant *operator-visible* definitions behind a clean catalog abstraction — not every
  discoverable descriptor automatically becomes a Component (e.g. auth permission descriptors stay an
  authorization concept, not a Component).
- **Live browse side → Services.** Browsing/querying live sub-resources behind a configured Connection (SQL
  tables, folders, remote entities, browse/query/operation surfaces) is operational/runtime behavior over a
  configured Connection — it belongs to Services, not the Component catalog.

**Config Inventory remains separate from both.** It stays a diagnostic/config explorer — read-model
aggregation over existing list services, never domain ownership. It is not Components and not a Resource
Catalog, per the durable architectural rule already established (ADR-0040 rule 6, "UI aggregation is not
domain ownership") and repeatedly reaffirmed across every CTX/HUB/NAV closure doc.

## Services

**Services are not Components.** A Service is a **derived, operator-facing experience over one configured
Connection**, plus whatever reusable capabilities Components provide — structurally distinct from
ADR-0034's "System" concept (a grouping layer *above* many Integrations). Service lives conceptually
*within* the existing Connection/Integration operational domain, not as a new layer physically inserted
between them: it is a presentation/experience over a Connection, not a persistence or execution hop that
requests pass through.

**Gate outcome (2026-09-23):** every bullet below was **confirmed** by the Services design gate, and one was
sharpened. Service is not "derived initially" — it is **not a separate object at any point**. The workspace *is*
`/connections/{id}`; identity *is* `ConnectionId`; cardinality is 1:1 by construction rather than by policy. The
gate additionally decided what this note left open: **Services v1 scope is S-B** (Connection-workspace foundation +
provider/tool seam + a SQL **structure**-browser exemplar, explicitly **not** a query console); **`connection.browse`**
is the one new action; the Connectors tab retires in Services (SVC-4); and **"Service" does not become an
operator-facing noun**. See [`services-design-gate.md`](./services-design-gate.md) §32.

Initial design hypothesis (recorded pre-gate; confirmed by the gate except where noted above):

- One Service experience per Connection.
- One Service may expose multiple capabilities/tools (basic connection info/settings, health, diagnostics,
  browsing, querying, operational actions — e.g. a future SQL Server browser/query experience belongs here).
- **Service is derived initially, not a separately persisted aggregate.** No independent Service lifecycle,
  no independent ResourceContext ownership initially. The rule is simply that a derived Service's effective
  ownership/security context is read from its backing Connection at the point of use — there is no separate
  `Service.ResourceContextId` column to keep in sync, because there is no separate Service row.
- **Integrations continue Binding directly to Connections, exactly as today.** Service is not inserted as a
  mandatory intermediary in that relationship — it is an additional operator-facing view over a Connection
  that already exists, not a new hop `Integration → Service → Connection` that execution or persistence
  must pass through.
- If a future concrete requirement demands independent Service lifecycle, identity, or permissions, *that*
  is the forcing function for giving Service its own persisted table — not a default assumption now.

## Components and Resources (hypothesis, with HSK-D evidence)

**Approved hypothesis (not a ruling that fixes a schema):** many operational Resources are concrete
manifestations of, or are enabled/enhanced by, Components. This is not a one-to-one invariant. Components may
produce, enable, or enhance Resources; some Components may have no persisted Resource, and some Resources may
have no direct originating Component. **No universal Component-to-Resource foreign key or generic
resource-operation framework is approved** — no `Resource.ComponentId`, no `IComponentResource`, no
`Capabilities[]`/`Actions[]` framework, no generic Move/Delete/Retire command surface. HSK-D contributes
evidence; the Components design gate will formalize the relationship.

Evidence gathered while building the Application Resources surface (2026-09-21), from code as it stands:

- **Connection — the strongest relationship.** A Connection carries a `ConnectorDefinitionId` and creation is
  validated against a `ConnectorDefinition` from `IConnectorDefinitionRegistry` (platform- or module-provided,
  with `ConnectionFields` authoring metadata and optional `ModuleId`). The Connection itself holds only the
  instance data (`Config`, `SecretRefs`, state, ownership). It is reasonably described as a *configured
  manifestation of a Connector capability* — the clearest candidate for "a Component produces a Resource".
- **Integration — an instance of a Module, not of a Component.** An Integration carries a `ModuleId` and is
  documented as an "operator-facing configured instance of a module"; it owns settings and Bindings. It
  *consumes* connector capability indirectly: a `Binding` fills a Module-declared slot with a Connection (or a
  `CapabilityId`), checked for connector/contract compatibility by `BindingEligibilityService`. Nothing catalog-like
  is instantiated by an Integration today; whether the Module's integration surface becomes a Component is a
  design-gate question. Here the relationship looks like "enabled/enhanced by", not "instance of".
- **Schedule — subordinate operational configuration.** A Schedule has recurrence/time zone and a
  `ScheduledTarget` (`ScheduledJobType`: workflow execution, module operation, connector sync/polling, and
  others), an optional `IntegrationId` (standalone Schedules are legal), and its own direct
  `ResourceContextId` (an Integration-attached Schedule must match its Integration's Context). No Component
  origin exists; it configures *when* something runs. The most likely outcome is "Resource with no direct
  originating Component".
- **A Resource can be created without any Component-side involvement**, and the Component-side pieces
  (`ConnectorDefinition`, Modules) are not persisted rows the Resource could reference (Connector definitions
  are host-seeded in memory). A foreign key would therefore have nothing to point at today.
- **Ownership is orthogonal.** ResourceContext ownership (`Shared` / Application) applies to the Resource and is
  independent of where its originating definition came from.

Repeated patterns noticed but deliberately not generalized: each family exposes its own list/detail/state
vocabulary (Active/Inactive/Retired, Active/Retired, Enabled/Disabled) and its own move/retire operations. The
Application Resources view lists them without normalizing state or adding generic actions.

## Sequencing

**Components design gate → Services design gate → Components implementation → Services implementation.**

The two design gates run back-to-back, before either implementation begins, because Services may reveal
which capability/catalog abstractions Components actually needs (in particular, the live-browse capability
Resource Catalog once envisioned lands in Services, and the Components catalog needs to know that before it
locks its own shape). Do not ship the entire Component catalog before the Services contract is decided.

**Status (2026-09-23): both design gates are complete and approved.**
Components design gate — [`components-design-gate.md`](./components-design-gate.md).
Services design gate — [`services-design-gate.md`](./services-design-gate.md).
**Components implementation (CMP-1…CMP-6) is complete ([closure](../../../audits/components-catalog-closure.md),
[ADR-0043](../../../02-decisions/ADR-0043-component-catalog-read-model.md)).** **Services
implementation (SVC-1…SVC-8) is also complete (2026-09-24; [closure](../../../audits/services-connection-workspace-closure.md),
[ADR-0044](../../../02-decisions/ADR-0044-connection-workspace-and-browse-authorization.md))**: the Connection workspace evolved
in place, `connection.browse` and a SQL structure browser were added, and the Connectors tab was retired. The Services gate confirmed
that it requires **no architectural amendment to Components**; the only Components-side follow-ups are a factual
CMP-3 deep-link correction and the Connectors-tab retirement timing, both recorded in that gate's §31.

## What this note deliberately does not decide

Left to the Components and Services design gates themselves: exact Component catalog schema and UI, exact
Service permission verbs, whether Service cardinality could ever exceed one-per-Connection, and any
implementation detail not stated above as a ruling.

**All of these are now decided** (2026-09-23). Component catalog schema and UI: Components gate §§7–18. Service
permission verbs: **`connection.browse` only**, Services gate §11.3 — with the durable rule that no `service.read`
exists in the derived Connection-workspace architecture, and that a Service-specific permission would require a
future, independently justified Service authorization boundary. Cardinality: **1:1 by construction**, Services
gate §8.

**Now decided by the Components gate** (see it, not this note, for detail): the projection-vs-persistence
model (§7), Component identity `(Kind, NativeId)` (§8), kind/origin taxonomy (§9), catalog API (§10),
authorization — **no new permission** (§11), detail UX (§12), usage relationships (§13), Module detail
evolution (§14), System Component model (§15), configuration (§16), lifecycle/provenance (§17), navigation
placement `Config → System → Components` (§18), and the Services handoff (§23). Product rulings **D1–D4** are
recorded as decided in gate §24.
