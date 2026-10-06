using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Username assignment on top of <see cref="IUserRepository"/>: generated names for users who did not
/// choose one, ordinary rename (including reclaiming one of the user's own retired names), and the
/// Recovery Access canonicalization rename. The username ledger in the database is the final arbiter of
/// every assignment; nothing here reserves a name in memory.
/// </summary>
public sealed class UsernameService
{
    private readonly IUserRepository _users;
    private readonly Func<string>? _randomSuffix;

    public UsernameService(IUserRepository users)
        : this(users, randomSuffix: null)
    {
    }

    /// <param name="randomSuffix">Test seam for the six-character collision suffix.</param>
    public UsernameService(IUserRepository users, Func<string>? randomSuffix)
    {
        _users = users;
        _randomSuffix = randomSuffix;
    }

    /// <summary>
    /// Adds the user built by <paramref name="buildUser"/> under the first free candidate generated from
    /// <paramref name="sources"/>: the base, <c>base-2</c> to <c>base-20</c>, then random suffixes. Any
    /// ledger row, current or retired, counts as taken. A failed insert persists nothing, so an exhausted
    /// or failed provisioning leaves no reserved name behind.
    /// </summary>
    public async ValueTask<User> AddWithGeneratedUsernameAsync(
        Func<string, User> buildUser,
        UsernameGenerationSources sources,
        CancellationToken cancellationToken = default)
    {
        var baseName = UsernamePolicy.GenerateBase(sources);
        foreach (var candidate in UsernamePolicy.CollisionCandidates(baseName, _randomSuffix))
        {
            // A pre-check only skips names that are visibly taken; the insert below is the arbiter.
            if (await _users.FindUsernameLedgerEntryAsync(candidate, cancellationToken) is not null)
                continue;

            var user = buildUser(candidate);
            if (!string.Equals(user.Username, candidate, StringComparison.Ordinal))
                throw new InvalidOperationException("The generated username must be the one assigned to the new user.");

            try
            {
                await _users.AddAsync(user, cancellationToken);
                return user;
            }
            catch (UsernameException ex) when (ex.Code == UsernameErrorCodes.Taken)
            {
                // Lost a race for this candidate; try the next one.
            }
        }

        throw new UsernameException(
            UsernameErrorCodes.GenerationExhausted,
            "No free username could be generated for this person. Try again, or ask an administrator to choose one.");
    }

    /// <summary>
    /// Ordinary rename. The target must satisfy the current policy, including reclaiming one of the user's
    /// own retired names; a name another user holds or held is refused. Recovery Access is system-managed
    /// and refused. Changes no credential, role or session.
    /// </summary>
    public async ValueTask<UsernameChangeOutcome> RenameAsync(
        UserId userId,
        string requestedUsername,
        CancellationToken cancellationToken = default) =>
        (await RenameWithResultAsync(userId, requestedUsername, cancellationToken)).Outcome;

    /// <summary>
    /// <see cref="RenameAsync"/>, reporting the name the transaction actually replaced and the canonical name now
    /// stored. Callers that audit a rename use this result and never read the previous name separately.
    /// </summary>
    public async ValueTask<UsernameRenameResult> RenameWithResultAsync(
        UserId userId,
        string requestedUsername,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId.Value} not found.");
        if (EmergencyAccessIdentity.IsSystemManaged(user))
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.UnsafeOperation,
                "Recovery Access is system-managed. Its username cannot be changed through ordinary user administration.");
        }

        var target = UsernamePolicy.RequireNewAssignment(requestedUsername);
        if (string.Equals(target, user.Username, StringComparison.Ordinal))
            return new UsernameRenameResult(UsernameChangeOutcome.Unchanged, user.Username, user.Username);

        return await _users.ChangeUsernameAsync(
            userId,
            user.Username,
            target,
            UsernameRetiredReasons.Rename,
            newDisplayName: null,
            cancellationToken);
    }

    /// <summary>
    /// Gives the designated Recovery Access user the canonical <c>breakglass</c> username and display name.
    /// The previous name is retired with reason <c>canonicalization</c> and stays bound to the same user.
    /// This is the only rename that may assign the reserved Recovery Access name.
    /// </summary>
    public async ValueTask<UsernameChangeOutcome> ApplyRecoveryAccessIdentityAsync(
        User designated,
        CancellationToken cancellationToken = default)
    {
        if (!designated.IsBreakGlassEligible)
            throw new InvalidOperationException("Only the designated Recovery Access user can take the Recovery Access username.");

        return (await _users.ChangeUsernameAsync(
            designated.Id,
            designated.Username,
            UsernamePolicy.RecoveryAccessUsername,
            UsernameRetiredReasons.Canonicalization,
            EmergencyAccessIdentity.DisplayName,
            cancellationToken)).Outcome;
    }
}
