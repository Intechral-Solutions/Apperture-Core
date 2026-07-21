namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>
/// Module-authored integration-instance setting declaration.
/// Persistence, secrets, and UI controls are platform concerns — not part of this contract.
/// </summary>
public sealed record IntegrationSettingDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required IntegrationSettingValueKind ValueKind { get; init; }

    public bool IsRequired { get; init; }

    /// <summary>
    /// Default when unset. Forbidden when <see cref="ValueKind"/> is <see cref="IntegrationSettingValueKind.SensitiveText"/>.
    /// </summary>
    public object? DefaultValue { get; init; }

    public IReadOnlyList<IntegrationSettingAllowedValue> AllowedValues { get; init; } =
        Array.Empty<IntegrationSettingAllowedValue>();

    public IntegrationSettingConstraints? Constraints { get; init; }

    /// <summary>Lower sort order appears first.</summary>
    public int Order { get; init; }

    /// <summary>Optional operator grouping label.</summary>
    public string? Group { get; init; }

    /// <summary>True when the value is secret-backed. Equivalent to <see cref="IntegrationSettingValueKind.SensitiveText"/>.</summary>
    public bool IsSensitive =>
        ValueKind == IntegrationSettingValueKind.SensitiveText;
}
