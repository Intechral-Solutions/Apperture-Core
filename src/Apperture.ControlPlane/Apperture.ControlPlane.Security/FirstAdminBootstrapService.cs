using Apperture.Core.Application;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// First-admin bootstrap only; succeeds when zero platform users exist. Creates one user, one identity link, and assigns platform administrator role in a single transaction.
/// For Local source, externalId is normalized (trim, lowercase). Optional password creates local credential when source is Local.
/// </summary>
/// <remarks>
/// AUD-1.2: the bootstrap request is anonymous. Every check (no user yet, the role, the identity) is read inside one audited
/// unit of work, and the user, its link, its role, its credential and the created row commit together. A refusal changes
/// nothing; the caller records it. Hardening the first-administrator window itself is OPS-1.4.
/// </remarks>
public sealed class FirstAdminBootstrapService : IFirstAdminBootstrapService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILocalCredentialRepository? _localCredentialRepository;
    private readonly ILocalPasswordHasher? _passwordHasher;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public FirstAdminBootstrapService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILocalCredentialRepository? localCredentialRepository = null,
        ILocalPasswordHasher? passwordHasher = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _localCredentialRepository = localCredentialRepository;
        _passwordHasher = passwordHasher;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("First-administrator bootstrap requires the audited unit of work.");

    /// <inheritdoc />
    public async Task<FirstAdminBootstrapResult> TryCreateFirstAdminAsync(
        string displayName,
        IdentitySource source,
        string externalId,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        // Chosen once, so a rerun after a confirmed rollback creates the same user.
        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var setsPassword = source == IdentitySource.Local && !string.IsNullOrEmpty(password)
            && _localCredentialRepository is not null && _passwordHasher is not null;

        return await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.FirstAdminCreated, AuditActor.Anonymous),
            async (transaction, ct) =>
            {
                FirstAdminBootstrapResult Refuse(FirstAdminBootstrapResult result)
                {
                    transaction.DeclareNoOp();
                    return result;
                }

                var count = await _userRepository.GetUserCountAsync(ct);
                if (count > 0)
                    return Refuse(FirstAdminBootstrapResult.AlreadyExists);

                var role = await _roleRepository.GetAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId), ct);
                if (role is null)
                    return Refuse(FirstAdminBootstrapResult.RoleNotFound);

                // For Local, use normalized username as IdentityLink.ExternalId (canonical for login).
                var linkExternalId = source == IdentitySource.Local
                    ? LocalAuthHelpers.NormalizeUsername(externalId)
                    : externalId.Trim();

                if (source == IdentitySource.Local && EmergencyAccessIdentity.IsReservedLocalUsername(linkExternalId))
                    return Refuse(FirstAdminBootstrapResult.DuplicateIdentityLink);

                if (source == IdentitySource.Entra)
                    return Refuse(FirstAdminBootstrapResult.DuplicateIdentityLink);

                // The first admin's sign-in name is a new username assignment (throws UsernameException).
                UsernamePolicy.RequireNewAssignment(linkExternalId);

                var existing = await _userRepository.GetByIdentityLinkAsync(source, linkExternalId, ct);
                if (existing is not null)
                    return Refuse(FirstAdminBootstrapResult.DuplicateIdentityLink);

                if (setsPassword)
                    LocalPasswordPolicy.Require(password, LocalPasswordPolicy.ForLocalIdentity(linkExternalId, displayName));

                var user = new User(
                    userId,
                    displayName,
                    new[] { new IdentityLink(source, linkExternalId, LastAuthenticatedAt: null) },
                    new[] { new RoleId(BootstrapConstants.PlatformAdminRoleId) },
                    IsBreakGlassEligible: false)
                {
                    Username = linkExternalId
                };
                await _userRepository.AddAsync(user, ct);

                if (setsPassword)
                {
                    var (hash, salt) = _passwordHasher!.HashPassword(password!);
                    await _localCredentialRepository!.SetPasswordAsync(userId, hash, salt, _passwordHasher.AlgorithmName, ct);
                }

                await transaction.StageAsync(AuditEvents.FirstAdminCreated(userId, linkExternalId, setsPassword), ct);
                return FirstAdminBootstrapResult.Success(userId);
            },
            (_, _) => Task.FromResult(FirstAdminBootstrapResult.Success(userId)),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BootstrapSetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        var count = await _userRepository.GetUserCountAsync(cancellationToken);
        return BootstrapSetupStatus.FromUserCount(count);
    }
}

/// <summary>
/// Helpers for local auth. Normalized username is trim + lowercase (canonical for IdentityLink.ExternalId when source is Local).
/// </summary>
public static class LocalAuthHelpers
{
    /// <summary>The single username normalization; see <see cref="UsernamePolicy.Normalize"/>.</summary>
    public static string NormalizeUsername(string username) => UsernamePolicy.Normalize(username);
}
