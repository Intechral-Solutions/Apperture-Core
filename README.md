# Apperture Core

Apperture Core is an open-core automation and integration platform built around a **durable, persistent workflow model** with consistent telemetry, auditable operations, and clean control-plane/instance-plane boundaries.

## Status

**Current phase:** Documentation and repository foundation. Ready for initial commit. No source code yet.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (see `global.json` for version)
- Git

## Repository structure

```
Apperture-Core/
├─ .ai/                 # AI guidance (playbooks, memory, prompts, agents)
├─ docs/                # Human-facing documentation
├─ src/                 # Source projects (next phase)
├─ tests/               # Test projects (next phase)
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

This project is licensed under the [GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0). AGPL is used to build towards an Enterprise offering. See [LICENSE](LICENSE) for full terms.
