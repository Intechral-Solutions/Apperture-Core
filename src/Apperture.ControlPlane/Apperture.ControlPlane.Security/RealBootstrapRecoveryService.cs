using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;
using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Executes recovery flow: authorize via IBootstrapRecoveryAuthorizer, audit, and for RestorePlatformAccess assigns platform-admin role to target user.
/// Uses a scope to resolve scoped repositories so it can be registered as a singleton.
/// </summary>
public sealed class RealBootstrapRecoveryService : IBootstrapRecoveryService
{
    private readonly IBootstrapRecoveryAuthorizer _authorizer;
    private readonly IBootstrapAuditWriter _auditWriter;
    private readonly IServiceScopeFactory _scopeFactory;

    public RealBootstrapRecoveryService(
        IBootstrapRecoveryAuthorizer authorizer,
        IBootstrapAuditWriter auditWriter,
        IServiceScopeFactory scopeFactory)
    {
        _authorizer = authorizer;
        _auditWriter = auditWriter;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<BootstrapRecoveryResult> ExecuteRecoveryAsync(BootstrapRecoveryRequest request, CancellationToken cancellationToken = default)
    {
        var auth = await _authorizer.AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
        var actionType = request.ActionType;
        var timestamp = DateTimeOffset.UtcNow;

        if (!auth.Authorized)
        {
            await _auditWriter.WriteRecoveryAsync(new BootstrapRecoveryAuditRecord
            {
                Timestamp = timestamp,
                RecoveryAction = actionType,
                Reason = request.Reason,
                Outcome = "Denied",
                Details = auth.Reason
            }, cancellationToken).ConfigureAwait(false);
            await RecordRefusalAsync(AuditEvents.RestorePlatformAccessDenied()).ConfigureAwait(false);
            return new BootstrapRecoveryResult { Success = false, Message = auth.Reason ?? "Recovery not authorized." };
        }

        if (actionType != BootstrapRecoveryAction.RestorePlatformAccess)
        {
            await _auditWriter.WriteRecoveryAsync(new BootstrapRecoveryAuditRecord
            {
                Timestamp = timestamp,
                RecoveryAction = actionType,
                Reason = request.Reason,
                Outcome = "Authorized",
                Details = "No operation performed for this action type."
            }, cancellationToken).ConfigureAwait(false);
            return new BootstrapRecoveryResult { Success = true, Message = "Recovery authorized; no operation performed for this action type." };
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IAuditedUnitOfWork>();
        var platformAdminRoleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);

        // AUD-1.2: the target is resolved and the role assigned in one unit with its row, as the recovery tool. A target
        // that already holds the role changes nothing and writes no row (D7).
        var targetUserId = await unit.ExecuteAsync<UserId?>(
            new AuditedOperation(AuditActionNames.RestorePlatformAccessExecuted, AuditActor.RecoveryTool),
            async (transaction, ct) =>
            {
                var target = await ResolveTargetUserAsync(request, userRepository, ct).ConfigureAwait(false);
                if (target is null || target.AssignedRoleIds.Contains(platformAdminRoleId))
                {
                    transaction.DeclareNoOp();
                    return target?.Id;
                }

                await roleRepository.AssignToUserAsync(target.Id, platformAdminRoleId, ct).ConfigureAwait(false);
                await transaction.StageAsync(AuditEvents.RestorePlatformAccessExecuted(target.Id, platformAdminRoleId), ct).ConfigureAwait(false);
                return target.Id;
            },
            // A verified commit is success for the user its row names, even if that user can no longer be read (§13.5).
            (committed, _) => Task.FromResult<UserId?>(new UserId(committed.GetField("userId")!)),
            cancellationToken).ConfigureAwait(false);

        if (targetUserId is not { } restoredUserId)
        {
            await _auditWriter.WriteRecoveryAsync(new BootstrapRecoveryAuditRecord
            {
                Timestamp = timestamp,
                RecoveryAction = actionType,
                Reason = request.Reason,
                Outcome = "Failed",
                Details = "Target user not found."
            }, cancellationToken).ConfigureAwait(false);
            await RecordRefusalAsync(AuditEvents.RestorePlatformAccessTargetNotFound()).ConfigureAwait(false);
            return new BootstrapRecoveryResult { Success = false, Message = "Target user not found. Specify TargetUserId or TargetExternalId and TargetIdentitySource." };
        }

        await _auditWriter.WriteRecoveryAsync(new BootstrapRecoveryAuditRecord
        {
            Timestamp = timestamp,
            RecoveryAction = actionType,
            Reason = request.Reason,
            Outcome = "Executed",
            Details = $"Platform admin assigned to user {restoredUserId.Value}."
        }, cancellationToken).ConfigureAwait(false);

        return new BootstrapRecoveryResult { Success = true, Message = $"Platform access restored for user {restoredUserId.Value}." };
    }

    private static async Task<User?> ResolveTargetUserAsync(BootstrapRecoveryRequest request, IUserRepository userRepository, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetUserId))
        {
            var user = await userRepository.GetByIdAsync(new UserId(request.TargetUserId.Trim()), cancellationToken).ConfigureAwait(false);
            return user;
        }

        if (!string.IsNullOrWhiteSpace(request.TargetExternalId) && !string.IsNullOrWhiteSpace(request.TargetIdentitySource))
        {
            var source = ParseIdentitySource(request.TargetIdentitySource);
            if (!source.HasValue)
                return null;
            var externalId = request.TargetExternalId.Trim();
            if (source.Value == IdentitySource.Local)
                externalId = LocalAuthHelpers.NormalizeUsername(externalId);
            else if (source.Value == IdentitySource.Entra)
                return null;
            var user = await userRepository.GetByIdentityLinkAsync(source.Value, externalId, cancellationToken).ConfigureAwait(false);
            return user;
        }

        return null;
    }

    /// <summary>
    /// Writes a refused recovery to platform audit as a W3 row of the recovery tool. No request text, proof or identifier.
    /// </summary>
    private async Task RecordRefusalAsync(AuditEvent refusal)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var events = scope.ServiceProvider.GetService<IAuditEventWriter>();
        if (events is not null)
            await events.TryWriteAsync(refusal, AuditActor.RecoveryTool, CancellationToken.None).ConfigureAwait(false);
    }

    private static IdentitySource? ParseIdentitySource(string value)
    {
        if (string.Equals(value, "Local", StringComparison.OrdinalIgnoreCase))
            return IdentitySource.Local;
        if (string.Equals(value, "Entra", StringComparison.OrdinalIgnoreCase))
            return IdentitySource.Entra;
        return null;
    }
}
