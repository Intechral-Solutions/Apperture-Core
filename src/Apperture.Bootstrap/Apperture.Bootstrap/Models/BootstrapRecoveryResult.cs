namespace Apperture.Bootstrap.Models;

/// <summary>
/// Result of a recovery operation. No secret values.
/// </summary>
public sealed record BootstrapRecoveryResult
{
    /// <summary>True if recovery completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Message or error description.</summary>
    public string? Message { get; init; }
}
