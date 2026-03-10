namespace Apperture.Bootstrap.Models;

/// <summary>
/// Audit record for recovery/break-glass use. No secret values.
/// </summary>
public sealed record BootstrapRecoveryAuditRecord
{
    /// <summary>Timestamp of the recovery event.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Recovery action performed or attempted.</summary>
    public required BootstrapRecoveryAction RecoveryAction { get; init; }

    /// <summary>Actor identity (optional).</summary>
    public string? Actor { get; init; }

    /// <summary>Reason for recovery (optional).</summary>
    public string? Reason { get; init; }

    /// <summary>Outcome: Authorized, Denied, Executed, Failed.</summary>
    public required string Outcome { get; init; }

    /// <summary>Non-sensitive details. No passwords or secrets.</summary>
    public string? Details { get; init; }
}
