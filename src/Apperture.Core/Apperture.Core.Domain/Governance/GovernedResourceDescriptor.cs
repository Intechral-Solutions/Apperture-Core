using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Governance;

/// <summary>
/// Governed resource descriptor: ResourceDescriptor plus optional ResourceFamily.
/// Used when registering a resource type for governance with IResourceDescriptorRegistry.
/// </summary>
public sealed record GovernedResourceDescriptor(ResourceDescriptor Descriptor, ResourceFamily? Family = null)
{
    /// <summary>Creates from type and scope.</summary>
    public static GovernedResourceDescriptor Create(string resourceType, string scopeKey, ResourceFamily? family = null) =>
        new(new ResourceDescriptor(resourceType, scopeKey), family);

    /// <summary>Creates from a governed resource identity for passing to EvaluateAsync.</summary>
    public static ResourceDescriptor FromIdentity(IGovernedResourceIdentity identity) =>
        new(identity.ResourceType, identity.ScopeKey ?? ScopeKeyConstants.Instance);
}
