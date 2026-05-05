namespace Apperture.Core.Application.Services;

/// <summary>
/// Browse Entra directory objects via Microsoft Graph for operator discovery (not sync/import).
/// </summary>
public interface IEntraDirectoryDiscoveryService
{
    Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
        EntraDirectoryDiscoveryContext context,
        string? search,
        int top,
        CancellationToken cancellationToken = default);

    Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(
        EntraDirectoryDiscoveryContext context,
        string? search,
        int top,
        CancellationToken cancellationToken = default);
}
