# Management UI navigation overhaul

Working record of the operator information-architecture change that **shipped**. Runtime source of truth: `ControlPanelMenuDefinitions`, `RouteToSectionResolver`, [control-panel-ui.md](../../control-panel-ui.md), [ADR-0040](../../../02-decisions/ADR-0040-operator-information-architecture.md).

| File | Role |
|------|------|
| [before.md](before.md) | Historical snapshot immediately before the overhaul |
| [mapping.md](mapping.md) | Old → new destination map as implemented |
| [closure-report.md](closure-report.md) | What shipped, tests, intentional deferrals |
| This README | Pointers; not a second architecture contract |

## Status

**Complete (NAV-1).** Canonical rail: Control Center (logo), Integrations, Business Data, Config (gear). Operations, Automation, Connections, and Dashboard are not rail domains.

This completed **rail** overhaul is **not** the future **cross-context organizational grouping** body recorded as near-term item 8 in [`pre-release-product-roadmap.md`](../../../00-overview/pre-release-product-roadmap.md). Item 8 is undesigned and not started; do not treat this folder as that work.
