using System.Text.Json;
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
            entity.ManagedIdentityPrincipalId,
            DeserializeScopes(entity.DeclaredScopesJson),
            entity.RegisteredAt);
    }

    public async ValueTask<IReadOnlyList<RuntimeIdentity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _db.RuntimeIdentities.AsNoTracking()
            .OrderBy(x => x.RegisteredAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(entity => new RuntimeIdentity(
            entity.Id,
            entity.DisplayName,
            entity.CredentialType,
            entity.PublicKeyOrThumbprint,
            entity.ManagedIdentityPrincipalId,
            DeserializeScopes(entity.DeclaredScopesJson),
            entity.RegisteredAt)).ToList();
    }

    public async ValueTask<RuntimeTrustValidationResult> ValidateAsync(
        string runtimeId,
        string presentedCredential,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.RuntimeIdentities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runtimeId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return new RuntimeTrustValidationResult(false, FailureReason: "Runtime identity not registered.");
        }

        if (entity.PublicKeyOrThumbprint is null && entity.ManagedIdentityPrincipalId is null)
        {
            return new RuntimeTrustValidationResult(false, FailureReason: "Runtime identity has no registered credential.");
        }

        // Stub: simple string comparison for public key / thumbprint.
        if (entity.PublicKeyOrThumbprint is not null &&
            string.Equals(entity.PublicKeyOrThumbprint, presentedCredential, StringComparison.Ordinal))
        {
            return new RuntimeTrustValidationResult(true);
        }

        // Phase 2: add Managed Identity assertion validation here.
        return new RuntimeTrustValidationResult(false, FailureReason: "Presented credential did not match registered identity.");
    }

    public async ValueTask<EnrollmentResult> RequestCredentialRolloverAsync(
        RuntimeCredentialRolloverRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.RuntimeIdentities
            .FirstOrDefaultAsync(x => x.Id == request.RuntimeId, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return new EnrollmentResult(request.RuntimeId, false, "Runtime identity not found.");
        }

        entity.CredentialType = request.NewCredentialType;
        entity.PublicKeyOrThumbprint = request.NewPublicKeyOrThumbprint;
        entity.ManagedIdentityPrincipalId = request.NewManagedIdentityPrincipalId;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new EnrollmentResult(request.RuntimeId, true, "Runtime credential rollover completed.");
    }

    /// <summary>The capability scopes a runtime identity row declares (a JSON array of strings).</summary>
    internal static IReadOnlyList<string> DeserializeScopes(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        // Stored as JSON array of strings.
        return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
    }
}
