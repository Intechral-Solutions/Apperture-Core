using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
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

public sealed class AuthControllerBreakGlassTests
{
    [Fact]
    public async Task BreakGlassLogin_when_disabled_by_default_returns_not_found_with_stable_code()
    {
        var audit = new CapturingAuditService();
        var controller = NewController(breakGlassEnabled: false, audit: audit);

        var result = await controller.BreakGlassLogin(
            new LocalLoginRequest("admin", "Password1!"),
            CancellationToken.None);

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var body = notFound.Value.Should().BeOfType<BreakGlassAuthErrorResponse>().Subject;
        body.Code.Should().Be(BreakGlassAuthErrorCodes.Disabled);
        audit.Records.Should().ContainSingle(r => r.Action == "auth.break_glass.rejected_disabled");
        audit.Records.Should().NotContain(r => r.Action == "auth.break_glass.login.success");
    }

    [Fact]
    public async Task BreakGlassLogin_when_disabled_does_not_set_authentication_cookie()
    {
        var httpContext = new DefaultHttpContext();
        var controller = NewController(breakGlassEnabled: false, httpContext: httpContext);

        _ = await controller.BreakGlassLogin(
            new LocalLoginRequest("admin", "Password1!"),
            CancellationToken.None);

        httpContext.Response.Headers.ContainsKey("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public void BreakGlassAuthOptions_default_is_disabled()
    {
        new BreakGlassAuthOptions().Enabled.Should().BeFalse();
    }

    private static AuthController NewController(
        bool breakGlassEnabled,
        CapturingAuditService? audit = null,
        HttpContext? httpContext = null)
    {
        var controller = new AuthController(
            new StubUserRepository(),
            new StubLocalCredentialRepository(),
            new StubPasswordHasher(),
            new StubPrincipalContext(),
            new StubPrincipalToUserResolver(),
            new StubRoleProvider(),
            new InMemoryUserPreferencesRepository(),
            audit ?? new CapturingAuditService(),
            Options.Create(new LocalAuthOptions { Enabled = true }),
            Options.Create(new BreakGlassAuthOptions { Enabled = breakGlassEnabled }),
            Options.Create(new EntraAuthOptions()),
            new StubAuthUiPolicyRepository());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext ?? new DefaultHttpContext()
        };
        return controller;
    }

    private sealed class StubUserRepository : IUserRepository
    {
        public ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<User?> GetByIdAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => ValueTask.FromResult<(IReadOnlyList<User>, int)>(([], 0));
        public ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(null);
        public ValueTask AddAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubLocalCredentialRepository : ILocalCredentialRepository
    {
        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) => ValueTask.FromResult<LocalCredentialData?>(null);
        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class StubPasswordHasher : ILocalPasswordHasher
    {
        public string AlgorithmName => "stub";
        public (string Hash, string Salt) HashPassword(string password) => ("hash", "salt");
        public bool VerifyPassword(string password, string hash, string salt) => true;
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(null);
    }

    private sealed class StubPrincipalToUserResolver : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(null);
    }

    private sealed class StubRoleProvider : IRoleProvider
    {
        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

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
