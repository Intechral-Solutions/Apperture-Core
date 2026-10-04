using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// IAM-9.2 through the real Management Host and its real session cookie: sessions are identified by user id,
/// stay bound to the identity link they were issued through, survive a username change, and pre-upgrade
/// cookies are rejected. Entra cookies are genuine tickets in the shape the OpenID Connect sign-in issues
/// (that issuance itself is proven against the real handler in the unit suite).
/// </summary>
[Collection(BootstrapHttpCollection.Name)]
public sealed class ManagementHostSessionIdentityHttpTests
{
    private const string SessionCookie = ".AspNetCore.Cookies";
    private const string Password = "session-identity-passphrase";
    private const string Tenant = "5b1c2d3e-4f50-4a61-8b72-9c83d4e5f607";
    private const string OtherTenant = "11111111-2222-4333-8444-555555555555";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly BootstrapHttpFixture _fixture;

    public ManagementHostSessionIdentityHttpTests(BootstrapHttpFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Local_sign_in_issues_a_user_id_session_that_resolves_to_the_same_user()
    {
        var person = await CreatePersonAsync(withEntraLink: false);
        var (client, cookie) = await SignInLocallyAsync(person.Username);

        var ticket = Unprotect(cookie);
        ticket.FindAll(ClaimTypes.NameIdentifier).Select(c => c.Value).Should().Equal(person.Id.Value);
        ticket.FindFirst(ApplicationSessionClaims.SessionFormatClaimType)!.Value.Should().Be("2");
        ticket.FindFirst(ApplicationSessionClaims.UsernameClaimType)!.Value.Should().Be(person.Username);
        ticket.FindFirst(ClaimsPrincipalToActorMapper.IdentitySourceClaimType)!.Value.Should().Be("Local");

        var me = await MeAsync(client);
        me.UserId.Should().Be(person.Id.Value);
        me.UserPrincipal.Should().Be(person.Username, "the sign-in name is shown, not the user id");
        me.SignInSource.Should().Be("Local");
    }

    [Fact]
    public async Task A_username_change_does_not_end_an_existing_local_session()
    {
        var person = await CreatePersonAsync(withEntraLink: false);
        var (client, _) = await SignInLocallyAsync(person.Username);
        var renamed = person.Username + ".new";

        await using (var scope = _fixture.Factory!.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            (await new UsernameService(users).RenameAsync(person.Id, renamed)).Should().Be(UsernameChangeOutcome.Assigned);
        }

        (await MeAsync(client)).UserId.Should().Be(person.Id.Value, "the session is keyed by user id, and the Local link still exists under its new name");

        var oldName = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/local/login", new LocalLoginRequest(person.Username, Password));
        oldName.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var newName = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/local/login", new LocalLoginRequest(renamed, Password));
        newName.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Removing_the_local_link_ends_the_local_session_and_the_refusal_is_attributed_to_the_user_id()
    {
        var person = await CreatePersonAsync(withEntraLink: true);
        var (client, _) = await SignInLocallyAsync(person.Username);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        await RemoveLinkAsync(person.Id, IdentitySource.Local, person.Username);

        var after = await client.GetAsync("/api/auth/me");
        after.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the link the session came through is gone: the terminal account-unavailable outcome");
        (await LatestActorAsync("auth.unlinked_principal")).Should().Be(person.Id.Value, "new actor values written through the mapper are user ids");
    }

    [Fact]
    public async Task An_entra_session_resolves_to_the_same_user_id_as_a_local_session_and_ends_when_its_link_is_removed()
    {
        var person = await CreatePersonAsync(withEntraLink: true);
        var (localClient, _) = await SignInLocallyAsync(person.Username);
        var entraClient = ClientWithCookie(EntraSessionCookie(person, Tenant, person.Oid!));

        var viaEntra = await MeAsync(entraClient);
        var viaLocal = await MeAsync(localClient);
        viaEntra.UserId.Should().Be(person.Id.Value);
        viaEntra.UserId.Should().Be(viaLocal.UserId, "provider provenance differs, the canonical identity does not");
        viaEntra.SignInSource.Should().Be("Entra");

        await RemoveLinkAsync(person.Id, IdentitySource.Entra, person.Oid!);

        (await entraClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "unlinking the Entra identity ends the session that came through it");
        (await localClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK, "the local session is bound to its own link");
    }

    [Fact]
    public async Task An_entra_session_is_refused_when_its_tenant_and_object_id_are_not_the_users_link()
    {
        var person = await CreatePersonAsync(withEntraLink: true);

        var otherTenant = ClientWithCookie(EntraSessionCookie(person, OtherTenant, person.Oid!));
        (await otherTenant.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var otherObject = ClientWithCookie(EntraSessionCookie(person, Tenant, Guid.NewGuid().ToString()));
        (await otherObject.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pre_upgrade_local_and_entra_cookies_are_rejected_once_and_touch_no_identity_link()
    {
        var person = await CreatePersonAsync(withEntraLink: true);
        var before = await LinkTimestampsAsync(person.Id);

        var oldLocal = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        oldLocal.AddClaim(new Claim(ClaimTypes.NameIdentifier, person.Username));
        oldLocal.AddClaim(new Claim(ClaimTypes.Name, "Session Test"));
        oldLocal.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local"));

        var oldEntra = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        oldEntra.AddClaim(new Claim("oid", person.Oid!));
        oldEntra.AddClaim(new Claim("tid", Tenant));
        oldEntra.AddClaim(new Claim("sub", "subject-1"));
        oldEntra.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra"));

        foreach (var old in new[] { oldLocal, oldEntra })
        {
            var client = _fixture.Factory!.CreateClient(new() { HandleCookies = false });
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            request.Headers.Add("Cookie", $"{SessionCookie}={Protect(old)}");

            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "an old cookie is an ordinary unauthenticated request: the client goes to sign-in");
            response.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
            setCookies!.Should().Contain(c => c.StartsWith(SessionCookie + "=;", StringComparison.Ordinal), "the old cookie is cleared");
        }

        (await LinkTimestampsAsync(person.Id)).Should().Equal(before, "rejection is a cookie-format check: no identity link is touched");
    }

    // ---------------------------------------------------------------------------------------------------------------

    private sealed record Person(UserId Id, string Username, string? Oid);

    private async Task<Person> CreatePersonAsync(bool withEntraLink)
    {
        if (_fixture.ConnectionString is null || _fixture.Factory is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());

        // An ordinary user must never be the first platform user (that would close first-admin bootstrap).
        _ = await BootstrapHttpPlatformAdmin.EnsureLocalExternalIdAsync(_fixture.Factory);

        var id = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var username = "session-" + Guid.NewGuid().ToString("N")[..10];
        var oid = withEntraLink ? Guid.NewGuid().ToString() : null;
        var links = new List<IdentityLink> { new(IdentitySource.Local, username, LastAuthenticatedAt: null) };
        if (oid is not null)
            links.Add(new IdentityLink(IdentitySource.Entra, oid, DateTimeOffset.UtcNow.AddDays(-1), TenantId: Tenant));

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserRepository>()
            .AddAsync(new User(id, "Session Test", links, [], false) { Username = username });
        var hasher = scope.ServiceProvider.GetRequiredService<ILocalPasswordHasher>();
        var (hash, salt) = hasher.HashPassword(Password);
        await scope.ServiceProvider.GetRequiredService<ILocalCredentialRepository>().SetPasswordAsync(id, hash, salt, hasher.AlgorithmName);
        return new Person(id, username, oid);
    }

    private async Task<(HttpClient Client, string Cookie)> SignInLocallyAsync(string username)
    {
        var client = _fixture.Factory!.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/local/login", new LocalLoginRequest(username, Password));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = login.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';')[0])
            .Single(c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal))[(SessionCookie.Length + 1)..];
        return (client, cookie);
    }

    private async Task<MeResponse> MeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<MeResponse>(JsonOptions))!;
    }

    /// <summary>An Entra session as OnTokenValidated issues it: token tid/oid provenance plus the user-id session identity.</summary>
    private string EntraSessionCookie(Person person, string tenantId, string objectId)
    {
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim("oid", objectId));
        identity.AddClaim(new Claim("tid", tenantId));
        identity.AddClaim(new Claim("name", "Session Test"));
        ApplicationSessionClaims.Apply(identity, person.Id, person.Username, "Session Test", ApplicationSessionClaims.EntraSource);
        return Protect(identity);
    }

    private HttpClient ClientWithCookie(string cookieValue)
    {
        var client = _fixture.Factory!.CreateClient(new() { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie}={cookieValue}");
        return client;
    }

    private CookieAuthenticationOptions CookieOptions() =>
        _fixture.Factory!.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>A genuine ticket, signed by the host's own cookie handler data protection.</summary>
    private string Protect(ClaimsIdentity identity) =>
        CookieOptions().TicketDataFormat.Protect(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) },
            CookieAuthenticationDefaults.AuthenticationScheme));

    private ClaimsPrincipal Unprotect(string cookieValue) =>
        CookieOptions().TicketDataFormat.Unprotect(cookieValue)!.Principal;

    private async Task RemoveLinkAsync(UserId userId, IdentitySource source, string externalId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserRepository>().RemoveIdentityLinkAsync(userId, source, externalId);
    }

    private async Task<List<DateTimeOffset?>> LinkTimestampsAsync(UserId userId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.IdentityLinks.AsNoTracking()
            .Where(l => l.UserId == userId.Value)
            .OrderBy(l => l.Id)
            .Select(l => l.LastAuthenticatedAt)
            .ToListAsync();
    }

    private async Task<string?> LatestActorAsync(string action)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == action)
            .OrderByDescending(a => a.Id)
            .Select(a => a.Actor)
            .FirstOrDefaultAsync();
    }
}
