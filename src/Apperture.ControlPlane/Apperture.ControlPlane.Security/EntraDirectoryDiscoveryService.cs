using Apperture.Core.Application.Services;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Microsoft Graph directory browse for Entra (discovery only; does not create platform users).
/// </summary>
public sealed class EntraDirectoryDiscoveryService : IEntraDirectoryDiscoveryService
{
    private const int MaxTop = 100;

    public async Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
        EntraDirectoryDiscoveryContext context,
        string? search,
        int top,
        CancellationToken cancellationToken = default)
    {
        top = Math.Clamp(top, 1, MaxTop);
        var cfg = ValidateContext(context);
        if (cfg is not null)
            return Failed<IReadOnlyList<EntraDirectoryUser>>(DiscoveryFailureCategory.Configuration, cfg);

        try
        {
            var graph = CreateGraphClient(context);
            var users = await graph.Users.GetAsync(
                requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Top = top;
                    requestConfiguration.QueryParameters.Select =
                        ["id", "displayName", "userPrincipalName", "mail"];
                    var filter = BuildUserFilter(search);
                    if (!string.IsNullOrEmpty(filter))
                        requestConfiguration.QueryParameters.Filter = filter;
                },
                cancellationToken).ConfigureAwait(false);

            var list = users?.Value ?? [];
            var mapped = list
                .Where(u => u.Id is not null)
                .Select(u => new EntraDirectoryUser(
                    u.Id!,
                    u.DisplayName ?? "",
                    u.UserPrincipalName,
                    u.Mail))
                .ToList();

            return new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>(
                true,
                DiscoveryFailureCategory.None,
                "Users retrieved from Microsoft Graph.",
                null,
                mapped);
        }
        catch (ApiException ex)
        {
            return MapGraphException<IReadOnlyList<EntraDirectoryUser>>(ex, "user search");
        }
        catch (Exception ex)
        {
            return Transient<IReadOnlyList<EntraDirectoryUser>>(ex, "user search");
        }
    }

    public async Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(
        EntraDirectoryDiscoveryContext context,
        string? search,
        int top,
        CancellationToken cancellationToken = default)
    {
        top = Math.Clamp(top, 1, MaxTop);
        var cfg = ValidateContext(context);
        if (cfg is not null)
            return Failed<IReadOnlyList<EntraDirectoryGroup>>(DiscoveryFailureCategory.Configuration, cfg);

        try
        {
            var graph = CreateGraphClient(context);
            var groups = await graph.Groups.GetAsync(
                requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Top = top;
                    requestConfiguration.QueryParameters.Select =
                        ["id", "displayName", "mailNickname", "description"];
                    var filter = BuildGroupFilter(search);
                    if (!string.IsNullOrEmpty(filter))
                        requestConfiguration.QueryParameters.Filter = filter;
                },
                cancellationToken).ConfigureAwait(false);

            var list = groups?.Value ?? [];
            var mapped = list
                .Where(g => g.Id is not null)
                .Select(g => new EntraDirectoryGroup(
                    g.Id!,
                    g.DisplayName ?? "",
                    g.MailNickname,
                    g.Description))
                .ToList();

            return new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>(
                true,
                DiscoveryFailureCategory.None,
                "Groups retrieved from Microsoft Graph.",
                null,
                mapped);
        }
        catch (ApiException ex)
        {
            return MapGraphException<IReadOnlyList<EntraDirectoryGroup>>(ex, "group search");
        }
        catch (Exception ex)
        {
            return Transient<IReadOnlyList<EntraDirectoryGroup>>(ex, "group search");
        }
    }

    private static GraphServiceClient CreateGraphClient(EntraDirectoryDiscoveryContext context)
    {
        var credential = new ClientSecretCredential(context.TenantId, context.ClientId, context.ClientSecret);
        var scopes = new[] { "https://graph.microsoft.com/.default" };
        return new GraphServiceClient(credential, scopes);
    }

    private static string? ValidateContext(EntraDirectoryDiscoveryContext context)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return "Azure tenant id could not be resolved. Set Azure tenant id on the registration or use an authority URL like https://login.microsoftonline.com/{tenant-id}.";

        if (string.IsNullOrWhiteSpace(context.ClientId))
            return "Client ID is missing.";

        if (string.IsNullOrWhiteSpace(context.ClientSecret))
            return "Client secret is not available. Configure Entra:ClientSecret (or KeyVault) for directory discovery.";

        return null;
    }

    private static string? BuildUserFilter(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;
        var q = ODataEscape(search.Trim());
        return
            $"startswith(displayName,'{q}') or startswith(mail,'{q}') or startswith(userPrincipalName,'{q}')";
    }

    private static string? BuildGroupFilter(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;
        var q = ODataEscape(search.Trim());
        return $"startswith(displayName,'{q}') or startswith(mailNickname,'{q}')";
    }

    private static string ODataEscape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static DirectoryDiscoveryResult<T> Failed<T>(DiscoveryFailureCategory category, string summary) =>
        new(false, category, summary, null, default);

    private static DirectoryDiscoveryResult<T> MapGraphException<T>(ApiException ex, string operation)
    {
        var msg = ex.Message;
        var status = (int)ex.ResponseStatusCode;
        if (status is 401 or 403 || msg.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
        {
            return new DirectoryDiscoveryResult<T>(
                false,
                DiscoveryFailureCategory.Permissions,
                "Microsoft Graph denied access. Ensure the app registration has application permissions (User.Read.All, Group.Read.All) and admin consent.",
                $"HTTP {status}, message={msg}");
        }

        if (status == 429 || msg.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase))
        {
            return new DirectoryDiscoveryResult<T>(
                false,
                DiscoveryFailureCategory.Throttled,
                "Microsoft Graph rate-limited the request. Wait briefly and try again.",
                msg);
        }

        if (status is >= 500)
        {
            return new DirectoryDiscoveryResult<T>(
                false,
                DiscoveryFailureCategory.Transient,
                "Microsoft Graph returned a server error. Try again later.",
                msg);
        }

        return new DirectoryDiscoveryResult<T>(
            false,
            DiscoveryFailureCategory.Configuration,
            $"Microsoft Graph request failed during {operation}. Check tenant id, client id, and secret.",
            $"HTTP {status}, message={msg}");
    }

    private static DirectoryDiscoveryResult<T> Transient<T>(Exception ex, string operation) =>
        new(
            false,
            DiscoveryFailureCategory.Transient,
            $"Unexpected error during {operation}.",
            ex.Message);
}
