using System.Security.Claims;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
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
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The password change of the signed-in user (<c>AuthController.ChangeLocalPassword</c>) against a designation that changes while it
/// is in progress, on real SQL Server and both isolation modes, through the production controller, repository and audited unit.
/// The change decides whether the account is system-managed from a copy it read, and then re-reads the stored designation under
/// the user's row lock as its last check before the write. These cases force the interleaving at that locked read: the designation
/// commits in a session of its own after the change's first read and before its locked read. They show the refusal is the
/// established one, that nothing is written or revoked, that the refusal is recorded as it always was, and (by the second case) that
/// the lock is taken inside the change's transaction and held to its end, so a check made outside the transaction cannot pass them.
/// Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class PasswordChangeDesignationSqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    private const string Password = "quiet-maple-lantern-harbor";
    private const string Next = "bright-cedar-window-meadow";
    private const string SystemManagedMessage = "This account is system-managed. Use Emergency Access operator tooling to change the password.";

    /// <summary>The statement every guarded writer reads the stored designation with, under the user's row lock.</summary>
    private const string LockedUserRead = "FROM [Users] WITH (";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public PasswordChangeDesignationSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    /// <summary>A failing case must not leave an operation parked with its locks.</summary>
    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ------------------------------------------------------------------------------------------------ the cases

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_password_change_for_an_account_designated_after_its_first_read_is_refused_as_system_managed_and_changes_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (user, token) = await OrdinaryUserWithResetTokenAsync(scratch);
        var pause = PauseAt.BeforeStatement(LockedUserRead);

        var change = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            return await Controller(scratch, db, new Principal(user)).ChangeLocalPassword(new ChangeLocalPasswordRequest(Password, Next), CancellationToken.None);
        });
        await ReachedAsync(pause, change);

        // The change has read the account as ordinary and passed every other check; it has not yet read the stored designation.
        (await SetDesignationAsync(scratch, user, expected: false, designated: true)).Should().BeTrue("the designation commits while the change holds a copy that says 'ordinary'");
        pause.Release();

        var refused = (await change.WaitAsync(Patience)).Should().BeOfType<ConflictObjectResult>().Subject;
        var body = refused.Value.Should().BeOfType<LocalPasswordChangeErrorResponse>().Subject;
        body.Code.Should().Be(LocalPasswordChangeErrorCodes.NoCredential, "the response contract of a system-managed account is unchanged");
        body.Message.Should().Be(SystemManagedMessage);

        (await VerifiesAsync(scratch, user, Password)).Should().BeTrue("no password was written");
        (await VerifiesAsync(scratch, user, Next)).Should().BeFalse();
        (await IsDesignatedAsync(scratch, user)).Should().BeTrue("the designation was not undone");
        (await RowsAsync(scratch, user, AuditActionNames.LocalPasswordChanged)).Should().BeEmpty("no success row for a change that was not made");
        var refusal = (await RowsAsync(scratch, user, AuditActionNames.LocalPasswordChangeRefused)).Should().ContainSingle().Which;
        refusal["Basis"].Should().Be("attempt");
        ((string)refusal["Details"]!).Should().Contain("system_managed").And.NotContain(Password).And.NotContain(Next);
        (await TokenIsActiveAsync(scratch, token)).Should().BeTrue("the account's password links were not revoked by a change that did not happen");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_waits_for_a_password_change_that_has_taken_the_row_lock_and_the_change_commits_first(bool rcsi)
    {
        // The recheck's lock belongs to the change's own transaction and is held until the change commits. A check made before the
        // transaction, or one that released its lock at the end of its statement, would let this designation through at once.
        var scratch = _catalogs.For(rcsi);
        var (user, token) = await OrdinaryUserWithResetTokenAsync(scratch);
        var pause = new PauseAt(c => c.CommandText.Contains(LockedUserRead, StringComparison.Ordinal), after: true);
        var world = new RoleAuthorityWorld(scratch);

        var change = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            return await Controller(scratch, db, new Principal(user)).ChangeLocalPassword(new ChangeLocalPasswordRequest(Password, Next), CancellationToken.None);
        });
        await ReachedAsync(pause, change);

        var designate = Task.Run(() => SetDesignationAsync(scratch, user, expected: false, designated: true));
        await world.WaitUntilBlockedOnAsync("Users", designate);
        designate.IsCompleted.Should().BeFalse("the change holds the user's row: the designation serializes behind it, so the change is never applied to an account that became designated under it");

        pause.Release();
        (await change.WaitAsync(Patience)).Should().BeOfType<NoContentResult>();
        (await designate.WaitAsync(Patience)).Should().BeTrue("the designation applies once the change has committed");

        (await VerifiesAsync(scratch, user, Next)).Should().BeTrue();
        (await IsDesignatedAsync(scratch, user)).Should().BeTrue();
        (await RowsAsync(scratch, user, AuditActionNames.LocalPasswordChanged)).Should().ContainSingle();
        (await TokenIsActiveAsync(scratch, token)).Should().BeFalse("the committed change revoked the account's password links, in the same transaction");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_password_change_for_an_account_designated_all_along_gets_the_same_refusal(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (user, _) = await OrdinaryUserWithResetTokenAsync(scratch);
        await SetDesignationAsync(scratch, user, expected: false, designated: true);

        await using var db = scratch.NewDb();
        var refused = (await Controller(scratch, db, new Principal(user)).ChangeLocalPassword(new ChangeLocalPasswordRequest(Password, Next), CancellationToken.None))
            .Should().BeOfType<ConflictObjectResult>().Subject;

        var body = refused.Value.Should().BeOfType<LocalPasswordChangeErrorResponse>().Subject;
        (body.Code, body.Message).Should().Be((LocalPasswordChangeErrorCodes.NoCredential, SystemManagedMessage));
        (await VerifiesAsync(scratch, user, Password)).Should().BeTrue();
        (await RowsAsync(scratch, user, AuditActionNames.LocalPasswordChanged)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_ordinary_password_change_is_unchanged(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (user, token) = await OrdinaryUserWithResetTokenAsync(scratch);

        await using var db = scratch.NewDb();
        (await Controller(scratch, db, new Principal(user)).ChangeLocalPassword(new ChangeLocalPasswordRequest(Password, Next), CancellationToken.None))
            .Should().BeOfType<NoContentResult>();

        (await VerifiesAsync(scratch, user, Next)).Should().BeTrue();
        (await RowsAsync(scratch, user, AuditActionNames.LocalPasswordChanged)).Should().ContainSingle();
        (await RowsAsync(scratch, user, AuditActionNames.LocalPasswordChangeRefused)).Should().BeEmpty();
        (await TokenIsActiveAsync(scratch, token)).Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------------------- helpers

    private static async Task ReachedAsync(PauseAt pause, Task operation)
    {
        var first = await Task.WhenAny(pause.Reached, operation, Task.Delay(Patience));
        if (first == operation)
        {
            await operation;
            throw new Xunit.Sdk.XunitException("The operation finished without reaching the pause.");
        }

        if (first != pause.Reached)
            throw new TimeoutException("The operation did not reach the pause.");
    }

    /// <summary>A new ordinary user with a Local credential and an unrevoked password-reset link; no other account is designated.</summary>
    private static async Task<(string Id, string Token)> OrdinaryUserWithResetTokenAsync(AuditSqlScratch scratch)
    {
        await scratch.ExecuteAsync("UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;");
        var id = NewUserId();
        await scratch.AddUserAsync(id, NewUsername("pwchange"));
        var hasher = new LocalPasswordHasher();
        var (hash, salt) = hasher.HashPassword(Password);
        await using var db = scratch.NewDb();
        await new SqlLocalCredentialRepository(db).SetPasswordAsync(new UserId(id), hash, salt, hasher.AlgorithmName);

        var tokens = new AccountActionTokenService(new SqlAccountActionTokenRepository(db), TimeProvider.System);
        var prepared = tokens.Prepare();
        await tokens.IssueAsync(new UserId(id), AccountActionTokenPurpose.PasswordReset, null, TimeSpan.FromHours(1), prepared);
        return (id, prepared.Plaintext);
    }

    private static async Task<bool> TokenIsActiveAsync(AuditSqlScratch scratch, string plaintext)
    {
        await using var db = scratch.NewDb();
        var tokens = new AccountActionTokenService(new SqlAccountActionTokenRepository(db), TimeProvider.System);
        return await tokens.FindActiveAsync(plaintext, AccountActionTokenPurpose.PasswordReset) is not null;
    }

    private static async Task<bool> SetDesignationAsync(AuditSqlScratch scratch, string userId, bool expected, bool designated)
    {
        await using var db = scratch.NewDb();
        return await new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance).TrySetDesignationAsync(new UserId(userId), expected, designated);
    }

    private static Task<bool> IsDesignatedAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<bool>($"SELECT IsBreakGlassEligible FROM Users WHERE Id = N'{userId}';");

    private static Task<List<Dictionary<string, object?>>> RowsAsync(AuditSqlScratch scratch, string userId, string action) =>
        scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetId = N'{userId}' AND Action = N'{action}' ORDER BY Id;");

    private static async Task<bool> VerifiesAsync(AuditSqlScratch scratch, string userId, string password)
    {
        await using var db = scratch.NewDb();
        var credential = await new SqlLocalCredentialRepository(db).GetByUserIdAsync(new UserId(userId));
        return credential is not null && new LocalPasswordHasher().VerifyPassword(password, credential.PasswordHash, credential.Salt);
    }

    private static AuthController Controller(AuditSqlScratch scratch, AppertureDbContext db, IPrincipalContext principal)
    {
        var controller = new AuthController(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlLocalCredentialRepository(db),
            new LocalPasswordHasher(),
            principal,
            new SelfResolver(),
            new NoRoles(),
            new SqlUserPreferencesRepository(db),
            new NoBridge(),
            Options.Create(new LocalAuthOptions { Enabled = true }),
            Options.Create(new BreakGlassAuthOptions()),
            Options.Create(new EntraAuthOptions()),
            new NoAuthUiPolicy(),
            actionTokens: new AccountActionTokenService(new SqlAccountActionTokenRepository(db), TimeProvider.System),
            auditedUnitOfWork: NewUnit(db, principal),
            auditEvents: new SqlAuditEventWriter(scratch.Options(), principal, HarnessCatalog, null, null, null));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(new NoAuthentication()).BuildServiceProvider(),
            },
        };
        return controller;
    }

    private sealed class NoAuthentication : IAuthenticationService
    {
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
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
    }

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
