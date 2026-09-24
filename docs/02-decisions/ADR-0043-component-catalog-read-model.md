# ADR-0043: Component catalog is a read-model projection

## Status

Accepted (delivered 2026-09-23). Design and rulings: the
[Components design gate](../01-architecture/transitions/components-services-catalog/components-design-gate.md)
(approved 2026-09-22) and its confirmation by the
[Services design gate](../01-architecture/transitions/components-services-catalog/services-design-gate.md)
(2026-09-23). Implementation account and evidence:
[`components-catalog-closure.md`](../audits/components-catalog-closure.md).

> **Update (2026-09-24):** the Connection workspace work this ADR calls "not started" has since been delivered, and the `/connections`
> Connectors tab it says is retained has been retired in favour of `/components?kind=connector`. See
> [ADR-0044](./ADR-0044-connection-workspace-and-browse-authorization.md). The catalog decision itself is unchanged.

## Context

The platform supplies operator-visible capabilities from several places that each already have an authoritative
home: Connector definitions live in an in-memory registry populated at host start, and Notification Definitions
live in SQL. Operators had no single place to discover what the platform and its Modules supply, and the only
connector list was a tab inside `/connections`. The obvious remedy — a `Components` table or a universal registry
— would have created a third copy of data neither authoritative system would cede ownership of.

## Decision

### A Component is a read-model projection

A **Component** is a curated, operator-visible platform capability supplied either by the platform itself or by a
Module. It is a projection over the specialized family services. There is **no** Components table, persisted
entity, migration, Component GUID, universal foreign key, or universal registry. `Connector` and supplied
`NotificationDefinition` remain owned by their own registries/stores and their own authorized read services.

v1 covers exactly two families:

- **Connector** — every registered connector definition.
- **Supplied Notification Definition** — Notification Definitions whose `Origin` is `platformBuiltIn` or
  `moduleBuiltIn`. Eligibility is an **Origin** predicate, never a Scope, id-shape or Mutability inference, and
  operator-authored (`userCreated`) definitions are not Components. The store gained an additive `Origins` set
  filter so both origins are fetched in one query rather than by misusing `SystemScopedOnly`.

Workflow, operation, procedure and metric families are deferred. Services are not Components.

### Identity and origin

Identity is `ComponentKey(Kind, NativeId)`: a string kind token (`connector`, `notification-definition`) plus the
family's own readable native id, used verbatim and routed as `/components/{kind}/{nativeId}`. No new identifier.
Origin has exactly two values and one derivation, `SupplyingModuleId is null ? system : module`; there is no
System Module and no `user` origin. For Notification Definitions the derived origin is cross-checked against the
stored `Origin`, and disagreement surfaces as an error instead of being coerced.

### Authorization is reused, never aggregated

There is no `component.read` or `component.manage`. Each provider depends only on its family's **authorized** read
service (`IConnectorDefinitionCatalogReadService`, `INotificationDefinitionService`), never on the registry or
store. A family the caller cannot read is omitted whole: its rows, its entry in the available-kind filter and its
share of `TotalMatching`. Only when every consulted family is denied does the list answer 403. Detail answers 404
— never 403 — for an unknown, unreadable or forbidden Component. A Module's display name, version and state are
resolved on detail only and only for a caller holding `module.configure`; the bare supplying-module id may appear
because the family DTOs already expose it.

### Performance guards

The list never calls the module registry (its descriptor path builds every module descriptor twice), issues at
most one Notification Definition query, performs no per-Component lookup and carries no usage counts. Scale is a
handful of entries, so there is no server paging, cache or search index; the cap is bounded and `TotalMatching` is
honest.

### Detail is a delegating wrapper

Component detail shows read-only family facts, module attribution where authorized, current usage and a link to
the authoritative surface. It contains no authoring or mutation control; the controller has only `GET` actions.
Built-in Notification Definition defaults (`DefaultSeverity`, `ToastEnabledByDefault`) are managed on the
Notification Definition page and are not duplicated. No capability advertisement exists in v1.

### Usage is current, attributable and detail-only

- **Connector → Connections**: the **Active** Connections made from the connector, through
  `IConnectionManagementService`, so normal Connection visibility applies. A retired Connection is decommissioned
  configuration excluded from binding eligibility, so it is not current usage (the ownership summary already treats
  only Active Connections as live).
- **Notification Definition → subscriptions**: the current subscription rows, through the authorized subscription
  service. Notification instances (history), per-user preferences and Integration ownership are not usage.
- A caller who cannot read the usage source gets **no usage block**, never a zero.

### Resources and Services

There is no `Resource.ComponentId`, no `IComponentResource`, no one-Component-per-Resource rule and no generic
capabilities/actions framework. The Connector → Connection relationship is derived from
`Connection.ConnectorDefinitionId`. Service work (the Connection workspace) is a separate body and is not started.

## Navigation

`Config → System → Components → { Catalog, Modules }`. No rail domain is added. The structural `Components` parent
carries **no** capability: a gated parent would drop its whole subtree and hide an authorized `Modules` child from
an operator who can manage modules but read neither family. `Catalog` is gated by the derived `ViewComponents`
menu token (connector-definition read OR notification-definition read), explicitly mapped in
`MenusController.HasCapability` because that switch's default arm is permissive. The `/connections` Connectors tab
is retained and links its rows to Component detail; its retirement belongs to the later Connection-workspace work.

## Consequences

- Adding a family is additive: a provider, a kind token and a detail block.
- Membership follows the underlying definition, not Module install/enable state.
- Module-supplied Connectors are wired and tested with a fake but none exists in production today.
- Hard loads of native-id detail routes needed explicit host fallbacks, because the default `nonfile` fallback
  refuses a final segment that looks like a file (`Apperture.Platform.Sql`).
- If a real requirement ever forces independent Component lifecycle, identity or persistence, that requirement —
  not the abstraction — is the forcing function for a table.
