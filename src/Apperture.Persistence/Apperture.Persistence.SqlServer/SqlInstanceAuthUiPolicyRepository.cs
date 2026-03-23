using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlInstanceAuthUiPolicyRepository : IInstanceAuthUiPolicyRepository
{
    public const string InstanceId = "instance";

    private readonly AppertureDbContext _db;

    public SqlInstanceAuthUiPolicyRepository(AppertureDbContext db) => _db = db;

    public async Task<InstanceAuthUiPolicy> GetAsync(CancellationToken cancellationToken = default)
    {
        var e = await _db.InstanceAuthUiPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == InstanceId, cancellationToken);
        if (e is null)
            throw new InvalidOperationException("InstanceAuthUiPolicies row is missing; apply database migrations.");

        return ToDomain(e);
    }

    public async Task SaveAsync(InstanceAuthUiPolicy policy, byte[]? expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var e = await _db.InstanceAuthUiPolicies
            .FirstOrDefaultAsync(x => x.Id == InstanceId, cancellationToken);
        if (e is null)
            throw new InvalidOperationException("InstanceAuthUiPolicies row is missing; apply database migrations.");

        if (expectedRowVersion is not null && (e.RowVersion is null || !e.RowVersion.SequenceEqual(expectedRowVersion)))
            throw new DbUpdateConcurrencyException("Instance auth UI policy was modified by another request.");

        e.ShowLocalSignInInPrimaryChooser = policy.ShowLocalSignInInPrimaryChooser;
        e.PreferEntraInChooser = policy.PreferEntraInChooser;
        e.AutoRedirectToEntra = policy.AutoRedirectToEntra;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static InstanceAuthUiPolicy ToDomain(InstanceAuthUiPolicyEntity e) =>
        new()
        {
            ShowLocalSignInInPrimaryChooser = e.ShowLocalSignInInPrimaryChooser,
            PreferEntraInChooser = e.PreferEntraInChooser,
            AutoRedirectToEntra = e.AutoRedirectToEntra,
            RowVersion = e.RowVersion is { Length: > 0 } ? e.RowVersion.ToArray() : null
        };
}
