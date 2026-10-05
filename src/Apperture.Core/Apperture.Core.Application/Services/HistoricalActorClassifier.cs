namespace Apperture.Core.Application.Services;

/// <summary>What historical actor normalization does with one stored actor value.</summary>
public enum HistoricalActorCategory
{
    /// <summary>A non-human actor literal (<see cref="ActorLiterals"/>). Left unchanged.</summary>
    Literal,

    /// <summary>Already the user id of exactly one user, with no other user meaning. Left unchanged.</summary>
    AlreadyUserId,

    /// <summary>A current or retired username, or an Entra object id with exactly one link, of exactly one user. Rewritten to that user id.</summary>
    Resolved,

    /// <summary>No safe match, or an Entra object id with several links of one user. Left unchanged.</summary>
    Unresolved,

    /// <summary>
    /// Means different users under different rules, is an Entra object id linked to several users, or would resolve to a
    /// user whose id itself means another user too. Left unchanged.
    /// </summary>
    Ambiguous,
}

/// <summary>The category of one historical actor value and, when <see cref="HistoricalActorCategory.Resolved"/>, the user id it becomes.</summary>
public readonly record struct HistoricalActorClassification(HistoricalActorCategory Category, string? UserId = null);

/// <summary>
/// Classifies a historical actor value against the instance's identity data, by the rules of the IAM-9 design gate §17.
/// Rewriting is the caller's job; this only decides. It never invents an identity.
/// </summary>
/// <remarks>
/// <para>Every human interpretation is collected before deciding, so a value that means different users under different
/// rules is ambiguous rather than whichever rule happens to be checked first:</para>
/// <list type="number">
/// <item>A non-human literal (<see cref="ActorLiterals.IsNonHumanLiteral"/>) is left unchanged. The gate's "recorded worker
/// or host id" needs no data source here: no writer of a normalized column stores one (worker ids go to <c>AppliedBy</c>
/// and <c>ClaimedBy</c>, which are not normalized).</item>
/// <item>A value equal to a user id names that user.</item>
/// <item>A value equal to a username in the ledger, current or retired, names the ledger row's owner. Usernames compare
/// case-insensitively, like the ledger's own key.</item>
/// <item>A value equal to the object id of exactly one Entra link names that link's owner. Historical values carry no
/// tenant, so the object id alone is matched across every Entra link, and several links never resolve.</item>
/// </list>
/// <para>Zero candidate users is unresolved, one is already a user id or resolved, several are ambiguous. An Entra object
/// id with several links of a single user resolves nothing by itself (the gate requires exactly one link). A value is
/// resolved only to a user whose id means that user alone; when the id is also another user's username or object id, the
/// rewritten value could not be read back, so the original stays and is counted ambiguous. Every value this writes is
/// therefore already a user id on the next run.</para>
/// </remarks>
public sealed class HistoricalActorClassifier
{
    private readonly HashSet<string> _userIds;
    private readonly Dictionary<string, HashSet<string>> _usernameOwners;
    private readonly Dictionary<string, List<string>> _entraLinkOwners;

    /// <param name="userIds">Every <c>Users.Id</c>.</param>
    /// <param name="usernameLedger">Every <c>UsernameLedger</c> row, current and retired: (username, owning user id).</param>
    /// <param name="entraLinks">Every Entra identity link, whatever its tenant: (object id, owning user id), one entry per link.</param>
    public HistoricalActorClassifier(
        IEnumerable<string> userIds,
        IEnumerable<(string Username, string UserId)> usernameLedger,
        IEnumerable<(string ObjectId, string UserId)> entraLinks)
    {
        _userIds = new HashSet<string>(userIds, StringComparer.Ordinal);

        _usernameOwners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (username, userId) in usernameLedger)
        {
            if (!_usernameOwners.TryGetValue(username, out var owners))
                _usernameOwners[username] = owners = new HashSet<string>(StringComparer.Ordinal);
            owners.Add(userId);
        }

        // Object ids are GUIDs: their letter case carries no meaning.
        _entraLinkOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (objectId, userId) in entraLinks)
        {
            if (!_entraLinkOwners.TryGetValue(objectId, out var owners))
                _entraLinkOwners[objectId] = owners = [];
            owners.Add(userId);
        }
    }

    public HistoricalActorClassification Classify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new(HistoricalActorCategory.Unresolved);

        if (ActorLiterals.IsNonHumanLiteral(value))
            return new(HistoricalActorCategory.Literal);

        var candidates = new HashSet<string>(StringComparer.Ordinal);

        var isUserId = _userIds.Contains(value);
        if (isUserId)
            candidates.Add(value);

        var isUsername = _usernameOwners.TryGetValue(value, out var usernameOwners);
        if (isUsername)
            candidates.UnionWith(usernameOwners!);

        var entraOwners = _entraLinkOwners.TryGetValue(value, out var links) ? links : [];
        candidates.UnionWith(entraOwners);

        if (candidates.Count > 1)
            return new(HistoricalActorCategory.Ambiguous);
        if (candidates.Count == 0)
            return new(HistoricalActorCategory.Unresolved);

        // One candidate user. An object id that is this value's only meaning must have exactly one link.
        if (!isUserId && !isUsername && entraOwners.Count != 1)
            return new(HistoricalActorCategory.Unresolved);

        if (isUserId)
            return new(HistoricalActorCategory.AlreadyUserId);

        // Never write a value these same rules cannot read back: the user's id must mean only that user.
        var userId = candidates.Single();
        return MeansOnly(userId)
            ? new(HistoricalActorCategory.Resolved, userId)
            : new(HistoricalActorCategory.Ambiguous);
    }

    private bool MeansOnly(string userId) =>
        (!_usernameOwners.TryGetValue(userId, out var owners) || owners.All(o => o == userId))
        && (!_entraLinkOwners.TryGetValue(userId, out var links) || links.All(o => o == userId));
}
