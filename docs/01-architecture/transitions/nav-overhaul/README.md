# Management UI navigation overhaul

Working record of the operator information-architecture change that **shipped**. Runtime source of truth: `ControlPanelMenuDefinitions`, `RouteToSectionResolver`, [control-panel-ui.md](../../control-panel-ui.md), [ADR-0040](../../../02-decisions/ADR-0040-operator-information-architecture.md).

| File | Role |
|------|------|
| [before.md](before.md) | Historical snapshot immediately before the overhaul |
| [mapping.md](mapping.md) | Old → new destination map as implemented |
| [closure-report.md](closure-report.md) | What shipped, tests, intentional deferrals |
| This README | Pointers; not a second architecture contract |

## Status

**Complete.** Canonical rail: Control Center (logo), Integrations, Business Data, Config (gear). Operations, Automation, Connections, and Dashboard are not rail domains.
