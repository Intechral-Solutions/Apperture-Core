namespace Apperture.Core.Domain.ResourceContexts;

/// <summary>
/// Thrown by the CTX-3A §6.5 reactivation guard when an operation would make a resource owned by an
/// archived ResourceContext live again (e.g. activating an Integration, or enabling a Schedule, while
/// its owning Application is archived), and by
/// <see cref="Apperture.Core.Application.ResourceContexts.ResourceContextService.ResolveForNewOwnershipAsync"/>
/// when a <i>new</i> resource is being created under an archived ResourceContext.
/// </summary>
/// <remarks>
/// Inherits <see cref="InvalidOperationException"/> so existing
/// <c>catch (InvalidOperationException)</c> call sites keep compiling and behaving as before. Its
/// purpose is to be a distinct, machine-checkable type: a later plan's controller needs to map this
/// specific rejection to 409 (the controllers today map ordinary <see cref="InvalidOperationException"/>
/// to 404 or 400, and discard or shed the message in the process -- see CTX-3A finding I1) without
/// resorting to string-matching a message.
/// </remarks>
/// <remarks>
/// Lives in <c>Apperture.Core.Domain</c> (alongside <see cref="ResourceContextId"/>) rather than
/// <c>Apperture.ControlPlane.Security</c>, where it originated: <c>Apperture.Core.Application</c>
/// (home of <c>ResourceContextService.ResolveForNewOwnershipAsync</c>) does not, and should not,
/// depend on <c>Apperture.ControlPlane.Security</c> -- the dependency arrow only goes the other way.
/// </remarks>
public sealed class ResourceContextArchivedException : InvalidOperationException
{
    public ResourceContextArchivedException(ResourceContextId resourceContextId, string resourceContextName, string message)
        : base(message)
    {
        ResourceContextId = resourceContextId;
        ResourceContextName = resourceContextName;
    }

    /// <summary>The archived ResourceContext that blocked the operation.</summary>
    public ResourceContextId ResourceContextId { get; }

    /// <summary>Display name of the archived ResourceContext, so a later mapper need not re-fetch it just to compose a message.</summary>
    public string ResourceContextName { get; }
}
