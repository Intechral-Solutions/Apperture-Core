using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class AuthControllerAccountTests
{
    private static readonly UserId TestUserId = new("usr_account_test000001");
    private const string LocalUsername = "account-user";

    [Fact]
    public async Task Me_when_unauthenticated_returns_unauthorized()
    {
        var controller = NewController(actor: null);
        var result = await controller.Me(CancellationToken.None);
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Me_when_unlinked_returns_forbidden_and_audits()
    {
        var audit = new CapturingAuditService();
        var actor = new HumanActor(new ActorId("entra-unlinked"), "entra-oid-1234", isBreakGlassSession: false);
        var controller = NewController(actor: actor, resolvedUserId: null, audit: audit);

        var result = await controller.Me(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
        audit.Records.Should().ContainSingle(r => r.Action == "auth.unlinked_principal");
    }

    [Fact]
    public async Task Me_for_linked_local_user_returns_enriched_account_fields()
    {
        var user = SampleUser();
        var roles = new[]
        {
            new Role(new RoleId("rol_platform_admin"), "Platform administrator", null, [], true)
        };
        var credential = new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false);
        var httpContext = HttpContextWithLocalClaims(LocalUsername);
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: user,
            roles: roles,
            credential: credential,
            httpContext: httpContext);

        var result = await controller.Me(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<MeResponse>().Subject;
        body.UserId.Should().Be(TestUserId.Value);
        body.DisplayName.Should().Be("Account Test User");
        body.SignInSource.Should().Be("Local");
        body.LocalCredentialStatus.Should().Be(AccountMeSnapshotBuilder.LocalCredentialStatusActive);
        body.CanChangeLocalPassword.Should().BeTrue();
        body.RoleNames.Should().Equal("Platform administrator");
        body.Roles.Should().Equal("rol_platform_admin");
        body.IdentityLinks.Should().ContainSingle(l =>
            l.Source == "Local" && l.DisplayLabel == LocalUsername);
        body.Deferred.AvatarManagementDeferred.Should().BeTrue();
    }

    [Fact]
    public async Task ChangeLocalPassword_success_updates_credential_and_audits()
    {
        var audit = new CapturingAuditService();
        var credentialRepo = new MutableCredentialRepository(
            new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false));
        var hasher = new TrackingPasswordHasher(verifyCurrent: true);
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credentialRepo: credentialRepo,
            passwordHasher: hasher,
            audit: audit);

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old-pass", "new-password-ok"),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        hasher.HashCalls.Should().Be(1);
        credentialRepo.SetPasswordCalls.Should().Be(1);
        audit.Records.Should().ContainSingle(r => r.Action == "auth.local.password_change.success");
    }

    [Fact]
    public async Task ChangeLocalPassword_wrong_current_password_returns_bad_request()
    {
        var audit = new CapturingAuditService();
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credential: new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false),
            passwordHasher: new TrackingPasswordHasher(verifyCurrent: false),
            audit: audit);

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("wrong", "new-password-ok"),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = bad.Value.Should().BeOfType<LocalPasswordChangeErrorResponse>().Subject;
        body.Code.Should().Be(LocalPasswordChangeErrorCodes.WrongCurrentPassword);
        audit.Records.Should().ContainSingle(r => r.Action == "auth.local.password_change.failed");
    }

    [Fact]
    public async Task ChangeLocalPassword_too_short_new_password_returns_bad_request()
    {
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credential: new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false),
            localOptions: new LocalAuthOptions { Enabled = true, MinPasswordLength = 12 });

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old-pass", "short"),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = bad.Value.Should().BeOfType<LocalPasswordChangeErrorResponse>().Subject;
        body.Code.Should().Be(LocalPasswordChangeErrorCodes.InvalidNewPassword);
    }

    [Fact]
    public async Task ChangeLocalPassword_no_local_credential_returns_bad_request()
    {
        var user = new User(
            TestUserId,
            "No Local Link",
            [new IdentityLink(IdentitySource.Entra, "entra-oid-abcd", null)],
            [],
            false);
        var controller = NewController(
            actor: new HumanActor(new ActorId("entra-oid-abcd"), "entra-oid-abcd", isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: user,
            credential: null);

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old", "new-password-ok"),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = bad.Value.Should().BeOfType<LocalPasswordChangeErrorResponse>().Subject;
        body.Code.Should().Be(LocalPasswordChangeErrorCodes.NoCredential);
    }

    [Fact]
    public async Task ChangeLocalPassword_disabled_credential_returns_conflict()
    {
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credential: new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: true),
            passwordHasher: new TrackingPasswordHasher(verifyCurrent: true));

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old-pass", "new-password-ok"),
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        var body = conflict.Value.Should().BeOfType<LocalPasswordChangeErrorResponse>().Subject;
        body.Code.Should().Be(LocalPasswordChangeErrorCodes.CredentialDisabled);
    }

    [Fact]
    public async Task ChangeLocalPassword_unlinked_principal_returns_forbidden()
    {
        var audit = new CapturingAuditService();
        var controller = NewController(
            actor: new HumanActor(new ActorId("unlinked"), "unlinked", isBreakGlassSession: false),
            resolvedUserId: null,
            audit: audit);

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old", "new-password-ok"),
            CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        audit.Records.Should().ContainSingle(r => r.Action == "auth.local.password_change.failed");
    }

    private static User SampleUser() =>
        new(
            TestUserId,
            "Account Test User",
            [new IdentityLink(IdentitySource.Local, LocalUsername, DateTimeOffset.UtcNow)],
            [new RoleId("rol_platform_admin")],
            false);

    private static DefaultHttpContext HttpContextWithLocalClaims(string username)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, username));
        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local"));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return context;
    }

    private static AuthController NewController(
        IActor? actor,
        UserId? resolvedUserId = null,
        User? user = null,
        IReadOnlyList<Role>? roles = null,
        LocalCredentialData? credential = null,
        MutableCredentialRepository? credentialRepo = null,
        ILocalPasswordHasher? passwordHasher = null,
        CapturingAuditService? audit = null,
        LocalAuthOptions? localOptions = null,
        HttpContext? httpContext = null)
    {
        var controller = new AuthController(
            new ConfigurableUserRepository(user),
            credentialRepo ?? (ILocalCredentialRepository)new ConfigurableCredentialRepository(credential),
            passwordHasher ?? new TrackingPasswordHasher(verifyCurrent: true),
            new ConfigurablePrincipalContext(actor),
            new ConfigurablePrincipalToUserResolver(resolvedUserId),
            new ConfigurableRoleProvider(roles ?? []),
            audit ?? new CapturingAuditService(),
            Options.Create(localOptions ?? new LocalAuthOptions { Enabled = true, MinPasswordLength = 8 }),
            Options.Create(new BreakGlassAuthOptions()),
            Options.Create(new EntraAuthOptions()),
            new StubAuthUiPolicyRepository());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext ?? HttpContextWithLocalClaims(LocalUsername)
        };
        return controller;
    }

    private sealed class ConfigurableUserRepository : IUserRepository
    {
        private readonly User? _user;

        public ConfigurableUserRepository(User? user) => _user = user;

        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_user is null ? 0 : 1);
        public ValueTask<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult(_user);
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class ConfigurableCredentialRepository : ILocalCredentialRepository
    {
        private readonly LocalCredentialData? _credential;

        public ConfigurableCredentialRepository(LocalCredentialData? credential) => _credential = credential;

        public int SetPasswordCalls { get; private set; }

        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_credential);

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default)
        {
            SetPasswordCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class MutableCredentialRepository : ILocalCredentialRepository
    {
        private LocalCredentialData _credential;

        public MutableCredentialRepository(LocalCredentialData credential) => _credential = credential;

        public int SetPasswordCalls { get; private set; }

        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LocalCredentialData?>(_credential);

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default)
        {
            SetPasswordCalls++;
            _credential = _credential with { PasswordHash = passwordHash, Salt = salt, Algorithm = algorithm };
            return ValueTask.CompletedTask;
        }

        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class TrackingPasswordHasher : ILocalPasswordHasher
    {
        private readonly bool _verifyCurrent;

        public TrackingPasswordHasher(bool verifyCurrent) => _verifyCurrent = verifyCurrent;

        public int HashCalls { get; private set; }

        public string AlgorithmName => "stub";

        public (string Hash, string Salt) HashPassword(string password)
        {
            HashCalls++;
            return ("new-hash", "new-salt");
        }

        public bool VerifyPassword(string plainPassword, string storedHash, string storedSalt) => _verifyCurrent;
    }

    private sealed class ConfigurablePrincipalContext : IPrincipalContext
    {
        private readonly IActor? _actor;

        public ConfigurablePrincipalContext(IActor? actor) => _actor = actor;

        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_actor);
    }

    private sealed class ConfigurablePrincipalToUserResolver : IPrincipalToUserResolver
    {
        private readonly UserId? _userId;

        public ConfigurablePrincipalToUserResolver(UserId? userId) => _userId = userId;

        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_userId);
    }

    private sealed class ConfigurableRoleProvider : IRoleProvider
    {
        private readonly IReadOnlyList<Role> _roles;

        public ConfigurableRoleProvider(IReadOnlyList<Role> roles) => _roles = roles;

        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_roles);

        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_roles);

        public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubAuthUiPolicyRepository : IInstanceAuthUiPolicyRepository
    {
        public Task<InstanceAuthUiPolicy> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstanceAuthUiPolicy());

        public Task SaveAsync(InstanceAuthUiPolicy policy, byte[]? expectedRowVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CapturingAuditService : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AuditRecord?>(null);
    }
}
