namespace Apperture.Core.Application.Services;

/// <summary>
/// A normalized request for one page of the user directory. Built only through <see cref="Create"/>, so every
/// consumer (service, repository, tests) sees the same trimmed, lower-cased query and the same paging bounds.
/// </summary>
/// <param name="Query">
/// Trimmed, lower-case search text, or null for the unfiltered list. LIKE wildcards are kept verbatim; the store
/// escapes them.
/// </param>
public sealed record UserSearchCriteria(string? Query, int Skip, int Take)
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;

    public static UserSearchCriteria Create(string? query, int skip, int take)
    {
        var normalized = string.IsNullOrWhiteSpace(query) ? null : query.Trim().ToLowerInvariant();
        return new UserSearchCriteria(normalized, Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
    }
}
