# Apperture Core

Apperture Core is an open-core automation and integration platform built around a **durable, persistent workflow model** with consistent telemetry, auditable operations, and clean control-plane/instance-plane boundaries.

## Status

**Current phase:** Product foundation baseline complete; major pre-release product development ahead. Stabilization (S1–S13) and Scheduling & Worker Management foundation (SWM-1–SWM-12) are closed. The repository has a working control plane (Management Host/API, Blazor UI), worker/runtime path, settings registry, auth/RBAC, integrations and connections, notifications, and schedule dispatch — operable via Aspire AppHost or Manage.ps1. This is **foundation-ready for continued product work**, not a finished release. See [`docs/00-overview/pre-release-product-roadmap.md`](docs/00-overview/pre-release-product-roadmap.md) and [`docs/audits/pfb1-product-foundation-baseline-audit.md`](docs/audits/pfb1-product-foundation-baseline-audit.md).

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (see `global.json` for version)
- Git

## Repository structure

```
Apperture-Core/
├─ .ai/                 # AI guidance (playbooks, memory, prompts, agents)
├─ docs/                # Human-facing documentation
├─ src/                 # Source projects (control plane, runtime, modules, shared)
├─ tests/               # Unit, integration, architecture, and e2e tests
├─ deploy/              # Docker, K8s, Azure config (future)
├─ scripts/             # Dev and CI scripts (with implementation)
└─ tools/               # Internal dev/build utilities
```

## Documentation

| Section | Contents |
|---------|----------|
| [00-overview](docs/00-overview/) | Vision, terminology, roadmap |
| [01-architecture](docs/01-architecture/) | Control plane, instance plane, tenancy, workflows, runtime backends |
| [02-decisions](docs/02-decisions/) | Architecture decision records (ADRs) |
| [03-operations](docs/03-operations/) | Observability, runbooks, incident response, backups |
| [04-api](docs/04-api/) | API contracts, OpenAPI |
| [05-modules](docs/05-modules/) | Module authoring guide, manifest spec, module types |

For AI agents and contributors: see [AGENTS.md](AGENTS.md) and [.ai/playbooks/](.ai/playbooks/).

## Contributing

1. Read [.ai/playbooks/principles.md](.ai/playbooks/principles.md) and [.ai/playbooks/anti-patterns.md](.ai/playbooks/anti-patterns.md).
2. Follow the architecture in [docs/01-architecture/](docs/01-architecture/).
3. Record decisions in `docs/02-decisions/` using the ADR template.

## License

Apperture Core is free and open-source software, licensed under the GNU Affero General Public License v3.0 only (`AGPL-3.0-only`). The full license text is in [LICENSE](LICENSE).

Copyright (c) 2026 Tennyson Edward Hull and contributors.

Use, modification, and redistribution are governed by the AGPLv3. If you modify Apperture Core and let users interact with your modified version over a network, the AGPL's corresponding-source requirements apply (see section 13 of the license).

Contributions require agreement to the Apperture Individual Contributor License Agreement ([CLA.md](CLA.md)). See [CONTRIBUTING.md](CONTRIBUTING.md) for how to contribute and how the CLA works.
