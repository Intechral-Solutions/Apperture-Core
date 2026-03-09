namespace Apperture.Bootstrap.Models;

/// <summary>
/// Transient update request. May carry sensitive payload. Do not persist; use and discard.
/// </summary>
public sealed record BootstrapUpdateRequest
{
    /// <summary>Updated profile (non-secret).</summary>
    public SqlConnectionBootstrapProfile? Profile { get; init; }

    /// <summary>Updated credential reference; no raw secret.</summary>
    public BootstrapCredentialReference? CredentialReference { get; init; }

    /// <summary>
    /// Transient sensitive payload for credential update (e.g. new password, cert).
    /// Must not be persisted or logged.
    /// </summary>
    public object? SensitivePayload { get; init; }
}
