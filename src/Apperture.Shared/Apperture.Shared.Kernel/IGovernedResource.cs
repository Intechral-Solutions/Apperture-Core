namespace Apperture.Shared.Kernel;

/// <summary>
/// Contract for platform-managed entities that participate in authorization.
/// Resource families implement this; authorization consumes identity only.
/// </summary>
public interface IGovernedResource
{
    /// <summary>Returns the resource identity for authorization evaluation.</summary>
    IGovernedResourceIdentity GetIdentity();
}
