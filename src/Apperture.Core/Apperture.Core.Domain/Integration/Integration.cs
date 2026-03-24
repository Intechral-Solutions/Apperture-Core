using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Integration;

/// <summary>
/// Operator-facing configured instance of a module. Will eventually own settings, bindings, schedules, and runtime deployment.
/// Epic 49.
/// </summary>
public sealed record Integration(
    IntegrationId Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    IntegrationState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt) : IGovernedResource
{
    /// <inheritdoc />
    public IGovernedResourceIdentity GetIdentity() =>
        new GovernedResourceIdentity(
            ResourceTypeConstants.Integration,
            Id.Value,
            ScopeKey ?? ScopeKeyConstants.Instance,
            null);
}
