namespace Apperture.Bootstrap.Models;

/// <summary>
/// Result of recovery action authorization.
/// </summary>
public sealed record BootstrapRecoveryAuthorizationResult
{
    /// <summary>True if recovery action is authorized.</summary>
    public bool Authorized { get; init; }

    /// <summary>Reason when not authorized.</summary>
    public string? Reason { get; init; }
}
