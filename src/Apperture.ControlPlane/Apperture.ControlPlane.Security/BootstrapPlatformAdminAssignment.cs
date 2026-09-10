using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Core logic for <see cref="BootstrapPlatformAdminAssignmentHostedService"/>; unit-testable without hosting.
/// </summary>
public static class BootstrapPlatformAdminAssignment
{
    /// <summary>
    /// Ensures each configured principal has the Platform Administrator role (creates user when allowed and missing).
    /// </summary>
    public static async Task ExecuteAsync(
        BootstrapPlatformAdminOptions options,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILogger? logger,
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

            User? existing;
            if (source == IdentitySource.Entra)
            {
                var tenantId = EntraTokenClaims.Normalize(p.TenantId);
                if (string.IsNullOrEmpty(tenantId) || !EntraTokenClaims.IsValidObjectId(tenantId) || !EntraTokenClaims.IsValidObjectId(externalId))
                {
                    logger?.LogWarning(
                        "Bootstrap Entra principal skipped: TenantId and ExternalId must be directory ids (tid + oid).");
                    continue;
                }

                existing = await userRepository
                    .GetByIdentityLinkAsync(ProviderIdentityKey.Entra(tenantId, externalId), cancellationToken)
                    .ConfigureAwait(false);
                if (existing is null)
                {
                    if (!options.CreateUserIfMissing)
                    {
                        logger?.LogInformation(
                            "Bootstrap principal {Source}/{TenantId}/{ExternalId}: no user found and CreateUserIfMissing=false; skipped.",
                            source,
                            tenantId,
                            externalId);
                        continue;
                    }

                    var displayName = string.IsNullOrWhiteSpace(p.DisplayName)
                        ? $"Bootstrap administrator ({externalId})"
                        : p.DisplayName.Trim();

                    var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
                    var user = new User(
                        userId,
                        displayName,
                        [new IdentityLink(IdentitySource.Entra, externalId, LastAuthenticatedAt: null, TenantId: tenantId)],
                        [adminRoleId],
                        IsBreakGlassEligible: false);

                    await userRepository.AddAsync(user, cancellationToken).ConfigureAwait(false);
                    logger?.LogInformation(
                        "Created user {UserId} for bootstrap principal {Source}/{TenantId}/{ExternalId} with Platform Administrator role.",
                        userId.Value,
                        source,
                        tenantId,
                        externalId);
                    continue;
                }
            }
            else
            {
                existing = await userRepository.GetByIdentityLinkAsync(source, externalId, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                {
                    if (!options.CreateUserIfMissing)
                    {
                        logger?.LogInformation(
                            "Bootstrap principal {Source}/{ExternalId}: no user found and CreateUserIfMissing=false; skipped.",
                            source,
                            externalId);
                        continue;
                    }

                    var displayName = string.IsNullOrWhiteSpace(p.DisplayName)
                        ? $"Bootstrap administrator ({externalId})"
                        : p.DisplayName.Trim();

                    var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
                    var user = new User(
                        userId,
                        displayName,
                        [new IdentityLink(source, externalId, LastAuthenticatedAt: null)],
                        [adminRoleId],
                        IsBreakGlassEligible: false);

                    await userRepository.AddAsync(user, cancellationToken).ConfigureAwait(false);
                    logger?.LogInformation(
                        "Created user {UserId} for bootstrap principal {Source}/{ExternalId} with Platform Administrator role.",
                        userId.Value,
                        source,
                        externalId);
                    continue;
                }
            }

            var roles = await roleRepository.GetForUserAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            var hasAdmin = roles.Any(r => string.Equals(r.Id.Value, BootstrapConstants.PlatformAdminRoleId, StringComparison.Ordinal));
            if (!hasAdmin)
            {
                await roleRepository.AssignToUserAsync(existing.Id, adminRoleId, cancellationToken).ConfigureAwait(false);
                logger?.LogInformation(
                    "Assigned Platform Administrator role to user {UserId} for bootstrap principal {Source}/{ExternalId}.",
                    existing.Id.Value,
                    source,
                    externalId);
            }
        }
    }
}
