# ADR-0036: Extension taxonomy for connectors and execution modules

## Status

Proposed

## Context

The platform currently has two distinct contribution surfaces:

- **Connector packages** contribute connector definitions, client factories, and connection verification contributors (for example Campus Nexus, SFTP).
- **Modules** contribute executable behavior (workflows, operations, procedures, permissions, and slots).

Both are modular in code composition, but the vocabulary is inconsistent in docs and UI discussions. "Module" is often used for executable modules only, while connector packages are discussed separately. This creates ambiguity when planning management UX and SDK/scaffolding support for both.

The architecture boundary in [ADR-0027](./ADR-0027-connector-definitions-not-platform-clients.md) is already clear: platform governs connector definitions/connections/bindings; executable behavior is module-contributed.

## Decision

1. Adopt **Extension** as the umbrella term for platform-additive packages.
2. Define two first-class extension types:
   - **Connector Extension**: contributes connector definition metadata, client factory registration, and optional connection verification support.
   - **Execution Module**: contributes executable definitions and governance-facing runtime behavior (workflows/operations/procedures/permissions/slots).
3. Keep "module" in code where it is already well-established (`IModule`, `ModuleManifest`), but use **Execution Module** in docs/UI copy where disambiguation is needed.
4. Management UX labeling guidance:
   - Top-level conceptual grouping: **Extensions**
   - Existing screen labels remain explicit and honest: **Modules** and **Connectors**
   - Optional future inventory page may list both types together under Extensions.
5. SDK/scaffolding guidance:
   - Provide separate templates for **Connector Extension** and **Execution Module**.
   - Do not collapse them into a single template; required contracts and runtime responsibilities differ materially.

## Glossary

- **Extension**: Any package that extends platform capabilities through host composition.
- **Connector Extension**: Defines "what can be connected" and how to build typed clients from resolved connection material.
- **Execution Module**: Defines "what can be executed" via governed platform surfaces (workflow/operation/procedure).
- **Integration**: Canonical deployment/binding/scheduling unit that binds one execution module to concrete connections.

## Consequences

### Positive

- Shared language across architecture docs, roadmap conversations, and UI planning.
- Preserves honest platform boundary from ADR-0027 while giving a unifying term.
- Clarifies template/scaffolding expectations for module authors.

### Negative

- Introduces one new term ("Extension") that must be socialized across existing docs.
- Temporary mixed vocabulary likely during transition.

### Neutral

- No runtime behavior change; this is taxonomy and naming guidance.
- Existing APIs and core interfaces remain unchanged.

