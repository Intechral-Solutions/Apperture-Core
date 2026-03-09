namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Links a User to an authentication source (Entra or local). Value object / entity.
/// </summary>
public sealed record IdentityLink(
    IdentitySource Source,
    string ExternalId,
    DateTimeOffset? LastAuthenticatedAt);
