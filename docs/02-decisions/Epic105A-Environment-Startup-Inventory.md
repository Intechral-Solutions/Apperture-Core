# Epic 105A — Environment and startup configuration inventory

**Status:** Delivered

**Context:** Before platform-managed settings and Aspire orchestration, `appsettings` files and [deploy/config](../../deploy/config/) templates were scattered across the repo with no single operator-facing map of what is startup configuration vs future steady-state control plane.

## Decision

Treat **environment/startup configuration** as a distinct layer: committed baselines, gitignored dev overlays, and install templates only. Document the canonical file inventory and loading order; do not use `appsettings` as the long-term operator settings plane.

## Delivered

| Artifact | Purpose |
|----------|---------|
| [management-platform-configuration.md](../03-operations/management-platform-configuration.md) | Canonical inventory of Management.Host, UI, and WorkerHost startup files; explains ~40+ search hits vs ~10 intentional edit paths |
| [deploy/config](../../deploy/config/) examples | `appsettings.docker-dev`, Windows, Azure topology skeletons; UI `wwwroot` appsettings |
| Rationalization | Production-safe notification defaults in committed JSON; dev overlays gitignored; legacy redirect for Development example |

**Key rules captured:**

- Only edit paths listed in the canonical inventory (not `tests/**/bin`, `**/obj`, or `poc/`).
- Integration tests use `Testing` environment + injected bootstrap profile; no test appsettings required.
- Steady-state operator changes belong in the future platform settings registry (delivered in Epic 109), not in JSON files.

## Non-goals

- Bootstrap persistence (Epic 108)
- Platform settings registry implementation (Epic 109)
- Aspire AppHost (Epic 106)
- ADR-level four-layer model (Epic 105B → [ADR-0039](./ADR-0039-environment-startup-bootstrap-and-platform-settings.md))

## References

- [ADR-0039](./ADR-0039-environment-startup-bootstrap-and-platform-settings.md) — layer model (105B)
- [management-platform-configuration.md](../03-operations/management-platform-configuration.md)
- [platform-settings-registry.md](../01-architecture/platform-settings-registry.md)
