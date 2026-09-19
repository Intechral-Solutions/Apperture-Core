# Components / Resource Catalog / Services — approved boundary rulings

Date: 2026-09-18 (post-CTX roadmap reset)
Status: **Approved product/roadmap rulings, not yet a design gate.** This is a short planning note
recording the conceptual boundary future design work must start from — it is deliberately not a full ADR
and does not specify implementation. The Components design gate and the Services design gate (see
[`pre-release-product-roadmap.md`](../../../00-overview/pre-release-product-roadmap.md)'s near-term
execution order) will each produce their own ADR when they run.

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
  by an installed Module.
- Internally, do **not** use a bare, ambiguous `Component` class. Follow the codebase's existing
  `*Definition`/`*Descriptor` naming convention — the recommended shape is `ComponentDefinition` (or a more
  qualified variant), paired with an `I*Registry`/`I*Contributor` pair mirroring the existing
  `ConnectorDefinition`/`IConnectorDefinitionRegistry`/`IConnectorDefinitionContributor` triad. A bare
  `Component` type would collide with Blazor's own `ComponentBase`/"Razor component" vocabulary in the UI
  project specifically.

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

Initial design hypothesis (subject to the Services design gate, not final):

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

## Sequencing

**Components design gate → Services design gate → Components implementation → Services implementation.**

The two design gates run back-to-back, before either implementation begins, because Services may reveal
which capability/catalog abstractions Components actually needs (in particular, the live-browse capability
Resource Catalog once envisioned lands in Services, and the Components catalog needs to know that before it
locks its own shape). Do not ship the entire Component catalog before the Services contract is decided.

## What this note deliberately does not decide

Left to the Components and Services design gates themselves: exact `ComponentDefinition` schema, exact
Component catalog UI, exact Service permission verbs, whether Service cardinality could ever exceed
one-per-Connection, and any implementation detail not stated above as a ruling.
