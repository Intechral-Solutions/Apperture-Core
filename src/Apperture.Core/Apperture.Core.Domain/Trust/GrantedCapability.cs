namespace Apperture.Core.Domain.Trust;

/// <summary>
/// Normalized delegated permission grant in a trust relationship.
/// </summary>
public sealed record GrantedCapability(
    string CapabilityName,
    string? ScopeExpression,
    string? Constraints,
    string? GrantedBy,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? ExpiresAtUtc);
