# Future Concept Note — Systems as a Logical Grouping Layer

## Status

Deferred / future concept  
Not part of the current platform roadmap

## Intent

Introduce a future **System** concept as a logical grouping layer above integrations.

A System would represent a managed external platform or domain that may eventually own or organize:

- related integrations
- system-level monitoring and attention
- metadata exploration / object browsing
- future system-aware workflow authoring capabilities

## Why this is deferred

The current platform is still solidifying the more fundamental operational model:

- connections
- integrations
- readiness
- verification
- procedures
- fleet attention
- expected-run correctness

Adding Systems now would likely create a premature abstraction with limited real behavior, while increasing complexity in navigation, permissions, ownership, and lifecycle semantics.

For now, integrations remain ungrouped and live directly within the current management scope.

## Current model

The platform currently uses these concepts:

- **Connection** — persisted configuration/secrets for external access
- **Integration** — canonical deployable and operational unit
- **Fleet** — the full managed population of integrations in the current control-plane scope

A future **System** would be a grouping/orchestration concept above integrations, not a replacement for Integration or Fleet.

## Future direction

If introduced later, a System may support capabilities such as:

- grouping related integrations under one managed external system
- summarizing health/attention across those integrations
- exposing system metadata and remote object exploration
- serving as a future anchor for advanced workflow-builder scenarios that retrieve records and execute commands on remote systems

## Non-goals for the current platform

The current platform does **not** implement:

- system entities
- system-level permissions/resources
- system metadata browsers
- system-scoped workflow builders
- system ownership requirements for integrations

## Design guardrail for current work

Current implementation should avoid assumptions that make a future System layer awkward or impossible, but should **not** introduce placeholder abstractions for Systems before real behavior justifies them.

## Revisit trigger

Revisit this concept when at least one of the following becomes a near-term product need:

- operators need to manage multiple related integrations as one external system
- system-level monitoring or attention becomes more valuable than per-integration fleet views alone
- a metadata/object browser becomes product-relevant
- advanced workflow authoring needs a system-scoped remote object model