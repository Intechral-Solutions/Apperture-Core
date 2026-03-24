using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Integration;

/// <summary>
/// Operator-facing configured instance of a module. Owns settings, bindings, schedules. Epic 49, Epic 50.
/// </summary>
public sealed record Integration(
    IntegrationId Id,
    string DisplayName,
    string ModuleId,
    string? ScopeKey,
    IntegrationState State,
    IReadOnlyDictionary<string, string>? Settings,
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
