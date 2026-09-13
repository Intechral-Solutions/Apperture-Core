using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
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
            await AppendRecoveryToPlatformAuditAsync("recovery.restore_platform_access.denied", auth.Reason ?? "Recovery not authorized.", cancellationToken).ConfigureAwait(false);
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
        var platformAudit = scope.ServiceProvider.GetService<IAuditService>();

        var targetUser = await ResolveTargetUserAsync(request, userRepository, cancellationToken).ConfigureAwait(false);
        if (targetUser is null)
        {
            await _auditWriter.WriteRecoveryAsync(new BootstrapRecoveryAuditRecord
            {
                Timestamp = timestamp,
                RecoveryAction = actionType,
                Reason = request.Reason,
                Outcome = "Failed",
                Details = "Target user not found."
            }, cancellationToken).ConfigureAwait(false);
            if (platformAudit is not null)
            {
                var failedRecord = new AuditRecord(
                    new AuditRecordId(Guid.NewGuid().ToString("N")),
                    new ActorId("recovery"),
                    "recovery.restore_platform_access.failed",
                    new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
                    "recovery",
                    DateTimeOffset.UtcNow,
                    new CorrelationId(null),
                    "Target user not found.");
                await platformAudit.AppendAsync(failedRecord, cancellationToken).ConfigureAwait(false);
            }
            return new BootstrapRecoveryResult { Success = false, Message = "Target user not found. Specify TargetUserId or TargetExternalId and TargetIdentitySource." };
        }

        var platformAdminRoleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);
        await roleRepository.AssignToUserAsync(targetUser.Id, platformAdminRoleId, cancellationToken).ConfigureAwait(false);

        await _auditWriter.WriteRecoveryAsync(new BootstrapRecoveryAuditRecord
        {
            Timestamp = timestamp,
            RecoveryAction = actionType,
            Reason = request.Reason,
            Outcome = "Executed",
            Details = $"Platform admin assigned to user {targetUser.Id.Value}."
        }, cancellationToken).ConfigureAwait(false);

        if (platformAudit is not null)
        {
            var record = new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                new ActorId("recovery"),
                "recovery.restore_platform_access.executed",
                new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
                "recovery",
                DateTimeOffset.UtcNow,
                new CorrelationId(null),
                $"targetUserId={targetUser.Id.Value}");
            await platformAudit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return new BootstrapRecoveryResult { Success = true, Message = $"Platform access restored for user {targetUser.Id.Value}." };
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
    /// Writes a recovery outcome to platform audit (durable). No secrets or proof. Used for denied and optionally other outcomes.
    /// </summary>
    private async Task AppendRecoveryToPlatformAuditAsync(string action, string? details, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var platformAudit = scope.ServiceProvider.GetService<IAuditService>();
        if (platformAudit is null)
            return;
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            new ActorId("recovery"),
            action,
            new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
            "recovery",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details);
        await platformAudit.AppendAsync(record, cancellationToken).ConfigureAwait(false);
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
