namespace Apperture.Core.Application.PlatformSettings;

public sealed record PlatformSettingUpdatePayload
{
    public string? ScalarValue { get; init; }

    public IReadOnlyList<string>? ArrayValues { get; init; }

    /// <summary>
    /// Integration context for provider-backed validation when the setting requires integration-scoped options.
    /// Does not change where the setting value is persisted in Epic 115.
    /// </summary>
    public string? IntegrationId { get; init; }

    /// <summary>
    /// Sensitive settings only: <see cref="PlatformSettingSecretOperations.Replace"/>,
    /// <see cref="PlatformSettingSecretOperations.Clear"/>, or null/omit for unchanged.
    /// </summary>
    public string? SecretOperation { get; init; }

    /// <summary>Plaintext secret for replace only; never persisted in platform setting tables and never returned from APIs.</summary>
    public string? SecretValue { get; init; }
}
