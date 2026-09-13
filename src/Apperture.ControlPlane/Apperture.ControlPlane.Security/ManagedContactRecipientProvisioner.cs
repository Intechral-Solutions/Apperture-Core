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

    public async ValueTask<string> ResolveOrCreateManagedContactIdAsync(
        string managedContactInput,
        bool createIfMissing,
        CancellationToken cancellationToken = default)
    {
        if (!_parser.TryParse(managedContactInput, out var parsed, out var error) || parsed is null)
            throw new InvalidOperationException(error ?? "Invalid managed contact address.");

        var existing = await _contacts.FindByEmailAsync(parsed.Email, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing.Id;

        if (!createIfMissing)
            throw new InvalidOperationException($"No managed contact found for {parsed.Email}.");

        await RequireManagedContactManageAsync(cancellationToken).ConfigureAwait(false);
        var displayName = parsed.DisplayName ?? parsed.Email;
        var now = _time.GetUtcNow();
        return await _contacts
            .CreateCustomAsync(displayName, parsed.Email, isEnabled: true, now, cancellationToken)
            .ConfigureAwait(false);
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
