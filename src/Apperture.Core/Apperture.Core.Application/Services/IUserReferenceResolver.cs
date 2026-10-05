using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// How one user is shown wherever a page refers to a person: the stable id, the human-facing username, and
/// the display name. Status and avatar are not part of this contract yet; they arrive as additive fields.
/// </summary>
public sealed record UserReferenceDto(string UserId, string Username, string DisplayName);

/// <summary>
/// Turns user ids into <see cref="UserReferenceDto"/> values in one batch, so a response that mentions people
/// composes them server-side without per-row lookups.
/// </summary>
/// <remarks>
/// This is composition, not a directory: it performs no <c>user.read</c> check. A caller may show a reference
/// because its own permission allows the page it is building, not because the viewer can read the user directory.
/// Recovery Access resolves like any other user. Ids that match no user are omitted; an unknown historical actor is
/// not a user reference and must be rendered from the original string by the caller.
/// </remarks>
public interface IUserReferenceResolver
{
    ValueTask<IReadOnlyDictionary<UserId, UserReferenceDto>> ResolveAsync(
        IEnumerable<UserId> userIds,
        CancellationToken cancellationToken = default);
}
