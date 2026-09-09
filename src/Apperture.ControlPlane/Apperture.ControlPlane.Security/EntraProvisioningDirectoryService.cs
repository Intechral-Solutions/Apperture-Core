using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Configuration;

namespace Apperture.ControlPlane.Security;

public sealed class EntraProvisioningDirectoryService : IEntraProvisioningDirectoryService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IEntraInstanceRegistrationRepository _registrations;
    private readonly IEntraDirectoryDiscoveryService _discovery;
    private readonly IUserRepository _users;
    private readonly IEntraAccessRequestRepository _requests;
    private readonly ISecretResolver? _secrets;
    private readonly IConfiguration _configuration;

    public EntraProvisioningDirectoryService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IEntraInstanceRegistrationRepository registrations,
        IEntraDirectoryDiscoveryService discovery,
        IUserRepository users,
        IEntraAccessRequestRepository requests,
        IConfiguration configuration,
        ISecretResolver? secrets = null)
    {
        _principalContext = principalContext;
        _auth = auth;
        _registrations = registrations;
        _discovery = discovery;
        _users = users;
        _requests = requests;
        _configuration = configuration;
        _secrets = secrets;
    }

    public async ValueTask<EntraProvisioningDirectoryPage> SearchAsync(
        string registrationId,
        string? search,
        string? continuationToken,
        int top,
        CancellationToken cancellationToken = default)
    {
        top = Math.Clamp(top, 1, IEntraProvisioningDirectoryService.MaxPageSize);
        await RequireUserManageAsync(cancellationToken);
        var registration = await RequireRegistrationAsync(registrationId, cancellationToken);
        if (registration is null)
        {
            return new EntraProvisioningDirectoryPage(
                false,
                "That Microsoft directory registration was not found.",
                null,
                [],
                null,
                registrationId,
                null);
        }

        if (!registration.Enabled)
        {
            return new EntraProvisioningDirectoryPage(
                false,
                "That Microsoft directory registration is disabled.",
                null,
                [],
                null,
                registration.Id,
                registration.DisplayName);
        }

        string? skipToken = null;
        var query = search;
        if (!string.IsNullOrWhiteSpace(continuationToken)
            && EntraDirectoryContinuationCodec.TryDecode(continuationToken, registration.Id, out var storedSearch, out skipToken, out var storedTop))
        {
            query = storedSearch;
            top = storedTop;
        }
        else if (!string.IsNullOrWhiteSpace(continuationToken))
        {
            return new EntraProvisioningDirectoryPage(
                false,
                "That page of directory results is no longer valid. Search again.",
                null,
                [],
                null,
                registration.Id,
                registration.DisplayName);
        }

        var context = await TryCreateContextAsync(registration, cancellationToken);
        if (context is null)
        {
            return new EntraProvisioningDirectoryPage(
                false,
                "Microsoft Graph is not available for this registration. Check the client secret and tenant id.",
                null,
                [],
                null,
                registration.Id,
                registration.DisplayName);
        }

        var result = await _discovery.SearchUsersAsync(context, query, top, skipToken, cancellationToken);
        if (!result.Success)
        {
            return new EntraProvisioningDirectoryPage(
                false,
                result.Summary,
                result.TechnicalDetail,
                [],
                null,
                registration.Id,
                registration.DisplayName);
        }

        var items = await AnnotateAsync(registration.Id, result.Data ?? [], cancellationToken);
        var next = string.IsNullOrWhiteSpace(result.ContinuationToken)
            ? null
            : EntraDirectoryContinuationCodec.Encode(registration.Id, query, result.ContinuationToken, top);

        return new EntraProvisioningDirectoryPage(
            true,
            result.Summary,
            null,
            items,
            next,
            registration.Id,
            registration.DisplayName);
    }

    public async ValueTask<EntraProvisioningDirectoryUser?> GetByObjectIdAsync(
        string registrationId,
        string objectId,
        CancellationToken cancellationToken = default)
    {
        await RequireUserManageAsync(cancellationToken);
        var registration = await RequireRegistrationAsync(registrationId, cancellationToken);
        if (registration is null || !registration.Enabled)
            return null;

        var context = await TryCreateContextAsync(registration, cancellationToken);
        if (context is null)
            return null;

        var result = await _discovery.GetUserByIdAsync(context, objectId, cancellationToken);
        if (!result.Success || result.Data is null)
            return null;

        var annotated = await AnnotateAsync(registration.Id, [result.Data], cancellationToken);
        return annotated[0];
    }

    private async ValueTask<IReadOnlyList<EntraProvisioningDirectoryUser>> AnnotateAsync(
        string registrationId,
        IReadOnlyList<EntraDirectoryUser> users,
        CancellationToken cancellationToken)
    {
        var pending = await _requests.FindPendingByObjectIdsAsync(
            registrationId,
            users.Select(u => u.ObjectId).ToList(),
            cancellationToken);
        var pendingByOid = pending.ToDictionary(r => r.ProviderObjectId, StringComparer.OrdinalIgnoreCase);

        var mapped = new List<EntraProvisioningDirectoryUser>(users.Count);
        foreach (var user in users)
        {
            var owner = await _users.GetByIdentityLinkAsync(IdentitySource.Entra, user.ObjectId, cancellationToken);
            EntraDirectoryAppertureStatus status;
            string? provisionedUserId = null;
            if (owner is not null)
            {
                status = EntraDirectoryAppertureStatus.Provisioned;
                provisionedUserId = owner.Id.Value;
            }
            else if (pendingByOid.ContainsKey(user.ObjectId))
            {
                status = EntraDirectoryAppertureStatus.AccessRequestPending;
            }
            else
            {
                status = EntraDirectoryAppertureStatus.Available;
            }

            mapped.Add(new EntraProvisioningDirectoryUser(
                user.ObjectId,
                user.DisplayName,
                user.UserPrincipalName,
                user.Mail,
                user.UserType,
                user.AccountEnabled != false,
                status,
                provisionedUserId));
        }

        return mapped;
    }

    private async Task<EntraInstanceRegistration?> RequireRegistrationAsync(string registrationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registrationId))
            return null;
        return await _registrations.GetByIdAsync(registrationId.Trim(), cancellationToken);
    }

    private async Task<EntraDirectoryDiscoveryContext?> TryCreateContextAsync(
        EntraInstanceRegistration registration,
        CancellationToken cancellationToken)
    {
        var secret = await EntraClientSecretAccess.TryResolveAsync(
            registration,
            _secrets,
            _configuration[EntraAuthOptions.SectionName + ":" + nameof(EntraAuthOptions.ClientSecret)],
            cancellationToken) ?? "";
        var tenantId = EntraDirectoryTenantResolver.TryResolveGraphTenantId(registration) ?? "";
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(registration.ClientId))
            return null;
        return new EntraDirectoryDiscoveryContext(tenantId, registration.ClientId, secret, registration.Id);
    }

    private async ValueTask RequireUserManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.UserManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
            null, null, cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage users.");
    }
}
