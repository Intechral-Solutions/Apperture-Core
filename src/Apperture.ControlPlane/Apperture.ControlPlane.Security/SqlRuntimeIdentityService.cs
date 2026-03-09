using Apperture.Core.Security.Trust;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// SQL-backed runtime identity service. Resolves and validates registered runtime identities.
/// </summary>
public sealed class SqlRuntimeIdentityService : IRuntimeIdentityService
{
    private readonly AppertureDbContext _db;

    public SqlRuntimeIdentityService(AppertureDbContext db)
    {
        _db = db;
    }

    public async ValueTask<RuntimeIdentity?> GetByIdAsync(string runtimeId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.RuntimeIdentities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runtimeId, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : new RuntimeIdentity(
            entity.Id,
            entity.DisplayName,
            entity.CredentialType,
            entity.PublicKeyOrThumbprint,
            entity.RegisteredAt);
    }

    public async ValueTask<bool> ValidateAsync(string runtimeId, string presentedCredential, CancellationToken cancellationToken = default)
    {
        var entity = await _db.RuntimeIdentities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runtimeId, cancellationToken).ConfigureAwait(false);
        if (entity?.PublicKeyOrThumbprint is null)
            return false;
        // Stub: simple string comparison. Phase 2+ uses asymmetric verification.
        return string.Equals(entity.PublicKeyOrThumbprint, presentedCredential, StringComparison.Ordinal);
    }
}
