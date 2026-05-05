using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Configuration;

namespace Apperture.ControlPlane.Security;

public sealed class ManagedContactManagementService : IManagedContactManagementService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IManagedContactRepository _contacts;
    private readonly IManagedContactGroupRepository _groups;
    private readonly IEntraInstanceRegistrationRepository _entraRegistrations;
    private readonly IEntraDirectoryDiscoveryService _directory;
    private readonly IUserRepository _users;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;

    public ManagedContactManagementService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IManagedContactRepository contacts,
        IManagedContactGroupRepository groups,
        IEntraInstanceRegistrationRepository entraRegistrations,
        IEntraDirectoryDiscoveryService directory,
        IUserRepository users,
        IConfiguration configuration,
        TimeProvider time)
    {
        _principalContext = principalContext;
        _auth = auth;
        _contacts = contacts;
        _groups = groups;
        _entraRegistrations = entraRegistrations;
        _directory = directory;
        _users = users;
        _configuration = configuration;
        _time = time;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ManagedContactSummary>> ListAsync(
        string? query,
        int take,
        CancellationToken cancellationToken = default)
    {
        await RequireManagedContactReadAsync(cancellationToken);
        var rows = await _contacts.SearchAsync(query, take, cancellationToken).ConfigureAwait(false);
        return rows.Select(ToSummary).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<ManagedContactDetail?> GetDetailAsync(string id, CancellationToken cancellationToken = default)
    {
        await RequireManagedContactReadAsync(cancellationToken);
        var row = await _contacts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return null;
        var links = await _contacts.ListLinksForContactAsync(row.Id, cancellationToken).ConfigureAwait(false);
        return ToDetail(row, links);
    }

    /// <inheritdoc />
    public async ValueTask<ManagedContactDetail> CreateCustomAsync(
        ManagedContactCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        await RequireManagedContactManageAsync(cancellationToken);
        var now = _time.GetUtcNow();
        if (string.IsNullOrWhiteSpace(command.DisplayName))
            throw new InvalidOperationException("Display name is required.");
        if (!string.IsNullOrWhiteSpace(command.EmailAddress))
        {
            var other = await _contacts.FindByEmailAsync(command.EmailAddress, cancellationToken).ConfigureAwait(false);
            if (other is not null)
                throw new InvalidOperationException("Another managed contact already uses this email address.");
        }

        var id = await _contacts.CreateCustomAsync(
            command.DisplayName,
            command.EmailAddress,
            command.IsEnabled,
            now,
            cancellationToken).ConfigureAwait(false);
        var row = await _contacts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (row is null)
            throw new InvalidOperationException("Failed to create managed contact.");
        var links = await _contacts.ListLinksForContactAsync(id, cancellationToken).ConfigureAwait(false);
        return ToDetail(row, links);
    }

    /// <inheritdoc />
    public async ValueTask<ManagedContactDetail?> UpdateCustomAsync(
        string id,
        ManagedContactUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        await RequireManagedContactManageAsync(cancellationToken);
        var now = _time.GetUtcNow();
        if (string.IsNullOrWhiteSpace(command.DisplayName))
            throw new InvalidOperationException("Display name is required.");
        if (!string.IsNullOrWhiteSpace(command.EmailAddress))
        {
            var other = await _contacts.FindByEmailAsync(command.EmailAddress, cancellationToken).ConfigureAwait(false);
            if (other is not null && !string.Equals(other.Id, id.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("Another managed contact already uses this email address.");
        }

        await _contacts.UpdateContactAsync(
            id,
            command.DisplayName,
            command.EmailAddress,
            command.IsEnabled,
            now,
            cancellationToken).ConfigureAwait(false);
        return await GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ManagedContactDetail?> SetEnabledAsync(
        string id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await RequireManagedContactManageAsync(cancellationToken);
        var row = await _contacts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return null;
        var now = _time.GetUtcNow();
        await _contacts.SetIsEnabledAsync(id, isEnabled, now, cancellationToken).ConfigureAwait(false);
        return await GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ImportEntraUsersResult> ImportEntraUsersAsync(
        string entraRegistrationId,
        IReadOnlyList<string> entraUserObjectIds,
        CancellationToken cancellationToken = default)
    {
        await RequireManagedContactImportAsync(cancellationToken);
        var ctx = await ResolveDiscoveryContextAsync(entraRegistrationId, cancellationToken).ConfigureAwait(false);
        if (ctx is null)
            throw new InvalidOperationException("Entra registration was not found.");

        var results = new List<ImportEntraUserItemResult>();
        foreach (var oidRaw in entraUserObjectIds)
        {
            var oid = oidRaw.Trim();
            if (oid.Length == 0)
            {
                results.Add(new ImportEntraUserItemResult("", false, null, "Empty object id."));
                continue;
            }

            var graph = await _directory.GetUserByIdAsync(ctx, oid, cancellationToken).ConfigureAwait(false);
            if (!graph.Success)
            {
                results.Add(new ImportEntraUserItemResult(oid, false, null, graph.Summary));
                continue;
            }

            if (graph.Data is null)
            {
                results.Add(new ImportEntraUserItemResult(oid, false, null, "User was not found in the directory."));
                continue;
            }

            var u = graph.Data;
            var existingLink = await _contacts.FindIdentityLinkByEntraObjectIdAsync(oid, cancellationToken).ConfigureAwait(false);
            var mail = string.IsNullOrWhiteSpace(u.Mail) ? null : u.Mail.Trim();
            if (existingLink is null && mail is not null)
            {
                var emailOwner = await _contacts.FindByEmailAsync(mail, cancellationToken).ConfigureAwait(false);
                if (emailOwner is not null)
                {
                    results.Add(new ImportEntraUserItemResult(
                        oid,
                        false,
                        null,
                        $"This directory user's mail matches managed contact '{emailOwner.Id}', which is not linked to this Entra principal. Resolve the conflict before import."));
                    continue;
                }
            }

            if (existingLink is not null && mail is not null)
            {
                var emailOwner = await _contacts.FindByEmailAsync(mail, cancellationToken).ConfigureAwait(false);
                if (emailOwner is not null && !string.Equals(emailOwner.Id, existingLink.ManagedContactId, StringComparison.Ordinal))
                {
                    results.Add(new ImportEntraUserItemResult(
                        oid,
                        false,
                        null,
                        $"Updated mail would match another managed contact ({emailOwner.Id})."));
                    continue;
                }
            }

            try
            {
                var now = _time.GetUtcNow();
                var upsert = await _contacts.UpsertFromEntraDirectoryUserAsync(
                    u.DisplayName,
                    u.Mail,
                    u.UserPrincipalName,
                    u.ObjectId,
                    now,
                    cancellationToken).ConfigureAwait(false);
                results.Add(new ImportEntraUserItemResult(oid, true, upsert.ManagedContactId, null));
            }
            catch (Exception ex)
            {
                results.Add(new ImportEntraUserItemResult(oid, false, null, ex.Message));
            }
        }

        return new ImportEntraUsersResult(results);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ManagedContactGroupSummary>> ImportEntraGroupsAsync(
        string entraRegistrationId,
        IReadOnlyList<string> entraGroupObjectIds,
        CancellationToken cancellationToken = default)
    {
        await RequireManagedContactImportAsync(cancellationToken);
        var ctx = await ResolveDiscoveryContextAsync(entraRegistrationId, cancellationToken).ConfigureAwait(false);
        if (ctx is null)
            throw new InvalidOperationException("Entra registration was not found.");

        var list = new List<ManagedContactGroupSummary>();
        foreach (var gidRaw in entraGroupObjectIds)
        {
            var gid = gidRaw.Trim();
            if (gid.Length == 0)
                continue;
            var graph = await _directory.GetGroupByIdAsync(ctx, gid, cancellationToken).ConfigureAwait(false);
            if (!graph.Success || graph.Data is null)
                continue;
            var g = graph.Data;
            var now = _time.GetUtcNow();
            var row = await _groups.UpsertEntraGroupAsync(g.ObjectId, g.DisplayName, now, cancellationToken).ConfigureAwait(false);
            list.Add(new ManagedContactGroupSummary(row.Id, row.DisplayName, row.EntraGroupObjectId, row.UpdatedAtUtc));
        }

        return list;
    }

    /// <inheritdoc />
    public async ValueTask<ManagedContactDetail?> LinkToUserAsync(
        string managedContactId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await RequireManagedContactManageAsync(cancellationToken);
        await RequireUserReadAsync(cancellationToken);
        var contact = await _contacts.GetByIdAsync(managedContactId, cancellationToken).ConfigureAwait(false);
        if (contact is null)
            return null;
        var user = await _users.GetByIdAsync(new UserId(userId.Trim()), cancellationToken).ConfigureAwait(false);
        if (user is null)
            throw new InvalidOperationException("Platform user was not found.");
        var now = _time.GetUtcNow();
        await _contacts.SetLinkedUserIdAsync(contact.Id, user.Id.Value, now, cancellationToken).ConfigureAwait(false);
        return await GetDetailAsync(contact.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ManagedContactDetail?> UnlinkUserAsync(string managedContactId, CancellationToken cancellationToken = default)
    {
        await RequireManagedContactManageAsync(cancellationToken);
        var contact = await _contacts.GetByIdAsync(managedContactId, cancellationToken).ConfigureAwait(false);
        if (contact is null)
            return null;
        var now = _time.GetUtcNow();
        await _contacts.SetLinkedUserIdAsync(contact.Id, null, now, cancellationToken).ConfigureAwait(false);
        return await GetDetailAsync(contact.Id, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<EntraDirectoryDiscoveryContext?> ResolveDiscoveryContextAsync(
        string registrationId,
        CancellationToken cancellationToken)
    {
        var reg = await _entraRegistrations.GetByIdAsync(registrationId.Trim(), cancellationToken).ConfigureAwait(false);
        if (reg is null)
            return null;
        var secret = _configuration[EntraAuthOptions.SectionName + ":" + nameof(EntraAuthOptions.ClientSecret)] ?? "";
        var tenantId = EntraDirectoryTenantResolver.TryResolveGraphTenantId(reg) ?? "";
        return new EntraDirectoryDiscoveryContext(tenantId, reg.ClientId, secret, reg.Id);
    }

    private ManagedContactSummary ToSummary(ManagedContactRepositoryRow row)
    {
        var linkStatus = string.IsNullOrWhiteSpace(row.LinkedUserId)
            ? UserLinkStatusValues.None
            : UserLinkStatusValues.Linked;
        var usability = ComputeUsability(row.IsEnabled, row.EmailAddress);
        return new ManagedContactSummary(
            row.Id,
            row.DisplayName,
            row.EmailAddress,
            row.Source,
            row.IsEnabled,
            linkStatus,
            usability,
            row.UpdatedAtUtc);
    }

    private static ManagedContactDetail ToDetail(
        ManagedContactRepositoryRow row,
        IReadOnlyList<ManagedContactIdentityLinkRepositoryRow> links)
    {
        var linkStatus = string.IsNullOrWhiteSpace(row.LinkedUserId)
            ? UserLinkStatusValues.None
            : UserLinkStatusValues.Linked;
        var usability = ComputeUsability(row.IsEnabled, row.EmailAddress);
        var linkDtos = links.Select(l => new ManagedContactIdentityLinkDetail(
            l.Id,
            l.Provider,
            l.ExternalObjectId,
            l.ExternalPrincipalName,
            l.LastSyncedAtUtc,
            l.LinkStatus,
            l.UpdatedAtUtc)).ToList();
        return new ManagedContactDetail(
            row.Id,
            row.DisplayName,
            row.EmailAddress,
            row.Source,
            row.IsEnabled,
            row.LinkedUserId,
            linkStatus,
            usability,
            linkDtos,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
    }

    private static string ComputeUsability(bool isEnabled, string? email)
    {
        if (!isEnabled)
            return NotificationUsabilityStatusValues.ContactDisabled;
        if (string.IsNullOrWhiteSpace(email))
            return NotificationUsabilityStatusValues.MissingEmail;
        return NotificationUsabilityStatusValues.Deliverable;
    }

    private async ValueTask RequireManagedContactReadAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.ManagedContactRead, new ResourceDescriptor(ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance)),
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to read managed contacts.");
    }

    private async ValueTask RequireManagedContactManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.ManagedContactManage, new ResourceDescriptor(ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance)),
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage managed contacts.");
    }

    private async ValueTask RequireManagedContactImportAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.ManagedContactImport, new ResourceDescriptor(ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance)),
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to import directory principals into managed contacts.");
    }

    private async ValueTask RequireUserReadAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to read users for linking.");
    }
}