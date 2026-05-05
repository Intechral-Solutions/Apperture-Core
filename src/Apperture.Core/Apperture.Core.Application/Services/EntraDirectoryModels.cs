namespace Apperture.Core.Application.Services;

/// <summary>
/// Context for Microsoft Graph directory discovery (client credentials). Tenant id should be the Azure AD directory id.
/// </summary>
public sealed record EntraDirectoryDiscoveryContext(
    string TenantId,
    string ClientId,
    string ClientSecret,
    string RegistrationId);

/// <summary>
/// Normalized Entra directory user for admin browse/import preparation (not a platform user).
/// </summary>
public sealed record EntraDirectoryUser(
    string ObjectId,
    string DisplayName,
    string? UserPrincipalName,
    string? Mail);

/// <summary>
/// Normalized Entra directory group for admin browse/import preparation.
/// </summary>
public sealed record EntraDirectoryGroup(
    string ObjectId,
    string DisplayName,
    string? MailNickname,
    string? Description);

/// <summary>
/// Result of listing directory principals via Graph (discovery only).
/// </summary>
public sealed record DirectoryDiscoveryResult<T>(
    bool Success,
    DiscoveryFailureCategory FailureCategory,
    string Summary,
    string? TechnicalDetail,
    T? Data = default);
