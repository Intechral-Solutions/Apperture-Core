# Epic 112 — Platform settings multi-select foundation

**Status:** Delivered

**Context:** Epic 111 polished scalar Phase 1 settings. Future settings need array values, static multi-select, and input metadata without implementing dynamic catalogs or module contributions yet.

## Decision

Extend the registry foundation for **static** select/multi-select only:

| Addition | Purpose |
|----------|---------|
| `PlatformSettingInputKind` | UI hint distinct from `ValueType` (Toggle, Number, Text, Duration, Select, MultiSelect) |
| `PlatformSettingAllowedValue` | Static options (`value`, `label`, optional `description`) |
| `ValueType.StringArray` | Multi-select storage; JSON array in DB |
| `PlatformSettingsAllowedValuesValidator` | Membership, duplicates, `MinItems`/`MaxItems` |
| API contract | Scalar `value` vs array `values`; resolved `CurrentValues` for arrays |
| UI | Checkbox list for `MultiSelect` with static `AllowedValues` |

**Duplicate policy:** Reject duplicates (do not normalize). **Empty array:** Explicit `[]` is a database override; missing row means no override.

**No new production keys** — nine Phase 1 keys unchanged; coverage via test-only definitions and structural tests.

## Non-goals

- Dynamic option providers (Epic 113 design, Epic 114 prototype)
- Integration/module/user scopes
- Searchable async selectors
- Production MultiSelect setting exposed to operators

## References

- [platform-settings-registry.md](../01-architecture/platform-settings-registry.md#stringarray-and-static-multi-select-epic-112)
- [Epic 113](./Epic113-Platform-Settings-Option-Providers-Design.md)
- [Epic 114](./Epic114-Platform-Setting-Option-Provider-Prototype.md)
