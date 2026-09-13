using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Integration;

/// <summary>
/// Operator-facing configured instance of a module. Owns settings, bindings, schedules. Epic 49, Epic 50.
/// </summary>
/// <param name="ScopeKey">
/// <b>Authorization</b> scope of this Integration (see <see cref="ScopeKeyConstants"/>). This is not
/// a product context and is unrelated to <see cref="ResourceContextId"/>. Do not conflate the two.
/// </param>
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
    /// <summary>
    /// The ResourceContext that owns this Integration. Required in storage; defaults to the reserved
    /// Shared context.
    /// </summary>
    /// <remarks>
    /// Ownership / namespace only. <see cref="ScopeKey"/> remains the authorization concept and is
    /// untouched by ResourceContext ownership.
    /// </remarks>
    public ResourceContextId ResourceContextId { get; init; } = ResourceContextIds.Shared;

    /// <inheritdoc />
    public IGovernedResourceIdentity GetIdentity() =>
        new GovernedResourceIdentity(
            ResourceTypeConstants.Integration,
            Id.Value,
            ScopeKey ?? ScopeKeyConstants.Instance,
            null);
}
