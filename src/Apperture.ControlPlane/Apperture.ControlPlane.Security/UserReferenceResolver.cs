using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <inheritdoc cref="IUserReferenceResolver" />
public sealed class UserReferenceResolver : IUserReferenceResolver
{
    private readonly IUserRepository _users;

    public UserReferenceResolver(IUserRepository users) => _users = users;

    public async ValueTask<IReadOnlyDictionary<UserId, UserReferenceDto>> ResolveAsync(
        IEnumerable<UserId> userIds,
        CancellationToken cancellationToken = default)
    {
        var distinct = userIds.Distinct().ToList();
        if (distinct.Count == 0)
            return new Dictionary<UserId, UserReferenceDto>();

        var references = await _users.GetReferencesAsync(distinct, cancellationToken).ConfigureAwait(false);
        return references.ToDictionary(r => new UserId(r.UserId));
    }
}
