namespace Apperture.Cli.Bootstrap;

/// <summary>
/// Result of a bootstrap CLI command. No secrets; safe for output.
/// </summary>
public sealed record BootstrapCommandResult
{
    /// <summary>True if the command succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable message. No secrets.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Fields changed (e.g. Profile, CredentialReference).</summary>
    public IReadOnlyList<string> ChangedFields { get; init; } = [];

    /// <summary>True if restart/recycle of the host is recommended.</summary>
    public bool RestartRecommended { get; init; }
}
