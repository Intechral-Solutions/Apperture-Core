using Apperture.Core.Security.Trust;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// SQL-backed enrollment service. Creates invitations and registers runtime identities.
/// </summary>
public sealed class SqlRuntimeEnrollmentService : IRuntimeEnrollmentService
{
    private readonly AppertureDbContext _db;
    private readonly IEnvironmentContext _environment;

    public SqlRuntimeEnrollmentService(AppertureDbContext db, IEnvironmentContext environment)
    {
        _db = db;
        _environment = environment;
    }

    public async ValueTask<EnrollmentInvitation> CreateInvitationAsync(
        string runtimeId,
        TimeSpan? ttl = null,
        IReadOnlySet<string>? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var duration = ttl ?? TimeSpan.FromHours(24);
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);
        var token = Guid.NewGuid().ToString("N");
        var scopeList = scopes is null
            ? Array.Empty<string>()
            : scopes.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        _db.EnrollmentTokens.Add(new EnrollmentTokenEntity
        {
            Id = Guid.NewGuid(),
            Token = token,
            RuntimeId = runtimeId,
            EnvironmentId = _environment.EnvironmentId,
            ExpiresAt = expiresAt,
            Used = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ScopesJson = JsonSerializer.Serialize(scopeList)
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var baseUrl = "https://localhost"; // TODO: from config
        var enrollmentUrl = $"{baseUrl}/api/enroll?token={token}";
        return new EnrollmentInvitation(token, enrollmentUrl, expiresAt);
    }

    public async ValueTask<EnrollmentTokenValidationResult> ValidateTokenAsync(
        string enrollmentToken,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.EnrollmentTokens.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Token == enrollmentToken, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return new EnrollmentTokenValidationResult(false, FailureReason: "Invalid or expired enrollment token.");
        }

        if (entity.Used)
        {
            return new EnrollmentTokenValidationResult(false, entity.ExpiresAt, "Enrollment token has already been used.");
        }

        if (entity.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return new EnrollmentTokenValidationResult(false, entity.ExpiresAt, "Enrollment token has expired.");
        }

        return new EnrollmentTokenValidationResult(true, entity.ExpiresAt);
    }

    public async ValueTask<EnrollmentResult> EnrollAsync(
        string enrollmentToken,
        RuntimeIdentityRegistration registration,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.EnrollmentTokens
            .FirstOrDefaultAsync(x => x.Token == enrollmentToken && !x.Used, cancellationToken).ConfigureAwait(false);
        if (entity is null)
            return new EnrollmentResult("", false, "Invalid or expired enrollment token.");

        // Token-scoped environment mapping validation for enrollment.
        if (!string.Equals(entity.EnvironmentId, _environment.EnvironmentId, StringComparison.Ordinal))
            return new EnrollmentResult(entity.RuntimeId, false, "Enrollment token environment mismatch.");

        if (entity.ExpiresAt < DateTimeOffset.UtcNow)
            return new EnrollmentResult(entity.RuntimeId, false, "Enrollment token has expired.");
        entity.Used = true;
        _db.RuntimeIdentities.Add(new RuntimeIdentityEntity
        {
            Id = entity.RuntimeId,
            DisplayName = registration.RuntimeName,
            CredentialType = registration.CredentialType,
            PublicKeyOrThumbprint = registration.PublicKeyOrThumbprint,
            ManagedIdentityPrincipalId = registration.ManagedIdentityPrincipalId,
            DeclaredScopesJson = entity.ScopesJson,
            RegisteredAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new EnrollmentResult(entity.RuntimeId, true);
    }
}
