using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications.Subscriptions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

public sealed class ManagedContactRecipientProvisioner
{
    private readonly NotificationRecipientAddressParser _parser;
    private readonly IManagedContactRepository _contacts;
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly TimeProvider _time;

    public ManagedContactRecipientProvisioner(
        NotificationRecipientAddressParser parser,
        IManagedContactRepository contacts,
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        TimeProvider time)
    {
        _parser = parser;
        _contacts = contacts;
        _principalContext = principalContext;
        _auth = auth;
        _time = time;
    }

    /// <summary>The managed contact a recipient input resolved to, and whether this call created it.</summary>
    public sealed record Resolved(string Id, bool Created);

    /// <summary>
    /// Resolves the contact an address names, creating it when allowed and missing, inside the caller's audited unit (AUD-1.4 slice
    /// 6): the contact, the subscription that names it and their audit rows commit or roll back together. The email lock is taken
    /// first, so the lookup and the insert cannot race another writer of the same address. Creating needs the management permission
    /// (checked only on the creating branch, as before); finding an existing contact does not. A creation stages
    /// <c>managed_contact.created</c> (via the subscription); the address and the name are never recorded.
    /// </summary>
    public async ValueTask<Resolved> ResolveOrCreateAsync(
        string managedContactInput,
        bool createIfMissing,
        IAuditedTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        if (!_parser.TryParse(managedContactInput, out var parsed, out var error) || parsed is null)
            throw new InvalidOperationException(error ?? "Invalid managed contact address.");

        await _contacts.LockEmailNamespaceAsync(cancellationToken).ConfigureAwait(false);
        var existing = await _contacts.FindByEmailAsync(parsed.Email, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return new Resolved(existing.Id, Created: false);

        if (!createIfMissing)
            throw new InvalidOperationException($"No managed contact found for {parsed.Email}.");

        await RequireManagedContactManageAsync(cancellationToken).ConfigureAwait(false);
        var displayName = parsed.DisplayName ?? parsed.Email;
        var now = _time.GetUtcNow();
        var id = await _contacts
            .CreateCustomAsync(displayName, parsed.Email, isEnabled: true, now, cancellationToken)
            .ConfigureAwait(false);
        await transaction.StageAsync(
            AuditEvents.ManagedContactCreated(id, "custom", "subscription", enabled: true, hasEmail: true),
            cancellationToken).ConfigureAwait(false);
        return new Resolved(id, Created: true);
    }

    private async ValueTask RequireManagedContactManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");

        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(
                ActionConstants.ManagedContactManage,
                new ResourceDescriptor(ResourceTypeConstants.ManagedContact, ScopeKeyConstants.Instance)),
            null,
            null,
            cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new UnauthorizedAccessException($"Not authorized: {ActionConstants.ManagedContactManage}.");
    }
}
