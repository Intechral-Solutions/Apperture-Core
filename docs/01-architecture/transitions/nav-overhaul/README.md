# Management UI navigation overhaul

**Not architecture.** This set is a working record of a **heavy nav overhaul that is about to happen**. Do not treat anything here as the live shell contract or the target IA until it is written into an ADR and [control-panel-ui.md](../../control-panel-ui.md).

| File | Role |
|------|------|
| [before.md](before.md) | Snapshot of nav as it exists immediately before the overhaul (rail, Config, account, off-menu pages) |
| This README | Intent and how to use the set |

Code remains the runtime source of truth until the overhaul ships: `ControlPanelMenuDefinitions`, `AccountUiRoutes`, and the page `@page` routes.

## Status

About to change. The before-snapshot is captured; the target tree is not decided in this folder yet.

## After the overhaul

1. Record the decided IA in `docs/02-decisions/` (ADR).
2. Update [control-panel-ui.md](../../control-panel-ui.md) to match what shipped.
3. Delete this folder (or leave a one-line pointer to the ADR).
