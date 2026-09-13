# Epic 113 — Platform settings option providers and scoped settings (design)

**Status:** Delivered (documentation / design only — no runtime changes)

**Canonical spec:** [platform-settings-option-providers-and-scopes.md](../01-architecture/platform-settings-option-providers-and-scopes.md)

**Context:** Epic 112 added static `AllowedValues` and `StringArray`. Product direction requires settings backed by live catalogs (connector definitions, modules, integration metadata) without turning the settings tree into a general metadata database or cross-module query bus.

## Decision

Produce an architecture specification **before** implementing dynamic providers or scoped settings. The spec must:

1. **Separate** settings, option providers, resource/catalog providers, domain config, datasets/artifacts, and custom UI/wizards.
2. Define option source kinds: `StaticAllowedValues` (112), `PlatformProvider`, `SameIntegrationProvider`, future `CrossIntegrationProvider`.
3. Require **backend validation** — never trust UI-loaded options.
4. Document **stale selection** policies (`PreserveWithWarning` vs `Reject`).
5. Document future **scopes** (Instance, Integration, Module, Connection, User) without implementing them.
6. Defer cross-integration bindings until same-integration providers are proven.

**Recommended next implementation:** Epic 114 — single `platform.*` provider prototype.

## Delivered

- Full design document with provider contracts (pseudo-code), authorization patterns, UI implications, phased roadmap, explicit non-goals
- Cross-links from [platform-settings-registry.md](../01-architecture/platform-settings-registry.md) and [management-platform-configuration.md](../03-operations/management-platform-configuration.md)
- Decisions index entry

## Non-goals (epic scope)

- Any runtime provider code, options endpoint, or new production keys
- Module-contributed settings; wizard framework; custom panels
- Changing Epic 109/112 API behavior

## References

- [Epic 112](./Epic112-Platform-Settings-MultiSelect-Foundation.md)
- [Epic 114](./Epic114-Platform-Setting-Option-Provider-Prototype.md)
