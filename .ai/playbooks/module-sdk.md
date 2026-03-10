# Module SDK

## Module structure

A module provides:

- Workflow definitions ("procedures")
- Step implementations
- Optional connectors/adapters
- Optional UI metadata (nav links, screens)
- Permission/resource declarations

## Resource and permission registration

Modules receive `IResourceDescriptorRegistry` from host DI at startup and must call `RegisterResource` and `RegisterPermission` for any module-contributed resource types and permissions. Use `ActionConstants` and `ResourceTypeConstants` from Core.Domain.Governance for consistency; see ADR-0013 for naming conventions.

**Constraints:**
- Do not browse arbitrary global resources at runtime.
- Resolve only explicitly declared and bound dependencies (slots, connections).
- Binding is an explicit delegation mechanism; do not bypass it.

See auth and resource governance plan Section 7.2 (Forbidden) and 7.3 (Anti-Patterns).

## Metadata (manifest)

- Id, Name, Version, Description
- Capabilities
- Required Permissions / Resources
- Configuration schema (future UI-driven config)

## Workflow registration

Modules register workflow definitions with the runtime. Each definition includes:

- Typed parameter schema (for UI-generated forms)
- Step graph (linear sequence first; extensible later)
- Operational attributes: category (Query vs Command), required permissions, visibility, retry policy, idempotency rules

## Step implementation

Steps are the unit of execution. The runtime invokes them in order according to the workflow definition. Steps receive context and parameters; they return results that feed the next step or the final OperationResult.

## Deployment

- **On-prem:** Modules are compiled-in projects referenced by the worker host. One module per process.
- **Cloud:** Modules are deployed as their own Function App. "Install" is deployment configuration.
