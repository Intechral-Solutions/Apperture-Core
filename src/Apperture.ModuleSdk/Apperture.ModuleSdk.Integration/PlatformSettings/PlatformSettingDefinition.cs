namespace Apperture.Core.Application.PlatformSettings;

/// <summary>Hardcoded platform-owned setting metadata (Phase 1).</summary>
public sealed record PlatformSettingDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required PlatformSettingCategory Category { get; init; }

    public required PlatformSettingValueType ValueType { get; init; }

    public required PlatformSettingInputKind InputKind { get; init; }

    /// <summary>
    /// Optional provider key indicating that the UI should load options from a provider and that the backend must validate
    /// submitted values against that provider.
    /// </summary>
    public string? OptionProviderKey { get; init; }

    /// <summary>
    /// Required provider context for option lookup and write-time validation. Defaults to instance-wide catalogs.
    /// </summary>
    public PlatformSettingOptionProviderContextKind OptionProviderContextKind { get; init; }
        = PlatformSettingOptionProviderContextKind.Instance;

    /// <summary>Value scopes where this setting may be stored (instance, integration, etc.).</summary>
    public IReadOnlyList<PlatformSettingValueScopeKind> AllowedValueScopes { get; init; }
        = [PlatformSettingValueScopeKind.Instance];

    /// <summary>IConfiguration path (colon-separated) for boot default resolution.</summary>
    public required string ConfigurationPath { get; init; }

    public required object DefaultValue { get; init; }

    public IReadOnlyList<PlatformSettingAllowedValue>? AllowedValues { get; init; }

    public int? MinInteger { get; init; }

    public int? MaxInteger { get; init; }

    public TimeSpan? MinDuration { get; init; }

    public int? MinItems { get; init; }

    public int? MaxItems { get; init; }

    public bool Editable { get; init; } = true;

    public bool RequiresRestart { get; init; } = true;

    /// <summary>
    /// When true, values are stored via <c>ISecretStore</c>; platform setting persistence holds only an opaque secret reference.
    /// Read APIs expose configuration status, never plaintext or the reference GUID.
    /// </summary>
    public bool IsSensitive { get; init; }

    /// <summary>
    /// Whether an audit row of a change may carry this setting's values. Independent of <see cref="IsSensitive"/> and of how the
    /// setting is stored or read; it only governs audit content. See <see cref="PlatformSettingAuditValuePolicy"/>.
    /// </summary>
    public PlatformSettingAuditValuePolicy AuditValue { get; init; } = PlatformSettingAuditValuePolicy.ByShape;
}
