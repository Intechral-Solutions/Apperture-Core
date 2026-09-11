using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.ResourceContexts;

/// <summary>
/// The ResourceContext reference rule.
/// </summary>
/// <remarks>
/// <para>
/// A resource may reference resources in its own ResourceContext, or in the reserved Shared context.
/// It may not reference resources in any other context.
/// </para>
/// <para>
/// In CTX-1 every row is in Shared, so the rule is vacuously true and changes no behavior. It ships now
/// so the enforcement point is written and tested while it cannot break anything — retrofitting it after
/// resources can move between contexts would mean fixing already-invalid data.
/// </para>
/// </remarks>
public static class ResourceContextReferenceRule
{
    /// <summary>
    /// True when a resource owned by <paramref name="owningResourceContextId"/> may reference a resource
    /// owned by <paramref name="referencedResourceContextId"/>.
    /// </summary>
    public static bool CanReference(ResourceContextId owningResourceContextId, ResourceContextId referencedResourceContextId)
    {
        ArgumentNullException.ThrowIfNull(owningResourceContextId);
        ArgumentNullException.ThrowIfNull(referencedResourceContextId);

        if (string.Equals(owningResourceContextId.Value, referencedResourceContextId.Value, StringComparison.Ordinal))
            return true;

        return ResourceContextIds.IsShared(referencedResourceContextId);
    }
}
