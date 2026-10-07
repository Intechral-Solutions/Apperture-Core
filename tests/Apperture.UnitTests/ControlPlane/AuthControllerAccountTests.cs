using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.UnitTests.Audit;
using Apperture.Core.Application;
using Apperture.Core.Application.Accounts;
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

using Apperture.UnitTests.Accounts;

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
        body.UserPrincipal.Should().Be(LocalUsername, "a local session shows its sign-in name, never the user id it is keyed by");
        body.SignInSource.Should().Be("Local");
        body.LocalCredentialStatus.Should().Be(AccountMeSnapshotBuilder.LocalCredentialStatusActive);
        body.CanChangeLocalPassword.Should().BeTrue();
        body.RoleNames.Should().Equal("Platform administrator");
        body.Roles.Should().Equal("rol_platform_admin");
        body.IdentityLinks.Should().ContainSingle(l =>
            l.Source == "Local" && l.DisplayLabel == LocalUsername);
        // Profile editing, avatar management, and notification preferences all ship in AUP-1, so the
        // session response no longer announces any deferred account capability.
        body.Deferred.Should().BeNull();
        body.DisplayTimeZoneId.Should().BeNull();
        // A user who has chosen nothing still gets the neutral built-in avatar, so the shell always
        // has something to draw.
        body.Avatar.Should().NotBeNull();
        body.Avatar!.Kind.Should().Be(UserAvatarStorageKinds.BuiltIn);
        body.Avatar.BuiltInOptionId.Should().Be(BuiltInAvatarCatalog.DefaultOptionId);
        body.Avatar.ImageUrl.Should().BeNull("built-in artwork is drawn by the client, not fetched");
        body.HasRecentAccountVerification.Should().BeFalse();
        body.RecentAccountVerificationExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Me_reports_stored_appearance_and_clock_preferences()
    {
        var preferences = new InMemoryUserPreferencesRepository();
        preferences.Seed(TestUserId.Value, new UserPreferences(AppearancePreference.Dark, TimeFormatPreference.Hour12));

        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credential: new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false),
            preferences: preferences);

        var result = await controller.Me(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<MeResponse>().Subject;
        body.Appearance.Should().Be("Dark");
        body.TimeFormat.Should().Be("Hour12");
    }

    [Fact]
    public async Task Me_reports_custom_avatar_with_a_fetchable_versioned_url()
    {
        var user = SampleUser() with { Avatar = UserAvatar.ForCustomImage("avt_abcdef0123456789") };

        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: user,
            credential: new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false));

        var result = await controller.Me(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<MeResponse>().Subject;
        body.Avatar!.Kind.Should().Be(UserAvatarStorageKinds.Custom);
        body.Avatar.Version.Should().Be("avt_abcdef0123456789");
        // The URL carries the version so a replaced avatar cannot be served from cache, and it never
        // exposes a storage location.
        body.Avatar.ImageUrl.Should().Be("api/account/avatar/image?size=128&v=avt_abcdef0123456789");
    }

    [Fact]
    public async Task Me_includes_display_timezone_when_user_has_preference()
    {
        var user = SampleUser() with { DisplayTimeZoneId = "America/Los_Angeles" };
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
        body.DisplayTimeZoneId.Should().Be("America/Los_Angeles");
    }

    [Fact]
    public async Task ChangeLocalPassword_success_updates_credential_and_audits()
    {
        var recorder = new AuditRecording();
        var credentialRepo = new MutableCredentialRepository(
            new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false));
        var hasher = new TrackingPasswordHasher(verifyCurrent: true);
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credentialRepo: credentialRepo,
            passwordHasher: hasher,
            recorder: recorder);

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old-pass", "new-password-ok"),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        hasher.HashCalls.Should().Be(1);
        credentialRepo.SetPasswordCalls.Should().Be(1);
        var (changed, actor) = recorder.Events.Should().ContainSingle().Subject;
        changed.Action.Should().Be("auth.local.password_change.success");
        actor.Should().Be(AuditActor.CurrentPrincipal);
        changed.Target.Should().Be(new AuditTarget(AuditTargetTypes.User, TestUserId.Value));
    }

    [Fact]
    public async Task ChangeLocalPassword_whose_acknowledgement_is_lost_answers_as_changed_without_a_refusal()
    {
        var recorder = new AuditRecording();
        recorder.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;
        var credentialRepo = new MutableCredentialRepository(
            new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false));
        var hasher = new TrackingPasswordHasher(verifyCurrent: true);
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credentialRepo: credentialRepo,
            passwordHasher: hasher,
            recorder: recorder);

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old-pass", "new-password-ok"),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>("a verified commit is the change, never a refusal");
        hasher.HashCalls.Should().Be(1, "nothing reran");
        recorder.Actions.Should().Equal("auth.local.password_change.success");
    }

    [Fact]
    public async Task ChangeLocalPassword_whose_outcome_is_unknown_is_neither_success_nor_a_refusal()
    {
        var recorder = new AuditRecording();
        recorder.Unit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credentialRepo: new MutableCredentialRepository(
                new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false)),
            passwordHasher: new TrackingPasswordHasher(verifyCurrent: true),
            recorder: recorder);

        var act = () => controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old-pass", "new-password-ok"),
            CancellationToken.None);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        recorder.Actions.Should().BeEmpty("an unconfirmed change is not recorded as committed or as refused");
    }

    [Fact]
    public async Task ChangeLocalPassword_with_recent_account_verification_omits_current_password()
    {
        var credentialRepo = new MutableCredentialRepository(
            new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false));
        var hasher = new TrackingPasswordHasher(verifyCurrent: false);
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credentialRepo: credentialRepo,
            passwordHasher: hasher,
            accountVerification: StubAccountVerification.Satisfied());

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest(null, "new-password-ok"),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        credentialRepo.SetPasswordCalls.Should().Be(1);
    }

    [Fact]
    public async Task ChangeLocalPassword_without_verification_or_current_password_is_forbidden()
    {
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credential: new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false),
            accountVerification: StubAccountVerification.Unsatisfied());

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest(null, "new-password-ok"),
            CancellationToken.None);

        var forbidden = result.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = forbidden.Value.Should().BeOfType<LocalPasswordChangeErrorResponse>().Subject;
        body.Code.Should().Be(LocalPasswordChangeErrorCodes.RecentVerificationRequired);
    }

    [Fact]
    public async Task ChangeLocalPassword_wrong_current_password_returns_bad_request()
    {
        var recorder = new AuditRecording();
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credential: new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false),
            passwordHasher: new TrackingPasswordHasher(verifyCurrent: false),
            recorder: recorder);

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("wrong", "new-password-ok"),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = bad.Value.Should().BeOfType<LocalPasswordChangeErrorResponse>().Subject;
        body.Code.Should().Be(LocalPasswordChangeErrorCodes.WrongCurrentPassword);
        var refused = recorder.Events.Should().ContainSingle().Subject.Event;
        refused.Action.Should().Be("auth.local.password_change.failed");
        refused.Fields["reason"].Should().Be("wrong_current_password");
        recorder.Unit.Operations.Should().ContainSingle().Which.NoOp.Should().BeTrue("the refusal changed nothing");
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
    public async Task ChangeLocalPassword_common_new_password_returns_bad_request()
    {
        var controller = NewController(
            actor: new HumanActor(new ActorId(LocalUsername), LocalUsername, isBreakGlassSession: false),
            resolvedUserId: TestUserId,
            user: SampleUser(),
            credential: new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: false));

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old-pass", "Password1!"),
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
            false)
        { Username = "no.local.link" };
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
    public async Task LocalLogin_disabled_credential_returns_unauthorized()
    {
        var controller = NewController(
            actor: null,
            user: SampleUser(),
            credential: new LocalCredentialData(TestUserId, "hash", "salt", "stub", IsDisabled: true),
            httpContext: new DefaultHttpContext());

        var result = await controller.LocalLogin(
            new LocalLoginRequest(LocalUsername, "Password1!"),
            CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task ChangeLocalPassword_unlinked_principal_returns_forbidden()
    {
        var recorder = new AuditRecording();
        var controller = NewController(
            actor: new HumanActor(new ActorId("unlinked"), "unlinked", isBreakGlassSession: false),
            resolvedUserId: null,
            recorder: recorder);

        var result = await controller.ChangeLocalPassword(
            new ChangeLocalPasswordRequest("old", "new-password-ok"),
            CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        recorder.Events.Should().ContainSingle().Which.Event.Fields["reason"].Should().Be("not_linked");
    }

    [Theory]
    [InlineData("localuser")]
    [InlineData("unknown@example.com")]
    [InlineData("breakglass")]
    public async Task ForgotPassword_always_returns_the_generic_acknowledgement(string identifier)
    {
        var forgotten = new RecordingForgottenPasswordService();
        var controller = NewController(actor: null, forgottenPassword: forgotten);

        var result = await controller.ForgotPassword(new ForgotPasswordRequest(identifier), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ForgotPasswordResponse>().Subject;
        body.Message.Should().Be(ForgottenPasswordService.GenericAcknowledgement);
        forgotten.Identifiers.Should().Equal(identifier);
    }

    private static User SampleUser() =>
        new(
            TestUserId,
            "Account Test User",
            [new IdentityLink(IdentitySource.Local, LocalUsername, DateTimeOffset.UtcNow)],
            [new RoleId("rol_platform_admin")],
            false)
        { Username = LocalUsername };

    private static DefaultHttpContext HttpContextWithLocalClaims(string username)
    {
        // A local session as the host issues it: identified by user id, with the username for display.
        var identity = LocalSessionIdentity.Create(TestUserId, username, "Account Test User");
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
        HttpContext? httpContext = null,
        InMemoryUserPreferencesRepository? preferences = null,
        IForgottenPasswordService? forgottenPassword = null,
        IRecentAccountVerificationGate? accountVerification = null,
        AuditRecording? recorder = null)
    {
        recorder ??= new AuditRecording();
        var controller = new AuthController(
            new ConfigurableUserRepository(user),
            credentialRepo ?? (ILocalCredentialRepository)new ConfigurableCredentialRepository(credential),
            passwordHasher ?? new TrackingPasswordHasher(verifyCurrent: true),
            new ConfigurablePrincipalContext(actor),
            new ConfigurablePrincipalToUserResolver(resolvedUserId),
            new ConfigurableRoleProvider(roles ?? []),
            preferences ?? new InMemoryUserPreferencesRepository(),
            audit ?? new CapturingAuditService(),
            Options.Create(localOptions ?? new LocalAuthOptions { Enabled = true, MinPasswordLength = 8 }),
            Options.Create(new BreakGlassAuthOptions()),
            Options.Create(new EntraAuthOptions()),
            new StubAuthUiPolicyRepository(),
            forgottenPassword: forgottenPassword,
            accountVerification: accountVerification,
            auditedUnitOfWork: recorder,
            auditEvents: recorder);

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
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                _user?.IdentityLinks.Any(l => l.Source == source && string.Equals(l.ExternalId, externalId, StringComparison.OrdinalIgnoreCase)) == true
                    ? _user
                    : null);
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

    private sealed class RecordingForgottenPasswordService : IForgottenPasswordService
    {
        public List<string> Identifiers { get; } = [];

        public ValueTask RequestAsync(string identifier, CancellationToken cancellationToken = default)
        {
            Identifiers.Add(identifier);
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(string token, string newPassword, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubAccountVerification : IRecentAccountVerificationGate
    {
        private readonly RecentAccountVerification _current;

        private StubAccountVerification(RecentAccountVerification current) => _current = current;

        public static StubAccountVerification Satisfied() =>
            new(new RecentAccountVerification(
                RecentAccountVerificationMechanisms.LocalPassword,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(14),
                IsSatisfied: true));

        public static StubAccountVerification Unsatisfied() => new(RecentAccountVerification.None);

        public RecentAccountVerification GetCurrent() => _current;

        public void Require()
        {
            if (!_current.IsSatisfied)
                throw new RecentAccountVerificationException(RecentAccountVerificationErrorCodes.Required, "Verify your identity.");
        }
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
