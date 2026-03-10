using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Governance;

/// <summary>
/// Value object implementing resource identity for authorization.
/// </summary>
public sealed record GovernedResourceIdentity(
    string ResourceType,
    string ResourceId,
    string? ScopeKey = null,
    string? OwnerId = null) : IGovernedResourceIdentity;
