namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>Optional validation constraints for an integration setting declaration.</summary>
public sealed record IntegrationSettingConstraints
{
    public int? MinInteger { get; init; }

    public int? MaxInteger { get; init; }

    public decimal? MinDecimal { get; init; }

    public decimal? MaxDecimal { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public string? Pattern { get; init; }

    /// <summary>ISO-8601 duration lower bound when <see cref="IntegrationSettingValueKind.Duration"/>.</summary>
    public string? MinDurationIso { get; init; }

    /// <summary>ISO-8601 duration upper bound when <see cref="IntegrationSettingValueKind.Duration"/>.</summary>
    public string? MaxDurationIso { get; init; }
}
