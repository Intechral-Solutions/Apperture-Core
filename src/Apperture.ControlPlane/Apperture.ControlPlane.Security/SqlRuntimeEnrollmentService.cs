using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Governance;
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
/// <remarks>
/// AUD-1.2: creating an invitation is one audited operation of the administrator, and enrolling is one audited operation of
/// an anonymous caller holding the invitation (D10). Each change and its row commit together; a refused enrollment changes
/// nothing and writes no row.
/// </remarks>
public sealed class SqlRuntimeEnrollmentService : IRuntimeEnrollmentService
{
    private readonly AppertureDbContext _db;
    private readonly IEnvironmentContext _environment;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public SqlRuntimeEnrollmentService(
        AppertureDbContext db,
        IEnvironmentContext environment,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _db = db;
        _environment = environment;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Runtime enrollment requires the audited unit of work.");

    public async ValueTask<EnrollmentInvitation> CreateInvitationAsync(
        string runtimeId,
        TimeSpan? ttl = null,
        IReadOnlySet<string>? scopes = null,
        CancellationToken cancellationToken = default)
    {
        // Chosen once, so a rerun after a confirmed rollback creates the same invitation.
        var duration = ttl ?? TimeSpan.FromHours(24);
        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = createdAt.Add(duration);
        var token = Guid.NewGuid().ToString("N");
        var invitationId = Guid.NewGuid();
        var scopeList = scopes is null
            ? Array.Empty<string>()
            : scopes.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var scopeKey = ScopeKeyConstants.RuntimeInstance(_environment.EnvironmentId, runtimeId);

        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RuntimeEnrollmentInvitationCreated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                _db.EnrollmentTokens.Add(new EnrollmentTokenEntity
                {
                    Id = invitationId,
                    Token = token,
                    RuntimeId = runtimeId,
                    EnvironmentId = _environment.EnvironmentId,
                    ExpiresAt = expiresAt,
                    Used = false,
                    CreatedAt = createdAt,
                    ScopesJson = JsonSerializer.Serialize(scopeList)
                });
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.RuntimeEnrollmentInvitationCreated(scopeKey, runtimeId, invitationId.ToString("N"), scopeList.Length),
                    ct).ConfigureAwait(false);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);

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
        var registeredAt = DateTimeOffset.UtcNow;
        return await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RuntimeEnrollmentCompleted, AuditActor.Anonymous),
            async (transaction, ct) =>
            {
                var entity = await _db.EnrollmentTokens
                    .FirstOrDefaultAsync(x => x.Token == enrollmentToken && !x.Used, ct).ConfigureAwait(false);
                var refusal = entity is null
                    ? new EnrollmentResult("", false, "Invalid or expired enrollment token.")
                    // Token-scoped environment mapping validation for enrollment.
                    : !string.Equals(entity.EnvironmentId, _environment.EnvironmentId, StringComparison.Ordinal)
                        ? new EnrollmentResult(entity.RuntimeId, false, "Enrollment token environment mismatch.")
                        : entity.ExpiresAt < DateTimeOffset.UtcNow
                            ? new EnrollmentResult(entity.RuntimeId, false, "Enrollment token has expired.")
                            : null;
                if (refusal is not null)
                {
                    transaction.DeclareNoOp();
                    return refusal;
                }

                entity!.Used = true;
                _db.RuntimeIdentities.Add(new RuntimeIdentityEntity
                {
                    Id = entity.RuntimeId,
                    DisplayName = registration.RuntimeName,
                    CredentialType = registration.CredentialType,
                    PublicKeyOrThumbprint = registration.PublicKeyOrThumbprint,
                    ManagedIdentityPrincipalId = registration.ManagedIdentityPrincipalId,
                    DeclaredScopesJson = entity.ScopesJson,
                    RegisteredAt = registeredAt
                });
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.StageAsync(
                    AuditEvents.RuntimeEnrollmentCompleted(
                        ScopeKeyConstants.RuntimeInstance(entity.EnvironmentId, entity.RuntimeId),
                        entity.RuntimeId,
                        entity.Id.ToString("N")),
                    ct).ConfigureAwait(false);
                return new EnrollmentResult(entity.RuntimeId, true);
            },
            (committed, _) => Task.FromResult(new EnrollmentResult(committed.GetField("runtimeId") ?? "", true)),
            cancellationToken).ConfigureAwait(false);
    }
}
