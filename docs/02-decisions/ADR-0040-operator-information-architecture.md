# ADR-0040: Operator information architecture

## Status

Accepted

## Context

The Management shell used six rail domains (Dashboard, Operations, Automation, Integrations, Connections, Config). Dashboard competed with Control Center as home. Blocked work, runs, schedules, notifications, and demo customers sat in places that implied the wrong owner. Module navigation could invent extra rail domains. Historical orientation (Epic 100) treated Dashboard as the summary home and Control Center as a live console under Operations.

Operators need a smaller product map: one home, a first-class Integrations domain, a transitional Business Data surface before later catalog work, and Config as administration — without collapsing distinct models into a generic resource catalog.

## Decision

The operator shell has four rail domains:

```
[Logo] Control Center
Integrations
Business Data
----------------
[Gear] Config
```

Rules:

1. **Control Center is the sole operator home.** `/` and post-login land on `/control/center`. `/dashboard` redirects there. Dashboard Panel routes stay deferred under Developer & Internal.
2. **Runs belong to Control Center.** Blocked work is the derived facet `/runs?status=blocked`, not a separate domain or database status. Scheduler cycle remains on Control Center; per-run retry remains on run detail.
3. **Integrations stay first-class.** Connections and schedules keep their own models and URLs; they are discovered from Inventory and Integration links, not promoted to rail domains.
4. **Business Data is a transitional collections surface.** Customers remain the existing demo projection at `/business-data/customers` (legacy `/demo/customers` redirects). No `BusinessRecord` table in this change.
5. **Config is Inventory, Governance, Attention, System.** Notifications live under Attention. Modules, platform settings, authentication, and Developer & Internal live under System.
6. **UI aggregation is not domain ownership.** Inventory and Control Center may list connections, schedules, runs, and collections; those objects do not become Config or Integration entities.
7. **Menus hide by established capabilities.** `GET /api/menus` omits items the operator cannot use. Page and API authorization remain independent. Missing view-capabilities are not invented here.
8. **Module contributions attach only to known rail domains.** Unknown domains are ignored.

Live contract: [control-panel-ui.md](../01-architecture/control-panel-ui.md). Destination map: [nav-overhaul/mapping.md](../01-architecture/transitions/nav-overhaul/mapping.md).

## Consequences

### Positive

- One operator home and a smaller rail.
- Blocked work is a filter on the real Runs surface.
- Customers leave Developer & Internal without pretending to be a platform record store.
- Config has a real Inventory instead of a dump of every administrative list.

### Negative

- Some destinations remain off-rail (connections, schedules, deferred dashboard tools) and rely on Inventory, Integration links, or Developer & Internal.
- Capability hiding is incomplete where no view token exists (Runs, Execute, Control Center overview, Modules, Developer & Internal).

### Neutral

- Historical Epics 98–101 remain valid as prior steps; this ADR supersedes their rail/home orientation.
- Later work (Integration Operations Hub, ResourceContext, Resource Catalog, final Business Data collections, Dashboard Panels, Applications/Services) is explicitly out of scope.
