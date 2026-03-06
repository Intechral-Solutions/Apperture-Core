namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// Lifecycle state for a secret version.
/// </summary>
public enum SecretState
{
    Pending = 0,
    Active = 1,
    Disabled = 2,
    Revoked = 3
}
