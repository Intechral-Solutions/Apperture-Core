namespace Apperture.Bootstrap.Models;

/// <summary>
/// Audit record for bootstrap configuration changes. No raw secrets.
/// </summary>
public sealed record BootstrapChangeAuditRecord
{
    /// <summary>Timestamp of change.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Action (e.g. UpdateProfile, RotateCredential).</summary>
    public required string Action { get; init; }

    /// <summary>Actor identity (optional).</summary>
    public string? Actor { get; init; }

    /// <summary>Non-sensitive details (no passwords).</summary>
    public string? Details { get; init; }
}
