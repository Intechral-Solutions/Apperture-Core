# Epic 117 — Integration-scoped settings and Integration Detail settings UI design

**Status:** Delivered (documentation/design only)

**Canonical spec:** [integration-scoped-settings.md](../01-architecture/integration-scoped-settings.md)

## Context

Epics 109–116 delivered instance-scoped platform settings, static and provider-backed options, integration provider context, and a real same-integration provider (`campusnexus.campuses`).  
What remains is a clear design for integration-scoped **value storage**, **API/UI management**, and **runtime consumption**, while preserving separation from provider context.

## Decision

Create a dedicated architecture spec that defines:

1. Conceptual separation among provider context, value scope, and runtime context.
2. First implementation scope as `Integration` (using existing `ScopeKind`/`ScopeId` columns).
3. Namespace strategy for platform-owned and future module-owned integration keys.
4. Integration settings API shape under `/api/integrations/{integrationId}/settings...`.
5. Integration Detail settings UI surface at `/integrations/{integrationId}/settings`.
6. Authorization, audit, lifecycle/stale behavior, and runtime-read model.
7. Recommended sequence for implementation epics (118+).

## Explicit non-goals

- No integration-scoped persistence or resolver implementation
- No integration settings API/controller implementation
- No integration settings UI implementation
- No production setting keys
- No module-contributed definitions
- No runtime consumption implementation
- No resource/catalog browser or wizard framework
- No cross-integration `ProviderBinding` implementation

## Verification

Docs-only epic. No build/tests run.

## Recommended next epic

**Epic 118:** Integration-scoped settings persistence and API foundation.

## References

- [platform-settings-registry.md](../01-architecture/platform-settings-registry.md)
- [platform-settings-option-providers-and-scopes.md](../01-architecture/platform-settings-option-providers-and-scopes.md)
- [Epic 115](./Epic115-Integration-Scoped-Option-Provider-Foundation.md)
- [Epic 116](./Epic116-CampusNexus-Campuses-Option-Provider.md)
