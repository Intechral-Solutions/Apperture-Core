# Integration typed settings persistence

**Scope:** SDK-6C configuration model and SDK-6D operator workflow  
**Status:** Canonical guidance for module authors and platform adapters

## Summary

Integration-instance settings may persist as **culture-invariant strings** in integration-scoped platform setting rows when module accessors validate and return typed values deterministically. Dedicated SQL columns per logical kind (decimal, URI, duration, time zone) are **not required** unless operational evidence shows string persistence is insufficient.

## Logical kinds and persistence

| Logical kind (`IntegrationSettingValueKind`) | Stored representation | Invariant rules |
| --- | --- | --- |
| String, Boolean, Integer | JSON scalar in `PlatformSettingValues` | Booleans as JSON `true`/`false`; integers as JSON numbers |
| Decimal | Culture-invariant decimal string | Parse with `InvariantCulture`; reject group separators and locale-specific formats |
| Uri | Absolute URI string | Must be absolute; normalized on read (trim, no relative URIs for production settings) |
| Duration | ISO-8601 duration (`PnYnMnDTnHnMnS`) or standard `c` format where documented | Round-trip through `TimeSpan` using invariant parsing |
| TimeZone | IANA time zone id string (e.g. `Australia/Sydney`) | Must resolve via `TimeZoneInfo.FindSystemTimeZoneById` on read |
| SensitiveText | Secret reference GUID only | Plaintext never stored in settings tables; presence-only in Management API |

Non-sensitive values are stored normally in integration-scoped settings tables. The security guarantee applies to **sensitive** values: secrets are not persisted or returned as plaintext.

## Validation before ready

Readiness evaluation (`ConfigurationReadinessEvaluator` / integration readiness services) treats invalid persisted values as blocking configuration issues **before** an integration or procedure is considered runnable:

1. Load effective value (definition default, integration DB override, or secret presence for sensitive keys).
2. Validate/coerce to the declared logical kind using invariant rules above.
3. Emit a blocking readiness reason when required settings are missing, secrets are not configured, or coercion fails.

Module code should use `IIntegrationSettingsAccessor` / `IIntegrationOptions<T>` at runtime rather than parsing raw strings ad hoc.

## Round-trip expectations

Operators set values through Management API integration settings endpoints. Module accessors must:

- Accept the same canonical string form written by the API/editor.
- Return the same typed value on subsequent reads within the instance.
- Fail readiness (not silently coerce) when stored data cannot be parsed.

Unit and integration tests should cover at least one non-default persisted value per kind used by the module.

## Related surfaces

- Declarations: `GET /api/integrations/{id}/configuration/declarations`
- Settings CRUD: `/api/integrations/{id}/settings`
- Readiness: `/api/integrations/{id}/readiness` and procedure-scoped readiness routes
- Endpoint map: [integration-configuration-endpoint-map.md](./integration-configuration-endpoint-map.md)
