namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>Allowed value for <see cref="IntegrationSettingValueKind.Choice"/> settings.</summary>
public sealed record IntegrationSettingAllowedValue(string Value, string DisplayName, string? Description = null);
