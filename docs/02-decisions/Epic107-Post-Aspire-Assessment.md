# Epic 107 — Post-Aspire setup, bootstrap, and settings reassessment

**Status:** Complete (assessment / documentation only — no runtime changes)

**Operational detail:** [epic107-post-aspire-assessment.md](../03-operations/epic107-post-aspire-assessment.md) (historical; next-step recommendations superseded by [Epic 110](./Epic110-Foundation-Stabilization.md)).

**Context:** After Epic 106, the team needed a written record of what Aspire **did not** solve and which follow-on epics were safe to schedule (bootstrap store vs platform settings Phase 1).

## Decision

**Do not** expand Aspire scope into bootstrap persistence or platform settings. Schedule:

1. **Epic 108** — file-backed `IBootstrapConfigurationStore` + thin dev setup automation (primary).
2. **Epic 109** — platform settings registry Phase 1 (parallelizable).

Preserve script/docker-compose path; reframe as alternate, not deprecated.

## Assessment summary (recorded)

| Area | Aspire solved | Remained open (at time of 107) |
|------|---------------|--------------------------------|
| Local orchestration | SQL container, multi-process startup, connection injection, dashboard/OTEL | Appsettings Development template copy; dev auth flags |
| Configuration layers | Dev connection injection | Templates, secrets, bootstrap store, registry |
| Production bootstrap | — | File store, credential binding, CLI |
| Platform settings | — | Phase 1 notification/platform-events/attention keys |
| Worker topology | Clarified local = one WorkerHost | Production = control plane + SQL dispatch |

## Recommended candidates for platform settings (Epic 109 input)

- `Notifications.*` cutover flags (Epic 77 runbook alignment)
- `PlatformEvents.Enabled`
- `AttentionIncidents.Observation.*`
- Partial Entra policy overlay precedent (`EntraAuthOptionsDatabasePostConfigure`)

## Non-goals

- Implementing bootstrap store or registry in Epic 107
- Deprecating scripts or merging SQL volumes
- Production worker orchestration design

## References

- [Epic 106](./Epic106-Aspire-Local-Orchestration.md)
- [Epic 108](./Epic108-File-Backed-Bootstrap-Store.md)
- [Epic 109](./Epic109-Platform-Settings-Registry-Phase1.md)
- [Epic 110](./Epic110-Foundation-Stabilization.md)
