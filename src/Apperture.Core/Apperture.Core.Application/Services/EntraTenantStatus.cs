namespace Apperture.Core.Application.Services;

/// <summary>
/// Persisted outcome of the last OpenID Connect metadata validation for an Entra tenant registration.
/// </summary>
public enum OpenIdConfigurationStatus
{
    Unknown = 0,
    NotValidated = 1,
    Valid = 2,
    InvalidConfiguration = 3,
    DiscoveryUnreachable = 4,
    IssuerMismatch = 5,
    TransientFailure = 6
}

/// <summary>
/// Persisted outcome of the last directory (Microsoft Graph) discovery attempt for an Entra registration.
/// </summary>
public enum DirectoryDiscoveryStatus
{
    Unknown = 0,
    NotAttempted = 1,
    Success = 2,
    FailedConfiguration = 3,
    FailedPermissions = 4,
    TransientFailure = 5,
    Throttled = 6
}

/// <summary>
/// Non-success categorization for <see cref="DirectoryDiscoveryResult{T}"/> API responses.
/// </summary>
public enum DiscoveryFailureCategory
{
    None = 0,
    Configuration = 1,
    Permissions = 2,
    Transient = 3,
    Throttled = 4
}
