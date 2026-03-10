namespace Apperture.Bootstrap.Models;

/// <summary>
/// Recovery action request for break-glass. Recovery only; do not use for routine credential rotation or admin.
/// </summary>
public sealed record BootstrapRecoveryRequest
{
    /// <summary>Recovery action type.</summary>
    public BootstrapRecoveryAction ActionType { get; init; }

    /// <summary>Legacy action identifier (optional; use ActionType for strongly-typed use).</summary>
    public string? Action { get; init; }

    /// <summary>Reason for recovery.</summary>
    public string? Reason { get; init; }

    /// <summary>Optional proof (e.g. MFA token).</summary>
    public object? Proof { get; init; }

    /// <summary>Optional new credential reference for RebindCredentialSource. No raw secrets.</summary>
    public BootstrapCredentialReference? NewCredentialReference { get; init; }

    /// <summary>Optional recovery profile for SwitchToRecoveryProfile. No credentials.</summary>
    public SqlConnectionBootstrapProfile? RecoveryProfile { get; init; }
}
