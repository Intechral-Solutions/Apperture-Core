using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.2 authentication against SQL Server (audit hardening design gate §13.4; acceptance "auth audit failure withholds the
/// success result"): a sign-in's W2 row commits before the session is issued, and when it cannot be written no session is
/// issued; a password change commits with its row or not at all, and a refusal is recorded after it changed nothing. Fails,
/// never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuthenticationAuditSqlServerTests : IAsyncLifetime
{
    private const string Password = "quiet-maple-lantern-harbor";

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public AuthenticationAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud12Auth_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task A_local_sign_in_commits_its_row_before_the_session_and_a_failed_row_issues_none()
    {
        var (user, username) = await AddUserWithPasswordAsync("signin");

        var signedIn = new CapturingAuthentication();
        await using (var db = _db.NewDb())
        {
            var result = await Controller(db, signedIn, principal: null).LocalLogin(new LocalLoginRequest(username, Password), CancellationToken.None);
            result.Result.Should().BeOfType<OkObjectResult>();
        }

        signedIn.Count.Should().Be(1);
        var row = (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{AuditActionNames.LocalSignInSucceeded}' AND TargetId = N'{user}';"))
            .Should().ContainSingle().Which;
        row["Actor"].Should().Be(user);
        row["Basis"].Should().Be("security_event");
        ((string)row["Details"]!).Should().NotContain(Password);

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var withheld = new CapturingAuthentication();
        await using (var db = _db.NewDb())
        {
            var act = () => Controller(db, withheld, principal: null, writerInterceptors: [failAudit]).LocalLogin(new LocalLoginRequest(username, Password), CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        withheld.Count.Should().Be(0, "the success result is withheld when its W2 row cannot be written");
    }

    [Fact]
    public async Task A_password_change_commits_with_its_row_or_not_at_all_and_a_refusal_is_recorded_after_it_changed_nothing()
    {
        var (user, _) = await AddUserWithPasswordAsync("changer");
        var principal = new Principal(user);
        const string Next = "bright-cedar-window-meadow";

        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Controller(db, new CapturingAuthentication(), principal).ChangeLocalPassword(new ChangeLocalPasswordRequest(Password, Next), CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }
        (await VerifiesAsync(user, Password)).Should().BeTrue("the change never commits without its row");

        await using (var db = _db.NewDb())
        {
            var refused = await Controller(db, new CapturingAuthentication(), principal).ChangeLocalPassword(new ChangeLocalPasswordRequest("not-the-password", Next), CancellationToken.None);
            refused.Should().BeOfType<BadRequestObjectResult>();
        }
        (await VerifiesAsync(user, Password)).Should().BeTrue();

        await using (var db = _db.NewDb())
        {
            var changed = await Controller(db, new CapturingAuthentication(), principal).ChangeLocalPassword(new ChangeLocalPasswordRequest(Password, Next), CancellationToken.None);
            changed.Should().BeOfType<NoContentResult>();
        }
        (await VerifiesAsync(user, Next)).Should().BeTrue();

        var rows = await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetType = N'user' AND TargetId = N'{user}' ORDER BY Id;");
        rows.Select(r => r["Action"]).Should().Equal(AuditActionNames.LocalPasswordChangeRefused, AuditActionNames.LocalPasswordChanged);
        rows.Should().OnlyContain(r => (string)r["Actor"]! == user);
        rows[0]["Basis"].Should().Be("attempt");
        rows.Should().OnlyContain(r => !((string)r["Details"]!).Contains(Password) && !((string)r["Details"]!).Contains(Next));
    }

    // ---- AUD-1.2 review (D4): a recovery code consumed with its own row, then a sign-in row that cannot be written ----

    [Fact]
    public async Task A_recovery_code_consumed_before_a_failed_sign_in_row_stays_consumed_with_its_row_and_no_session_is_issued()
    {
        var (user, _) = await AddUserWithPasswordAsync("mfa");
        var codes = await EnrollWithRecoveryCodesAsync(user);

        var failSignInRow = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var withheld = new CapturingAuthentication();
        await using (var db = _db.NewDb())
        {
            var act = () => MfaController(db, withheld, user, writerInterceptors: [failSignInRow])
                .LocalMfa(new LocalMfaChallengeRequest(null, codes[0]), CancellationToken.None);
            await act.Should().ThrowAsync<Exception>();
        }

        failSignInRow.Failures.Should().BeGreaterThan(0);
        withheld.Count.Should().Be(0, "no session is issued when the sign-in row cannot be written");
        (await UnusedCodesAsync(user)).Should().Be(codes.Count - 1, "the consumption committed with its own row before the sign-in row was attempted");
        var consumed = (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{AuditActionNames.RecoveryCodeConsumed}' AND TargetId = N'{user}';"))
            .Should().ContainSingle().Which;
        consumed["Actor"].Should().Be(user);
        (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{AuditActionNames.LocalSignInSucceeded}' AND TargetId = N'{user}';")).Should().BeEmpty();

        // The same code is refused next time: it is used up, which is what the sign-in form now tells the user.
        var retried = new CapturingAuthentication();
        await using (var db = _db.NewDb())
        {
            var result = await MfaController(db, retried, user).LocalMfa(new LocalMfaChallengeRequest(null, codes[0]), CancellationToken.None);
            result.Result.Should().BeOfType<UnauthorizedResult>();
        }

        retried.Count.Should().Be(0);
        await NoRowCarriesAnyCodeAsync(codes);
    }

    [Fact]
    public async Task A_recovery_code_consumption_whose_outcome_is_unknown_is_reported_unknown_and_issues_no_session()
    {
        var (user, _) = await AddUserWithPasswordAsync("mfa.unknown");
        var codes = await EnrollWithRecoveryCodesAsync(user);

        var signedIn = new CapturingAuthentication();
        await using (var db = _db.NewDb(new LostCommitAcknowledgement()))
        {
            var act = () => MfaController(db, signedIn, user, verification: new UnreachableVerification())
                .LocalMfa(new LocalMfaChallengeRequest(null, codes[0]), CancellationToken.None);
            await act.Should().ThrowAsync<AuditOutcomeUnknownException>("never a refusal and never a confirmed rollback");
        }

        signedIn.Count.Should().Be(0);
        (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{AuditActionNames.LocalSignInSucceeded}' AND TargetId = N'{user}';")).Should().BeEmpty();
        await NoRowCarriesAnyCodeAsync(codes);
    }

    private async Task<IReadOnlyList<string>> EnrollWithRecoveryCodesAsync(string userId)
    {
        var id = new UserId(userId);
        await using (var db = _db.NewDb())
        {
            var totp = new SqlNativeTotpCredentialRepository(db);
            await totp.UpsertPendingAsync(id, [1, 2, 3], "test-key", DateTimeOffset.UtcNow);
            await totp.MarkEnrolledAsync(id, 1, DateTimeOffset.UtcNow);
        }

        await using (var db = _db.NewDb())
            return await Mfa(db, NewUnit(db, null)).RegenerateRecoveryCodesAsync(id, AuditActor.EmergencyCli);
    }

    private Task<int> UnusedCodesAsync(string userId) =>
        _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId = N'{userId}' AND ConsumedAtUtc IS NULL;");

    private async Task NoRowCarriesAnyCodeAsync(IReadOnlyList<string> codes)
    {
        var details = (await _db.RowsAsync("SELECT Details FROM AuditLogs WHERE Details IS NOT NULL;")).Select(r => (string)r["Details"]!).ToList();
        foreach (var code in codes)
        {
            details.Should().NotContain(d => d.Contains(code, StringComparison.OrdinalIgnoreCase));
            details.Should().NotContain(d => d.Contains(MfaRecoveryCodes.Normalize(code), StringComparison.OrdinalIgnoreCase));
        }
    }

    private static NativeMfaService Mfa(AppertureDbContext db, IAuditedUnitOfWork unit) =>
        new(
            new SqlNativeTotpCredentialRepository(db),
            new SqlNativeMfaRecoveryCodeRepository(db),
            new LocalPasswordHasher(),
            new PassThroughProtector(),
            TimeProvider.System,
            NullLogger<NativeMfaService>.Instance,
            unit);

    /// <summary>The local MFA step of a user whose password this request's pending ticket already verified.</summary>
    private AuthController MfaController(
        AppertureDbContext db,
        CapturingAuthentication authentication,
        string userId,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[]? writerInterceptors = null,
        IAuditVerificationConnectionFactory? verification = null)
    {
        var unit = NewUnit(db, null, verification: verification);
        var controller = new AuthController(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            new NoPrincipal(),
            new SelfResolver(),
            new NoRoles(),
            new SqlUserPreferencesRepository(db),
            new NoBridge(),
            Options.Create(new LocalAuthOptions { Enabled = true }),
            Options.Create(new BreakGlassAuthOptions()),
            Options.Create(new EntraAuthOptions()),
            new NoAuthUiPolicy(),
            nativeMfa: Mfa(db, unit),
            pendingMfaTickets: new TicketFor(new UserId(userId)),
            auditedUnitOfWork: unit,
            auditEvents: new SqlAuditEventWriter(_db.Options(writerInterceptors ?? []), null, HarnessCatalog, null, null, null));
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider(),
        };
        http.Request.Headers.Cookie = "apperture.mfa.pending=ticket";
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private sealed class TicketFor(UserId userId) : IPendingMfaLoginTicketService
    {
        public ValueTask<string> IssueAsync(UserId user, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => ValueTask.FromResult("ticket");

        public ValueTask<PendingMfaLoginTicket?> RedeemAsync(string token, DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PendingMfaLoginTicket?>(new PendingMfaLoginTicket(userId, utcNow.AddMinutes(5)));
    }

    private sealed class PassThroughProtector : Apperture.Core.Security.Secrets.Abstractions.ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(plaintext.ToArray());

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(protectedPayload.ToArray());
    }

    // ---- helpers ----

    private async Task<(string Id, string Username)> AddUserWithPasswordAsync(string stem)
    {
        var id = NewUserId();
        var username = NewUsername(stem);
        await _db.AddUserAsync(id, username);
        var hasher = new LocalPasswordHasher();
        var (hash, salt) = hasher.HashPassword(Password);
        await using var db = _db.NewDb();
        await new SqlLocalCredentialRepository(db).SetPasswordAsync(new UserId(id), hash, salt, hasher.AlgorithmName);
        return (id, username);
    }

    private async Task<bool> VerifiesAsync(string userId, string password)
    {
        await using var db = _db.NewDb();
        var credential = await new SqlLocalCredentialRepository(db).GetByUserIdAsync(new UserId(userId));
        return credential is not null && new LocalPasswordHasher().VerifyPassword(password, credential.PasswordHash, credential.Salt);
    }

    private AuthController Controller(
        AppertureDbContext db,
        CapturingAuthentication authentication,
        IPrincipalContext? principal,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[]? writerInterceptors = null)
    {
        var users = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
        var controller = new AuthController(
            users,
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            principal ?? new NoPrincipal(),
            new SelfResolver(),
            new NoRoles(),
            new SqlUserPreferencesRepository(db),
            new NoBridge(),
            Options.Create(new LocalAuthOptions { Enabled = true }),
            Options.Create(new BreakGlassAuthOptions()),
            Options.Create(new EntraAuthOptions()),
            new NoAuthUiPolicy(),
            auditedUnitOfWork: NewUnit(db, principal),
            auditEvents: new SqlAuditEventWriter(_db.Options(writerInterceptors ?? []), principal, HarnessCatalog, null, null, null));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider(),
            },
        };
        return controller;
    }

    private sealed class CapturingAuthentication : IAuthenticationService
    {
        public int Count { get; private set; }

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            Count++;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class NoPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IActor?>(null);
    }

    private sealed class SelfResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId(actor.Id.Value));
    }

    private sealed class NoRoles : IRoleProvider
    {
        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.FromResult<Role?>(null);
        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<Role>>([]);
        public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    /// <summary>Sign-in failure rows stay on the append bridge (IAM-11.4); they are not under test here.</summary>
    private sealed class NoBridge : IAuditService
    {
        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new AuditPage([], null));
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<AuditRecord?>(null);
    }

    private sealed class NoAuthUiPolicy : IInstanceAuthUiPolicyRepository
    {
        public Task<InstanceAuthUiPolicy> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new InstanceAuthUiPolicy());
        public Task SaveAsync(InstanceAuthUiPolicy policy, byte[]? expectedRowVersion, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
