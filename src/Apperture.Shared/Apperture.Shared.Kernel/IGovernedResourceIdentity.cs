namespace Apperture.Shared.Kernel;

/// <summary>
/// Resource identity for authorization. Identifies a governed resource by type, id, scope, and owner.
/// </summary>
public interface IGovernedResourceIdentity
{
    /// <summary>Resource type (e.g. Connection, Binding, Module).</summary>
    string ResourceType { get; }

    /// <summary>Stable identifier for the resource instance.</summary>
    string ResourceId { get; }

    /// <summary>Scope boundary (e.g. instance, module:{moduleId}, connection:{connectionId}).</summary>
    string? ScopeKey { get; }

    /// <summary>Owner or creator identifier, if applicable.</summary>
    string? OwnerId { get; }
}
