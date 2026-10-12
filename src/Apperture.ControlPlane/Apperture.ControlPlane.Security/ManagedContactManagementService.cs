using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
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
    private readonly ISecretResolver? _secretResolver;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public ManagedContactManagementService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IManagedContactRepository contacts,
        IManagedContactGroupRepository groups,
        IEntraInstanceRegistrationRepository entraRegistrations,
        IEntraDirectoryDiscoveryService directory,
        IUserRepository users,
        IConfiguration configuration,
        TimeProvider time,
        ISecretResolver? secretResolver = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
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
        _secretResolver = secretResolver;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Managed contact changes require the audited unit of work.");

    private static string SourceName(string source) =>
        string.Equals(source, ManagedContactSources.Entra, StringComparison.OrdinalIgnoreCase) ? "entra" : "custom";

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
    /// <remarks>
    /// <para><b>One audited unit</b> (AUD-1.4 slice 6): the management check and the shape checks answer a refused request before the
    /// unit opens. Inside the attempt, the email lock (so the address check cannot race another writer), the check that no other
    /// contact has the address, the row and <c>managed_contact.created</c>. The contact is a third party: its address and name are
    /// never recorded, only whether it has an address. After a lost acknowledgement the contact is found by the committed row's
    /// target.</para>
    /// </remarks>
    public async ValueTask<ManagedContactDetail> CreateCustomAsync(
        ManagedContactCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        await RequireManagedContactManageAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(command.DisplayName))
            throw new InvalidOperationException("Display name is required.");

        var id = await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ManagedContactCreated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                await _contacts.LockEmailNamespaceAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(command.EmailAddress))
                {
                    var other = await _contacts.FindByEmailAsync(command.EmailAddress, ct).ConfigureAwait(false);
                    if (other is not null)
                        throw new InvalidOperationException("Another managed contact already uses this email address.");
                }

                var created = await _contacts.CreateCustomAsync(
                    command.DisplayName, command.EmailAddress, command.IsEnabled, _time.GetUtcNow(), ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.ManagedContactCreated(
                        created, "custom", "admin", command.IsEnabled, hasEmail: !string.IsNullOrWhiteSpace(command.EmailAddress)),
                    ct).ConfigureAwait(false);
                return created;
            },
            (committed, _) => Task.FromResult(committed.TargetId ?? throw new InvalidOperationException("The committed row names no contact.")),
            cancellationToken).ConfigureAwait(false);

        var row = await _contacts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (row is null)
            throw new InvalidOperationException("Failed to create managed contact.");
        var links = await _contacts.ListLinksForContactAsync(id, cancellationToken).ConfigureAwait(false);
        return ToDetail(row, links);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit with the email lock and the contact row locked. The change is computed against the row as locked: the name,
    /// the address and the enabled flag that differ are named in <c>managed_contact.updated</c> (never their values), and a request
    /// that changes nothing declares a no-op and records nothing. A contact that does not exist, or is not a custom contact, is
    /// refused as before.
    /// </remarks>
    public async ValueTask<ManagedContactDetail?> UpdateCustomAsync(
        string id,
        ManagedContactUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        await RequireManagedContactManageAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(command.DisplayName))
            throw new InvalidOperationException("Display name is required.");

        var trimmedId = id.Trim();
        await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ManagedContactUpdated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                await _contacts.LockEmailNamespaceAsync(ct).ConfigureAwait(false);
                var row = await _contacts.GetByIdForUpdateAsync(trimmedId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Managed contact not found.");
                if (!string.Equals(row.Source, ManagedContactSources.Custom, StringComparison.Ordinal))
                    throw new InvalidOperationException("Only custom contacts can be edited through this path.");

                if (!string.IsNullOrWhiteSpace(command.EmailAddress))
                {
                    var other = await _contacts.FindByEmailAsync(command.EmailAddress, ct).ConfigureAwait(false);
                    if (other is not null && !string.Equals(other.Id, trimmedId, StringComparison.Ordinal))
                        throw new InvalidOperationException("Another managed contact already uses this email address.");
                }

                var newName = command.DisplayName.Trim();
                var newEmail = string.IsNullOrWhiteSpace(command.EmailAddress) ? null : command.EmailAddress.Trim();
                var changed = new List<string>();
                if (!string.Equals(row.DisplayName, newName, StringComparison.Ordinal))
                    changed.Add("displayName");
                if (!string.Equals(row.EmailAddress, newEmail, StringComparison.Ordinal))
                    changed.Add("email");
                if (row.IsEnabled != command.IsEnabled)
                    changed.Add("enabled");
                if (changed.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await _contacts.UpdateContactAsync(trimmedId, command.DisplayName, command.EmailAddress, command.IsEnabled, _time.GetUtcNow(), ct)
                    .ConfigureAwait(false);
                changed.Sort(StringComparer.Ordinal);
                await transaction.StageAsync(
                    AuditEvents.ManagedContactUpdated(trimmedId, SourceName(row.Source), changed, command.IsEnabled),
                    ct).ConfigureAwait(false);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
        return await GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit with the contact row locked. Setting the state a contact already has changes nothing and records nothing;
    /// otherwise <c>managed_contact.updated</c> names the enabled flag. Allowed for any source, as before.
    /// </remarks>
    public async ValueTask<ManagedContactDetail?> SetEnabledAsync(
        string id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        await RequireManagedContactManageAsync(cancellationToken);
        var early = await _contacts.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (early is null)
            return null;

        await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ManagedContactUpdated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var row = await _contacts.GetByIdForUpdateAsync(early.Id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Managed contact not found.");
                if (row.IsEnabled == isEnabled)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await _contacts.SetIsEnabledAsync(early.Id, isEnabled, _time.GetUtcNow(), ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.ManagedContactUpdated(early.Id, SourceName(row.Source), ["enabled"], isEnabled),
                    ct).ConfigureAwait(false);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
        return await GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>Each directory user is its own audited unit, so a batch keeps its per-item results (a failed item does not undo the
    /// others) and each item is recorded with the contact it created or changed: <c>managed_contact.created</c> (via
    /// <c>entra_import</c>) or <c>managed_contact.updated</c> naming what differed (the name, the address, the directory link).
    /// The directory is read before the unit, never inside it. Inside the attempt: the email lock, the same address checks as
    /// before, the upsert and the comparison of the row before and after. A user whose contact already matches the directory
    /// declares a no-op and records nothing; its sync timestamp is refreshed afterwards, outside the unit.</para>
    /// </remarks>
    public async ValueTask<ImportEntraUsersResult> ImportEntraUsersAsync(
        string entraRegistrationId,
        IReadOnlyList<string> entraUserObjectIds,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
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
                results.Add(new ImportEntraUserItemResult(
                    "",
                    false,
                    null,
                    EntraUserImportOutcomeCodes.InvalidObjectId,
                    "Empty object id."));
                continue;
            }

            var graph = await _directory.GetUserByIdAsync(ctx, oid, cancellationToken).ConfigureAwait(false);
            if (!graph.Success)
            {
                results.Add(new ImportEntraUserItemResult(
                    oid,
                    false,
                    null,
                    EntraUserImportOutcomeCodes.DirectoryError,
                    graph.Summary));
                continue;
            }

            if (graph.Data is null)
            {
                results.Add(new ImportEntraUserItemResult(
                    oid,
                    false,
                    null,
                    EntraUserImportOutcomeCodes.UserNotFoundInDirectory,
                    "User was not found in the directory."));
                continue;
            }

            var u = graph.Data;
            var mail = string.IsNullOrWhiteSpace(u.Mail) ? null : u.Mail.Trim();
            try
            {
                var attempt = await unit.ExecuteAsync(
                    new AuditedOperation(
                        AuditActionNames.ManagedContactCreated,
                        AuditActor.CurrentPrincipal,
                        [AuditActionNames.ManagedContactUpdated]),
                    (transaction, ct) => ImportOneUserAsync(transaction, u, oid, mail, ct),
                    (committed, _) => Task.FromResult(new ImportAttempt(
                        new ImportEntraUserItemResult(
                            oid,
                            true,
                            committed.TargetId,
                            string.Equals(committed.Action, AuditActionNames.ManagedContactCreated, StringComparison.Ordinal)
                                ? EntraUserImportOutcomeCodes.Success
                                : EntraUserImportOutcomeCodes.AlreadyLinked,
                            null),
                        RefreshSync: false)),
                    cancellationToken).ConfigureAwait(false);

                if (attempt.RefreshSync)
                {
                    // Nothing about the contact differs, so nothing was recorded; the sync timestamp is operational state that is
                    // refreshed on its own, as it always was.
                    try
                    {
                        await _contacts.TouchEntraLinkSyncAsync(u.ObjectId, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Best effort: the import itself changed nothing and succeeded.
                    }
                }

                results.Add(attempt.Result);
            }
            catch (AuditInvariantException)
            {
                throw;
            }
            catch (AuditOutcomeUnknownException ex)
            {
                results.Add(new ImportEntraUserItemResult(
                    oid,
                    false,
                    null,
                    EntraUserImportOutcomeCodes.ImportFailed,
                    $"The outcome of this item could not be established (operation {ex.OperationEventId}); check the contact list before importing it again."));
            }
            catch (Exception ex)
            {
                results.Add(new ImportEntraUserItemResult(
                    oid,
                    false,
                    null,
                    EntraUserImportOutcomeCodes.ImportFailed,
                    ex.Message));
            }
        }

        return new ImportEntraUsersResult(results);
    }

    private sealed record ImportAttempt(ImportEntraUserItemResult Result, bool RefreshSync);

    private async Task<ImportAttempt> ImportOneUserAsync(
        IAuditedTransaction transaction,
        EntraDirectoryUser u,
        string oid,
        string? mail,
        CancellationToken ct)
    {
        await _contacts.LockEmailNamespaceAsync(ct).ConfigureAwait(false);
        var existingLink = await _contacts.FindIdentityLinkByEntraObjectIdAsync(oid, ct).ConfigureAwait(false);
        if (existingLink is null && mail is not null)
        {
            var emailOwner = await _contacts.FindByEmailAsync(mail, ct).ConfigureAwait(false);
            if (emailOwner is not null)
            {
                transaction.DeclareNoOp();
                return new ImportAttempt(
                    new ImportEntraUserItemResult(
                        oid,
                        false,
                        null,
                        EntraUserImportOutcomeCodes.EmailConflictExistingContact,
                        $"This directory user's mail matches managed contact '{emailOwner.Id}', which is not linked to this Entra principal. Resolve the conflict before import."),
                    RefreshSync: false);
            }
        }

        if (existingLink is not null && mail is not null)
        {
            var emailOwner = await _contacts.FindByEmailAsync(mail, ct).ConfigureAwait(false);
            if (emailOwner is not null && !string.Equals(emailOwner.Id, existingLink.ManagedContactId, StringComparison.Ordinal))
            {
                transaction.DeclareNoOp();
                return new ImportAttempt(
                    new ImportEntraUserItemResult(
                        oid,
                        false,
                        null,
                        EntraUserImportOutcomeCodes.EmailConflictExistingContact,
                        $"Updated mail would match another managed contact ({emailOwner.Id})."),
                    RefreshSync: false);
            }
        }

        var before = existingLink is null
            ? null
            : await _contacts.GetByIdForUpdateAsync(existingLink.ManagedContactId, ct).ConfigureAwait(false);
        var upsert = await _contacts.UpsertFromEntraDirectoryUserAsync(
            u.DisplayName, u.Mail, u.UserPrincipalName, u.ObjectId, _time.GetUtcNow(), ct).ConfigureAwait(false);

        if (upsert.Created)
        {
            await transaction.StageAsync(
                AuditEvents.ManagedContactCreated(upsert.ManagedContactId, "entra", "entra_import", enabled: true, hasEmail: mail is not null),
                ct).ConfigureAwait(false);
            return new ImportAttempt(
                new ImportEntraUserItemResult(oid, true, upsert.ManagedContactId, EntraUserImportOutcomeCodes.Success, null),
                RefreshSync: false);
        }

        var after = await _contacts.GetByIdAsync(upsert.ManagedContactId, ct).ConfigureAwait(false);
        var changed = new List<string>();
        if (before is null || after is null)
        {
            changed.Add("entraSync");
        }
        else
        {
            if (!string.Equals(before.DisplayName, after.DisplayName, StringComparison.Ordinal))
                changed.Add("displayName");
            if (!string.Equals(before.EmailAddress, after.EmailAddress, StringComparison.Ordinal))
                changed.Add("email");
            if (!string.Equals(before.Source, after.Source, StringComparison.Ordinal)
                || existingLink is null
                || !string.Equals(existingLink.ExternalPrincipalName, upsert.Link.ExternalPrincipalName, StringComparison.Ordinal)
                || !string.Equals(existingLink.LinkStatus, upsert.Link.LinkStatus, StringComparison.Ordinal))
            {
                changed.Add("entraSync");
            }
        }

        var alreadyLinked = new ImportEntraUserItemResult(oid, true, upsert.ManagedContactId, EntraUserImportOutcomeCodes.AlreadyLinked, null);
        if (changed.Count == 0)
        {
            // Rolls the timestamp refresh back with the unit; the caller refreshes it on its own.
            transaction.DeclareNoOp();
            return new ImportAttempt(alreadyLinked, RefreshSync: true);
        }

        changed.Sort(StringComparer.Ordinal);
        await transaction.StageAsync(
            AuditEvents.ManagedContactUpdated(upsert.ManagedContactId, "entra", changed, after?.IsEnabled ?? before?.IsEnabled ?? true),
            ct).ConfigureAwait(false);
        return new ImportAttempt(alreadyLinked, RefreshSync: false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each group is its own audited unit: <c>managed_contact.group_imported</c> records that a group's directory metadata row was
    /// created or refreshed (the group's target is the directory row; its name is not recorded). A group whose stored name already
    /// matches declares a no-op and records nothing. A group the directory cannot return is skipped, as before.
    /// </remarks>
    public async ValueTask<IReadOnlyList<ManagedContactGroupSummary>> ImportEntraGroupsAsync(
        string entraRegistrationId,
        IReadOnlyList<string> entraGroupObjectIds,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
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
            var summary = await unit.ExecuteAsync(
                new AuditedOperation(AuditActionNames.ManagedContactGroupImported, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    var existing = await _groups.FindByEntraGroupObjectIdAsync(g.ObjectId, ct).ConfigureAwait(false);
                    var newName = string.IsNullOrWhiteSpace(g.DisplayName) ? g.ObjectId.Trim() : g.DisplayName.Trim();
                    if (existing is not null && string.Equals(existing.DisplayName, newName, StringComparison.Ordinal))
                    {
                        transaction.DeclareNoOp();
                        return new ManagedContactGroupSummary(existing.Id, existing.DisplayName, existing.EntraGroupObjectId, existing.UpdatedAtUtc);
                    }

                    var row = await _groups.UpsertEntraGroupAsync(g.ObjectId, g.DisplayName, _time.GetUtcNow(), ct).ConfigureAwait(false);
                    await transaction.StageAsync(AuditEvents.ManagedContactGroupImported(row.Id, created: existing is null), ct).ConfigureAwait(false);
                    return new ManagedContactGroupSummary(row.Id, row.DisplayName, row.EntraGroupObjectId, row.UpdatedAtUtc);
                },
                async (committed, ct) =>
                {
                    var row = await _groups.FindByEntraGroupObjectIdAsync(g.ObjectId, ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The imported group could not be read back.");
                    return new ManagedContactGroupSummary(row.Id, row.DisplayName, row.EntraGroupObjectId, row.UpdatedAtUtc);
                },
                cancellationToken).ConfigureAwait(false);
            list.Add(summary);
        }

        return list;
    }

    /// <inheritdoc />
    /// <remarks>
    /// One audited unit with the contact row locked. Linking a contact to the user it already links to changes nothing and records
    /// nothing; otherwise <c>managed_contact.user_linked</c> names the user (by id) and the user it was linked to before.
    /// </remarks>
    public async ValueTask<ManagedContactDetail?> LinkToUserAsync(
        string managedContactId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        await RequireManagedContactManageAsync(cancellationToken);
        await RequireUserReadAsync(cancellationToken);
        var contact = await _contacts.GetByIdAsync(managedContactId, cancellationToken).ConfigureAwait(false);
        if (contact is null)
            return null;
        var user = await _users.GetByIdAsync(new UserId(userId.Trim()), cancellationToken).ConfigureAwait(false);
        if (user is null)
            throw new InvalidOperationException("Platform user was not found.");

        await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ManagedContactUserLinked, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var row = await _contacts.GetByIdForUpdateAsync(contact.Id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Managed contact not found.");
                if (await _users.GetByIdAsync(user.Id, ct).ConfigureAwait(false) is null)
                    throw new InvalidOperationException("Platform user was not found.");
                if (string.Equals(row.LinkedUserId, user.Id.Value, StringComparison.Ordinal))
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await _contacts.SetLinkedUserIdAsync(row.Id, user.Id.Value, _time.GetUtcNow(), ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.ManagedContactUserLinked(row.Id, user.Id.Value, string.IsNullOrWhiteSpace(row.LinkedUserId) ? null : row.LinkedUserId),
                    ct).ConfigureAwait(false);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
        return await GetDetailAsync(contact.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>One audited unit with the contact row locked. A contact that is not linked changes nothing and records nothing.</remarks>
    public async ValueTask<ManagedContactDetail?> UnlinkUserAsync(string managedContactId, CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        await RequireManagedContactManageAsync(cancellationToken);
        var contact = await _contacts.GetByIdAsync(managedContactId, cancellationToken).ConfigureAwait(false);
        if (contact is null)
            return null;

        await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.ManagedContactUserUnlinked, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var row = await _contacts.GetByIdForUpdateAsync(contact.Id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Managed contact not found.");
                if (string.IsNullOrWhiteSpace(row.LinkedUserId))
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                await _contacts.SetLinkedUserIdAsync(row.Id, null, _time.GetUtcNow(), ct).ConfigureAwait(false);
                await transaction.StageAsync(AuditEvents.ManagedContactUserUnlinked(row.Id, row.LinkedUserId), ct).ConfigureAwait(false);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
        return await GetDetailAsync(contact.Id, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<EntraDirectoryDiscoveryContext?> ResolveDiscoveryContextAsync(
        string registrationId,
        CancellationToken cancellationToken)
    {
        var reg = await _entraRegistrations.GetByIdAsync(registrationId.Trim(), cancellationToken).ConfigureAwait(false);
        if (reg is null)
            return null;
        var secret = await EntraClientSecretAccess
            .TryResolveAsync(
                reg,
                _secretResolver,
                _configuration[EntraAuthOptions.SectionName + ":" + nameof(EntraAuthOptions.ClientSecret)],
                cancellationToken)
            .ConfigureAwait(false) ?? "";
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