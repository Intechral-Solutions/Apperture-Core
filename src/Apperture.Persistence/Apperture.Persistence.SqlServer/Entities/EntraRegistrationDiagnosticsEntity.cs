namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Operational telemetry for one Entra registration: the outcome of its latest OpenID configuration validation and of its latest
/// directory discovery attempt. One row per registration (1:1, keyed by the registration id), removed with it.
/// </summary>
/// <remarks>
/// Probe outcomes are telemetry, not configuration, so they live apart from <see cref="EntraInstanceRegistrationEntity"/>: a
/// probe writes only this table and so changes neither the registration's <c>UpdatedUtc</c> nor its <c>rowversion</c>, the token
/// an administrator's save checks. There is no concurrency token here; two probes of one registration are last-writer-wins.
/// </remarks>
public sealed class EntraRegistrationDiagnosticsEntity
{
    public string RegistrationId { get; set; } = "";

    public int OpenIdConfigurationStatus { get; set; } = DefaultOpenIdConfigurationStatus;

    public DateTimeOffset? LastOpenIdValidationUtc { get; set; }

    public string? LastOpenIdValidationSummary { get; set; }

    public string? LastOpenIdValidationDetail { get; set; }

    public int DirectoryDiscoveryStatus { get; set; } = DefaultDirectoryDiscoveryStatus;

    public DateTimeOffset? LastDirectoryDiscoveryUtc { get; set; }

    public string? LastDirectoryDiscoverySummary { get; set; }

    public string? LastDirectoryDiscoveryDetail { get; set; }

    /// <summary>The stored value of <c>OpenIdConfigurationStatus.NotValidated</c>: a registration nobody has validated yet.</summary>
    public const int DefaultOpenIdConfigurationStatus = 1;

    /// <summary>The stored value of <c>DirectoryDiscoveryStatus.NotAttempted</c>: a registration nobody has searched yet.</summary>
    public const int DefaultDirectoryDiscoveryStatus = 1;
}
