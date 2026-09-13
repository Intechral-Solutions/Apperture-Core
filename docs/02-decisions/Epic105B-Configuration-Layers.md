# Epic 105B — Configuration layers decision

**Status:** Delivered (canonical record: [ADR-0039](./ADR-0039-environment-startup-bootstrap-and-platform-settings.md))

**Context:** Epic 105A inventoried startup files but did not record **why** configuration is split across layers or what must never collapse together.

## Decision

Adopt a **four-layer configuration model** with explicit boundaries:

| Layer | Owner / lifecycle | Storage (Phase 1+) |
|-------|-------------------|---------------------|
| **Environment / startup** | Install, dev, host boot | `appsettings`, env vars, Aspire injection (106) |
| **Secrets / credentials** | Security / deployment | User Secrets, `.env`, future DPAPI/Key Vault (108b) |
| **Bootstrap state** | First-run / instance install | File-backed JSON profile + credential references (108) |
| **Platform-managed settings** | Operators via product | SQL registry, API/UI (109+) |

**Non-negotiable rules:**

1. `appsettings` and deploy templates are **startup/default/template** configuration — not the steady-state operator plane.
2. Platform-managed settings **must not write back** to JSON files.
3. Secret plaintext **must not** be committed or stored in registry/bootstrap JSON.
4. Installer wizard and production credential binding remain **deferred** until bootstrap store exists (108) and reassessed post-Aspire (107, 110).

## Delivered

- [ADR-0039-environment-startup-bootstrap-and-platform-settings.md](./ADR-0039-environment-startup-bootstrap-and-platform-settings.md) — accepted decision record with mermaid layer diagram, post-Aspire checklist, and cross-links
- [platform-settings-registry.md](../01-architecture/platform-settings-registry.md) — conceptual registry (implementation in 109)
- [bootstrap-and-secrets-boundary.md](../01-architecture/bootstrap-and-secrets-boundary.md) — bootstrap vs operational secrets

## Consequences

**Positive:** Contributors and operators have one place to decide where a knob belongs before adding files or APIs.

**Negative:** Four layers increase onboarding surface; local dev still requires appsettings copy even with Aspire (106).

## References

- [Epic 105A](./Epic105A-Environment-Startup-Inventory.md)
- [Epic 106](./Epic106-Aspire-Local-Orchestration.md)
- [Epic 107](./Epic107-Post-Aspire-Assessment.md)
- [Epic 108](./Epic108-File-Backed-Bootstrap-Store.md)
- [Epic 109](./Epic109-Platform-Settings-Registry-Phase1.md)
