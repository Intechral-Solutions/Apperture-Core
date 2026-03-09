namespace Apperture.Bootstrap.Models;

/// <summary>
/// Recovery action request for break-glass. Recovery only, not routine admin.
/// </summary>
public sealed record BootstrapRecoveryRequest
{
    /// <summary>Recovery action identifier.</summary>
    public required string Action { get; init; }

    /// <summary>Reason for recovery.</summary>
    public string? Reason { get; init; }

    /// <summary>Optional proof (e.g. MFA token).</summary>
    public object? Proof { get; init; }
}
