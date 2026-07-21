namespace Apperture.ModuleSdk.Integration.Configuration;

/// <summary>
/// Module-authored named connection requirement (e.g. CnsApi, IntegrationDb, Sftp).
/// Capability-oriented: declares the contract type, not a persistence entity.
/// </summary>
public sealed record ConnectionRequirementDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public bool IsRequired { get; init; } = true;

    /// <summary>Fully-qualified CLR contract type name expected from binding resolution.</summary>
    public required string CapabilityContractType { get; init; }

    /// <summary>Optional stable capability kind key (http-api, relational-db, sftp).</summary>
    public string? CapabilityKind { get; init; }

    public ConnectionVerificationExpectation VerificationExpectation { get; init; } =
        ConnectionVerificationExpectation.Required;

    public bool AllowMultipleBindings { get; init; }

    public int Order { get; init; }
}

/// <summary>Well-known capability kind keys for current modules (not a universal connector taxonomy).</summary>
public static class ConnectionCapabilityKinds
{
    public const string HttpApi = "http-api";

    public const string RelationalDb = "relational-db";

    public const string Sftp = "sftp";
}
