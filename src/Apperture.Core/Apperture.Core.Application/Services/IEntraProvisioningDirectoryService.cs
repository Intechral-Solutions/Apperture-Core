using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Server-side Entra directory browse for Governance provisioning. The Blazor client never receives
/// Graph tokens. Continuation tokens are opaque and registration-scoped.
/// </summary>
public interface IEntraProvisioningDirectoryService
{
    const int MaxPageSize = 50;
    const int MaxBatchSize = 20;

    ValueTask<EntraProvisioningDirectoryPage> SearchAsync(
        string registrationId,
        string? search,
        string? continuationToken,
        int top,
        CancellationToken cancellationToken = default);

    ValueTask<EntraProvisioningDirectoryUser?> GetByObjectIdAsync(
        string registrationId,
        string objectId,
        CancellationToken cancellationToken = default);
}

public sealed record EntraProvisioningDirectoryUser(
    string ObjectId,
    string DisplayName,
    string? UserPrincipalName,
    string? Mail,
    string? UserType,
    bool AccountEnabled,
    EntraDirectoryAppertureStatus AppertureStatus,
    string? ProvisionedUserId);

public sealed record EntraProvisioningDirectoryPage(
    bool Success,
    string Summary,
    string? TechnicalDetail,
    IReadOnlyList<EntraProvisioningDirectoryUser> Items,
    string? ContinuationToken,
    string RegistrationId,
    string? RegistrationDisplayName);
