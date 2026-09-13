namespace Apperture.Core.Application.Services;

/// <summary>
/// Server-derived authentication-assurance snapshot. Never accept a client-supplied copy of this
/// record as evidence; it must be rebuilt from the signed application cookie (or equivalent
/// host-controlled principal) on each request.
/// </summary>
/// <param name="Mechanism">How the assurance was established (for example <c>entra-authentication-context</c>).</param>
/// <param name="RequirementSatisfied">Whether the configured strong-authentication requirement was met.</param>
/// <param name="EstablishedAtUtc">When the host recorded the assurance. Null when none is present.</param>
/// <param name="ProviderContext">Provider-specific context id when applicable (Entra authentication-context identifier such as <c>c1</c>).</param>
/// <param name="IsRecent">True when <paramref name="EstablishedAtUtc"/> is within the configured recent-authentication window.</param>
/// <param name="DevelopmentSimulated">True only for an explicit Development-environment simulation. Production must treat this as unsatisfied.</param>
public sealed record AuthenticationAssurance(
    string? Mechanism,
    bool RequirementSatisfied,
    DateTimeOffset? EstablishedAtUtc,
    string? ProviderContext,
    bool IsRecent,
    bool DevelopmentSimulated = false)
{
    public static AuthenticationAssurance None { get; } = new(null, false, null, null, false);
}

/// <summary>
/// Lifetime for "recent strong authentication". Bind from section <see cref="SectionName"/>.
/// A long-lived or sliding application cookie is not evidence of recency; only
/// <see cref="AuthenticationAssurance.EstablishedAtUtc"/> is.
/// </summary>
public sealed class AuthenticationAssuranceOptions
{
    public const string SectionName = "AuthenticationAssurance";

    /// <summary>Default conservative window: 15 minutes.</summary>
    public static readonly TimeSpan DefaultRecentLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a recorded strong-authentication assurance remains valid for sensitive operations.
    /// The same window is reused for recent account verification (local-password unlock).
    /// </summary>
    public TimeSpan RecentStrongAuthenticationLifetime { get; set; } = DefaultRecentLifetime;
}
