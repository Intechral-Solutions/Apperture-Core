namespace Apperture.Core.Application.Services;

/// <summary>
/// Browse Entra directory objects via Microsoft Graph for operator discovery and stable import refresh by object id.
/// </summary>
public interface IEntraDirectoryDiscoveryService
{
    Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
        EntraDirectoryDiscoveryContext context,
        string? search,
        int top,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="SearchUsersAsync(EntraDirectoryDiscoveryContext,string?,int,CancellationToken)"/>
    /// with an opaque Graph skip token previously returned by this service. Callers must not pass a raw Graph URL.
    /// </summary>
    Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
        EntraDirectoryDiscoveryContext context,
        string? search,
        int top,
        string? skipToken,
        CancellationToken cancellationToken = default) =>
        SearchUsersAsync(context, search, top, cancellationToken);

    Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(
        EntraDirectoryDiscoveryContext context,
        string? search,
        int top,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a single user by Entra object id (same fields as search).</summary>
    Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(
        EntraDirectoryDiscoveryContext context,
        string objectId,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a single group by Entra object id (same fields as search).</summary>
    Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(
        EntraDirectoryDiscoveryContext context,
        string objectId,
        CancellationToken cancellationToken = default);
}
