using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Core logic for <see cref="BootstrapPlatformAdminAssignmentHostedService"/>; unit-testable without hosting.
/// </summary>
/// <remarks>
/// AUD-1.2: each configured principal's change is one audited unit of work, recorded as
/// <c>system:bootstrap-configuration</c>. The lookup that decides it, the created user (with its link and role) or the role
/// assignment, and their rows commit together: <c>bootstrap.configured_admin.created</c> plus the role's
/// <c>governance.role.assignment.created</c>, or the assignment row alone for an existing user. A principal that already holds
/// the role changes nothing and writes no row. Configuration that cannot be applied is skipped and logged, as before.
/// </remarks>
public static class BootstrapPlatformAdminAssignment
{
    /// <summary>The actor of every change this startup step makes.</summary>
    public static AuditActor Actor { get; } = AuditActor.System("bootstrap-configuration");

    private enum Outcome
    {
        Skipped,
        InvalidUsername,
        Created,
        Assigned,
        AlreadyAdministrator,
    }

    /// <summary>
    /// Ensures each configured principal has the Platform Administrator role (creates user when allowed and missing).
    /// </summary>
    public static async Task ExecuteAsync(
        BootstrapPlatformAdminOptions options,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILogger? logger,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || options.Principals.Count == 0)
            return;

        var adminRoleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);
        var adminRole = await roleRepository.GetAsync(adminRoleId, cancellationToken).ConfigureAwait(false);
        if (adminRole is null)
        {
            logger?.LogWarning(
                "Platform Administrator role {RoleId} not found; bootstrap principal assignment skipped.",
                BootstrapConstants.PlatformAdminRoleId);
            return;
        }

        var unit = auditedUnitOfWork
            ?? throw new InvalidOperationException("Bootstrap principal assignment requires the audited unit of work.");

        foreach (var p in options.Principals)
        {
            if (string.IsNullOrWhiteSpace(p.ExternalId))
            {
                logger?.LogWarning("Bootstrap principal entry skipped: ExternalId is empty.");
                continue;
            }

            if (!Enum.TryParse<IdentitySource>(p.IdentitySource, ignoreCase: true, out var source) ||
                (source != IdentitySource.Entra && source != IdentitySource.Local))
            {
                logger?.LogWarning(
                    "Bootstrap principal skipped: invalid IdentitySource '{Source}' (use Entra or Local).",
                    p.IdentitySource);
                continue;
            }

            var externalId = source == IdentitySource.Local
                ? LocalAuthHelpers.NormalizeUsername(p.ExternalId)
                : p.ExternalId.Trim();

            if (source == IdentitySource.Local && EmergencyAccessIdentity.IsReservedLocalUsername(externalId))
            {
                logger?.LogWarning("Bootstrap principal skipped: username is reserved for Emergency Access.");
                continue;
            }

            if (source == IdentitySource.Entra && externalId.Contains('@', StringComparison.Ordinal))
            {
                logger?.LogWarning(
                    "Bootstrap Entra principal skipped: ExternalId must be oid/sub, not an email or UPN ({Hint}).",
                    p.ExternalId);
                continue;
            }

            string? tenantId = null;
            if (source == IdentitySource.Entra)
            {
                tenantId = EntraTokenClaims.Normalize(p.TenantId);
                if (string.IsNullOrEmpty(tenantId) || !EntraTokenClaims.IsValidObjectId(tenantId) || !EntraTokenClaims.IsValidObjectId(externalId))
                {
                    logger?.LogWarning(
                        "Bootstrap Entra principal skipped: TenantId and ExternalId must be directory ids (tid + oid).");
                    continue;
                }
            }

            var displayName = string.IsNullOrWhiteSpace(p.DisplayName)
                ? $"Bootstrap administrator ({externalId})"
                : p.DisplayName.Trim();

            // Chosen once, so a rerun after a confirmed rollback creates the same user.
            var newUserId = new UserId(Guid.NewGuid().ToString("N")[..24]);
            UserId? affected = null;
            string? createdUsername = null;

            Outcome outcome;
            try
            {
                outcome = await unit.ExecuteAsync(
                    new AuditedOperation(AuditActionNames.ConfiguredAdminCreated, Actor, [AuditActionNames.RoleAssigned]),
                    async (transaction, ct) =>
                    {
                        affected = null;
                        createdUsername = null;

                        var existing = source == IdentitySource.Entra
                            ? await userRepository.GetByIdentityLinkAsync(ProviderIdentityKey.Entra(tenantId!, externalId), ct).ConfigureAwait(false)
                            : await userRepository.GetByIdentityLinkAsync(source, externalId, ct).ConfigureAwait(false);

                        if (existing is null)
                        {
                            if (!options.CreateUserIfMissing)
                            {
                                transaction.DeclareNoOp();
                                return Outcome.Skipped;
                            }

                            if (source == IdentitySource.Local && UsernamePolicy.CheckNewAssignment(externalId) != UsernameAssignmentCheck.Valid)
                            {
                                transaction.DeclareNoOp();
                                return Outcome.InvalidUsername;
                            }

                            var user = await CreateAsync(userRepository, source, tenantId, externalId, displayName, p.DisplayName, newUserId, adminRoleId, ct)
                                .ConfigureAwait(false);
                            affected = user.Id;
                            createdUsername = user.Username;
                            await transaction.StageAsync(AuditEvents.ConfiguredAdminCreated(user.Id, user.Username, source, adminRoleId), ct).ConfigureAwait(false);
                            await transaction.StageAsync(AuditEvents.RoleAssigned(user.Id, adminRoleId), ct).ConfigureAwait(false);
                            return Outcome.Created;
                        }

                        var roles = await roleRepository.GetForUserAsync(existing.Id, ct).ConfigureAwait(false);
                        if (roles.Any(r => string.Equals(r.Id.Value, BootstrapConstants.PlatformAdminRoleId, StringComparison.Ordinal)))
                        {
                            transaction.DeclareNoOp();
                            return Outcome.AlreadyAdministrator;
                        }

                        await roleRepository.AssignToUserAsync(existing.Id, adminRoleId, ct).ConfigureAwait(false);
                        affected = existing.Id;
                        await transaction.StageAsync(AuditEvents.RoleAssigned(existing.Id, adminRoleId), ct).ConfigureAwait(false);
                        return Outcome.Assigned;
                    },
                    (committed, _) => Task.FromResult(
                        string.Equals(committed.Action, AuditActionNames.ConfiguredAdminCreated, StringComparison.Ordinal)
                            ? Outcome.Created
                            : Outcome.Assigned),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (UsernameException ex) when (ex.Code == UsernameErrorCodes.Taken)
            {
                logger?.LogWarning(
                    "Bootstrap principal {Source}/{ExternalId} skipped: that username belongs, or once belonged, to another user.",
                    source,
                    externalId);
                continue;
            }

            switch (outcome)
            {
                case Outcome.Created:
                    logger?.LogInformation(
                        "Created user {UserId} ({Username}) for bootstrap principal {Source}/{ExternalId} with Platform Administrator role.",
                        (affected ?? newUserId).Value,
                        createdUsername,
                        source,
                        externalId);
                    break;
                case Outcome.Assigned:
                    logger?.LogInformation(
                        "Assigned Platform Administrator role to user {UserId} for bootstrap principal {Source}/{ExternalId}.",
                        affected?.Value,
                        source,
                        externalId);
                    break;
                case Outcome.Skipped:
                    logger?.LogInformation(
                        "Bootstrap principal {Source}/{ExternalId}: no user found and CreateUserIfMissing=false; skipped.",
                        source,
                        externalId);
                    break;
                case Outcome.InvalidUsername:
                    logger?.LogWarning(
                        "Bootstrap principal {Source}/{ExternalId} skipped: a new local username must be 3 to 64 lowercase letters, digits, '.', '-' or '_', start with a letter, end with a letter or digit, and not be reserved.",
                        source,
                        externalId);
                    break;
            }
        }
    }

    /// <summary>Adds the configured administrator with its link and the Platform Administrator role.</summary>
    private static async Task<User> CreateAsync(
        IUserRepository userRepository,
        IdentitySource source,
        string? tenantId,
        string externalId,
        string displayName,
        string? configuredDisplayName,
        UserId userId,
        RoleId adminRoleId,
        CancellationToken cancellationToken)
    {
        if (source == IdentitySource.Entra)
        {
            // Configuration names only tid + oid, so the username comes from a configured display
            // name or falls back to "user". The default display text (with the oid) is never used.
            return await new UsernameService(userRepository).AddWithGeneratedUsernameAsync(
                username => new User(
                    userId,
                    displayName,
                    [new IdentityLink(IdentitySource.Entra, externalId, LastAuthenticatedAt: null, TenantId: tenantId)],
                    [adminRoleId],
                    IsBreakGlassEligible: false)
                {
                    Username = username
                },
                new UsernameGenerationSources(
                    UserPrincipalName: null,
                    Mail: null,
                    DisplayName: string.IsNullOrWhiteSpace(configuredDisplayName) ? null : displayName),
                cancellationToken).ConfigureAwait(false);
        }

        var user = new User(
            userId,
            displayName,
            [new IdentityLink(source, externalId, LastAuthenticatedAt: null)],
            [adminRoleId],
            IsBreakGlassEligible: false)
        {
            Username = externalId
        };
        await userRepository.AddAsync(user, cancellationToken).ConfigureAwait(false);
        return user;
    }
}
