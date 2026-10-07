using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Application.Audit;
using Apperture.UnitTests.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Host;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

/// <summary>
/// SEC-1A2b (finding F8): Entra step-up completes on the identity provider's CROSS-SITE <c>form_post</c> callback, where a
/// browser withholds the ordinary SameSite=Lax session cookie. These tests drive the REAL OpenID Connect handler and the REAL
/// <see cref="EntraStepUpService"/> (only the token endpoint, the user store and the audit sink are fakes) and post the callback
/// with the OIDC nonce and correlation cookies and NO session cookie. The step-up continuation must come from the protected
/// OIDC state alone, and the returned Entra identity must be independently proven to be the initiating user's own.
/// </summary>
public sealed class EntraStepUpCallbackTests
{
    private const string UserA = "usr_initiator_aaaa";
    private const string UserB = "usr_someone_else_b";
    private const string ActiveTenant = "5b1c2d3e-4f50-4a61-8b72-9c83d4e5f607";
    private const string OtherTenant = "11111111-2222-4333-8444-555555555555";
    private const string OidA = "3d1b7e42-6a55-4f6e-9c0d-1e2f3a4b5c6d";
    private const string OidB = "9e8d7c6b-5a49-4382-a1b0-c9d8e7f60514";
    private const string ClientId = "test-client-id";
    private const string Issuer = "https://idp.test";
    private const string SessionCookie = ".AspNetCore.Cookies";
    private const string ReturnEscaped = "%2Fconfig%2Fgovernance%2Fusers%2Fusr_initiator_aaaa%3Ftab%3Dsecurity%23roles";
    private const string ReturnDecoded = "/config/governance/users/usr_initiator_aaaa?tab=security#roles";

    // ---- the rig --------------------------------------------------------------------------------------------------------

    private sealed class Rig : IAsyncDisposable
    {
        private readonly RSA _rsa = RSA.Create(2048);
        private readonly RsaSecurityKey _key;
        private int _tokenRequests;

        public WebApplication App { get; private set; } = null!;

        public HttpClient Client { get; private set; } = null!;

        public Mock<IUserRepository> Users { get; } = new();

        public AuditRecording Recording { get; } = new();

        /// <summary>Rows still written through the append bridge (none on these paths after AUD-1.2).</summary>
        public List<AuditRecord> BridgeRows { get; } = new();

        public List<(Apperture.Core.Application.Audit.AuditEvent Event, AuditActor Actor)> Audit => Recording.Events;

        /// <summary>Directory id of the token the fake token endpoint issues.</summary>
        public string TokenTenant { get; set; } = ActiveTenant;

        public string TokenOid { get; set; } = OidA;

        public bool TokenHasAuthenticationContext { get; set; } = true;

        /// <summary>When true a second redemption of the same code is refused, as a real identity provider does.</summary>
        public bool CodeIsSingleUse { get; set; } = true;

        /// <summary>The nonce of the most recent challenge (what the id_token must echo).</summary>
        public string? Nonce { get; private set; }

        public string? LastFailure { get; private set; }

        /// <summary>The initiator's local credential (null = none). Local sign-in must still be available for a local session to be re-issued.</summary>
        public LocalCredentialData? LocalCredential { get; set; } = new(new UserId(UserA), "hash", "salt", "alg", IsDisabled: false);

        public bool LinkAIsProven { get; set; } = true;

        public Dictionary<string, object>? TokenExtraClaims { get; set; }

        /// <summary>Account linking and normal sign-in (their services are fakes; the OpenID Connect handler and the host wiring are real).</summary>
        public Mock<IEntraIdentityAssociationService> Association { get; } = new();

        public Func<AuthenticationProperties, AuthenticationProperties>? MutateStateBeforeItIsSent { get; set; }

        public Rig()
        {
            _key = new RsaSecurityKey(_rsa) { KeyId = "unit-test-key" };
        }

        public async Task StartAsync(string environment = "Production")
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{EntraAuthOptions.SectionName}:Enabled"] = "true",
                [$"{EntraAuthOptions.SectionName}:Authority"] = "https://login.microsoftonline.com/common",
                [$"{EntraAuthOptions.SectionName}:ClientId"] = ClientId,
                [$"{EntraAuthOptions.SectionName}:ClientSecret"] = "test-client-secret",
                [$"{EntraAuthOptions.SectionName}:AuthenticationContextId"] = "c1",
                [$"{LocalAuthOptions.SectionName}:Enabled"] = "true",
                [$"{LocalAuthOptions.SectionName}:MinPasswordLength"] = "8",
            });

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddAppertureAuthentication(builder.Configuration);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IPrincipalContext, ActorFromPrincipal>();
            builder.Services.AddSingleton<IPrincipalToUserResolver, ResolveToInitiator>();

            var registrations = new Mock<IEntraInstanceRegistrationRepository>();
            registrations
                .Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntraInstanceRegistration { Id = "reg1", AzureTenantId = ActiveTenant, Authority = "https://x/" + ActiveTenant, IsActive = true, Enabled = true });
            var audit = new Mock<IAuditService>();
            audit.Setup(a => a.AppendAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
                .Callback<AuditRecord, CancellationToken>((record, _) => BridgeRows.Add(record))
                .Returns(ValueTask.CompletedTask);

            // Users: A is the initiator (a local operator with a proven link to OidA in the active tenant); B is someone else.
            Users.Setup(u => u.GetByIdAsync(new UserId(UserA), It.IsAny<CancellationToken>())).ReturnsAsync(UserRecord(UserA, "Operator A", "opa", OidA));
            Users.Setup(u => u.GetByIdAsync(new UserId(UserB), It.IsAny<CancellationToken>())).ReturnsAsync(UserRecord(UserB, "Operator B", "opb", OidB));
            Users.Setup(u => u.FindIdentityLinkAsync(It.IsAny<ProviderIdentityKey>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProviderIdentityKey key, CancellationToken _) => FindOwner(key));

            Association.Setup(a => a.CompleteLinkAsync(It.IsAny<UserId>(), It.IsAny<EntraIdentityAssertion>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserId id, EntraIdentityAssertion _, CancellationToken _) => EntraAssociationOutcome.Success(id));
            Association.Setup(a => a.CompleteSignInAsync(It.IsAny<EntraIdentityAssertion>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(EntraAssociationOutcome.Success(new UserId(UserA)));
            builder.Services.AddSingleton<IEntraIdentityAssociationService>(Association.Object);
            var credentials = new Mock<ILocalCredentialRepository>();
            credentials.Setup(c => c.GetByUserIdAsync(new UserId(UserA), It.IsAny<CancellationToken>())).ReturnsAsync(() => LocalCredential);
            builder.Services.AddSingleton<ILocalCredentialRepository>(credentials.Object);
            builder.Services.AddSingleton<IUserRepository>(Users.Object);
            builder.Services.AddSingleton<IEntraInstanceRegistrationRepository>(registrations.Object);
            builder.Services.AddSingleton<IAuditService>(audit.Object);
            builder.Services.AddSingleton<IAuditEventWriter>(Recording);
            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddScoped<IEntraStepUpService>(sp => new EntraStepUpService(
                sp.GetRequiredService<IUserRepository>(),
                sp.GetRequiredService<IEntraInstanceRegistrationRepository>(),
                NullLogger<EntraStepUpService>.Instance,
                sp.GetRequiredService<IAuditEventWriter>()));

            builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    AuthorizationEndpoint = Issuer + "/authorize",
                    TokenEndpoint = Issuer + "/token",
                    EndSessionEndpoint = Issuer + "/logout",
                    Issuer = Issuer,
                };
                configuration.SigningKeys.Add(_key);
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.Backchannel = new HttpClient(new FakeTokenEndpoint(this));

                var innerFail = options.Events.OnRemoteFailure;
                options.Events.OnRemoteFailure = async context =>
                {
                    LastFailure = context.Failure?.ToString();
                    await innerFail(context);
                };
                var inner = options.Events.OnRedirectToIdentityProvider;
                options.Events.OnRedirectToIdentityProvider = async context =>
                {
                    if (inner is not null)
                        await inner(context);
                    Nonce = context.ProtocolMessage.Nonce;
                    if (MutateStateBeforeItIsSent is { } mutate)
                        context.ProtocolMessage.State = options.StateDataFormat.Protect(mutate(context.Properties));
                };
            });

            var app = builder.Build();
            app.UseAuthentication();
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Headers.ContainsKey("X-Test-Auth"))
                {
                    var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, UserA));
                    identity.AddClaim(new Claim(ClaimTypes.Name, "Operator A"));
                    var source = ctx.Request.Headers["X-Test-Identity-Source"].ToString();
                    if (!string.IsNullOrWhiteSpace(source))
                        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.IdentitySourceClaimType, source));
                    if (ctx.Request.Headers.ContainsKey("X-Test-Break-Glass"))
                        identity.AddClaim(new Claim(ClaimsPrincipalToActorMapper.BreakGlassClaimType, "1"));
                    ctx.User = new ClaimsPrincipal(identity);
                }

                await next();
            });
            app.UseAuthorization();
            app.MapManagementAuthEndpoints();
            await app.StartAsync();

            App = app;
            var server = app.GetTestServer();
            Client = new HttpClient(server.CreateHandler()) { BaseAddress = server.BaseAddress ?? new Uri("http://localhost") };
        }

        private static User UserRecord(string id, string name, string username, string oid) => new(
            new UserId(id),
            name,
            [
                new IdentityLink(IdentitySource.Local, username, LastAuthenticatedAt: null),
                new IdentityLink(IdentitySource.Entra, oid, DateTimeOffset.UtcNow.AddDays(-1), TenantId: ActiveTenant),
            ],
            [],
            IsBreakGlassEligible: false)
        { Username = username };

        /// <summary>Whether the (tenant, object id) belongs to A or B. Matches on tenant AND object id: never on object id alone.</summary>
        private IdentityLinkOwner? FindOwner(ProviderIdentityKey key)
        {
            if (key.Source != IdentitySource.Entra || !string.Equals(key.TenantId, ActiveTenant, StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.Equals(key.ExternalId, OidA, StringComparison.OrdinalIgnoreCase))
                return new IdentityLinkOwner(new UserId(UserA), new IdentityLink(IdentitySource.Entra, OidA, LinkAIsProven ? DateTimeOffset.UtcNow.AddDays(-1) : null, TenantId: ActiveTenant));
            if (string.Equals(key.ExternalId, OidB, StringComparison.OrdinalIgnoreCase))
                return new IdentityLinkOwner(new UserId(UserB), new IdentityLink(IdentitySource.Entra, OidB, DateTimeOffset.UtcNow.AddDays(-1), TenantId: ActiveTenant));
            return null;
        }

        /// <summary>The initiating request: a signed-in operator (Lax cookie, top-level navigation) starts step-up.</summary>
        public async Task<Challenge> BeginStepUpAsync(string source = "Local", string returnEscaped = ReturnEscaped)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/signin/step-up?returnUrl=" + returnEscaped);
            request.Headers.Add("X-Test-Auth", "1");
            request.Headers.Add("X-Test-Identity-Source", source);
            using var response = await Client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect, "step-up must challenge the identity provider");
            var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
            var cookies = string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
            return new Challenge(query["state"].ToString(), cookies);
        }

        /// <summary>The provider's cross-site <c>form_post</c>: the OIDC flow cookies and the code, and nothing else. No session cookie.</summary>
        public async Task<HttpResponseMessage> PostCallbackAsync(Challenge challenge, string? stateOverride = null, string code = "code-1")
        {
            var form = new List<KeyValuePair<string, string>> { new("state", stateOverride ?? challenge.State), new("code", code) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/signin-oidc") { Content = new FormUrlEncodedContent(form) };
            if (challenge.Cookies.Length > 0)
                request.Headers.Add("Cookie", challenge.Cookies);
            return await Client.SendAsync(request);
        }

        public (string Value, ClaimsPrincipal Principal, AuthenticationProperties Properties)? ReadSessionCookie(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values))
                return null;
            var header = values.FirstOrDefault(v => v.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
            if (header is null)
                return null;
            var value = header.Split(';')[0][(SessionCookie.Length + 1)..];
            if (string.IsNullOrEmpty(value))
                return null;
            var options = App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var ticket = options.TicketDataFormat.Unprotect(value);
            return ticket is null ? null : (value, ticket.Principal, ticket.Properties);
        }

        public string CreateIdToken()
        {
            var claims = new Dictionary<string, object>
            {
                ["oid"] = TokenOid,
                ["tid"] = TokenTenant,
                ["sub"] = "subject-1",
                ["name"] = "Provider Display Name",
                ["nonce"] = Nonce ?? "",
            };
            if (TokenHasAuthenticationContext)
                claims["acrs"] = new[] { "c1" };
            if (TokenExtraClaims is not null)
            {
                foreach (var (k, v) in TokenExtraClaims)
                    claims[k] = v;
            }

            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = ClientId,
                Claims = claims,
                NotBefore = DateTime.UtcNow.AddMinutes(-1),
                Expires = DateTime.UtcNow.AddMinutes(30),
                SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
            });
        }

        public int TokenRequests => Volatile.Read(ref _tokenRequests);

        private sealed class FakeTokenEndpoint(Rig rig) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var n = Interlocked.Increment(ref rig._tokenRequests);
                if (rig.CodeIsSingleUse && n > 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json"),
                    });
                }

                var body = JsonSerializer.Serialize(new { id_token = rig.CreateIdToken(), access_token = "not-a-real-token", token_type = "Bearer", expires_in = 3600 });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            if (App is not null)
                await App.DisposeAsync();
            _rsa.Dispose();
        }
    }

    private sealed record Challenge(string State, string Cookies);

    private sealed class ActorFromPrincipal(IHttpContextAccessor http) : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(http.HttpContext?.User.Identity?.IsAuthenticated == true ? new HumanActor(new ActorId(UserA), "Operator A") : null);
    }

    private sealed class ResolveToInitiator : IPrincipalToUserResolver
    {
        public ValueTask<UserId?> ResolveUserIdAsync(IActor actor, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<UserId?>(new UserId(UserA));
    }

    private static string Location(HttpResponseMessage response) => response.Headers.Location!.ToString();

    private static bool Has(ClaimsPrincipal p, string type, string value) => p.Claims.Any(c => c.Type == type && c.Value == value);

    private static bool ClearsSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
        && values.Any(v => v.StartsWith(SessionCookie + "=;", StringComparison.Ordinal)
            || (v.StartsWith(SessionCookie, StringComparison.Ordinal) && v.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)));

    private void NoIdentityLinkWasWritten(Rig rig)
    {
        rig.Users.Verify(u => u.AddIdentityLinkAsync(It.IsAny<UserId>(), It.IsAny<IdentityLink>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- central acceptance criterion: no session cookie on the callback ----------------------------------------------------

    [Fact]
    public async Task A_local_session_completes_step_up_from_the_protected_state_alone_and_is_reissued_with_the_assurance()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync(source: "Local");

        // The challenge itself must not have touched the session cookie's SameSite (only OIDC flow cookies were set).
        challenge.Cookies.Should().NotContain(SessionCookie + "=");

        var response = await rig.PostCallbackAsync(challenge);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Location(response).Should().Be("http://localhost:9001/config/governance/users/usr_initiator_aaaa?tab=security&steppedUp=1#roles",
            "the return target is preserved (path, query, fragment) with the step-up marker before the fragment");

        var session = rig.ReadSessionCookie(response);
        session.Should().NotBeNull("a session is issued by the callback itself; the browser never sent one");
        var (_, principal, properties) = session!.Value;
        Has(principal, ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local").Should().BeTrue("the session kind that started step-up is the kind that is re-issued");
        Has(principal, ClaimTypes.NameIdentifier, UserA).Should().BeTrue("identity is rebuilt from the INITIATING user's record (its user id), not from any cookie");
        Has(principal, ApplicationSessionClaims.SessionFormatClaimType, ApplicationSessionClaims.CurrentSessionFormat).Should().BeTrue();
        Has(principal, ApplicationSessionClaims.UsernameClaimType, "opa").Should().BeTrue();
        Has(principal, ClaimTypes.Name, "Operator A").Should().BeTrue();
        Has(principal, AuthenticationAssuranceClaims.MechanismClaimType, AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism).Should().BeTrue();
        Has(principal, AuthenticationAssuranceClaims.ContextClaimType, "c1").Should().BeTrue();
        AuthenticationAssuranceClaims.TryGetEstablishedAt(principal).Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        principal.Claims.Should().NotContain(c => c.Type == ClaimsPrincipalToActorMapper.BreakGlassClaimType);
        properties.IsPersistent.Should().BeTrue();
        properties.ExpiresUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.Add(LocalSessionIdentity.Lifetime), TimeSpan.FromMinutes(1));
        rig.Audit.Should().Contain(a => a.Event.Action == StrongAuthenticationAuditActionNames.StepUpSatisfied && a.Actor == AuditActor.AuthenticatedUser(new UserId(UserA)));
        NoIdentityLinkWasWritten(rig);
    }

    [Fact]
    public async Task An_entra_session_completes_step_up_and_is_reissued_as_a_normal_entra_sign_in_without_the_step_up_continuation()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync(source: "Entra");

        var response = await rig.PostCallbackAsync(challenge);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Location(response).Should().Be("http://localhost:9001/config/governance/users/usr_initiator_aaaa?tab=security&steppedUp=1#roles");
        var session = rig.ReadSessionCookie(response);
        session.Should().NotBeNull();
        var (_, principal, properties) = session!.Value;
        Has(principal, ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra").Should().BeTrue();
        principal.FindAll(ClaimTypes.NameIdentifier).Select(c => c.Value).Should().Equal(UserA);
        Has(principal, ApplicationSessionClaims.SessionFormatClaimType, ApplicationSessionClaims.CurrentSessionFormat).Should().BeTrue();
        Has(principal, "oid", OidA).Should().BeTrue("the validated id_token principal (tid + oid proven to be the initiator's) becomes the session");
        Has(principal, "tid", ActiveTenant).Should().BeTrue();
        Has(principal, AuthenticationAssuranceClaims.MechanismClaimType, AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism).Should().BeTrue();
        Has(principal, AuthenticationAssuranceClaims.ContextClaimType, "c1").Should().BeTrue();
        foreach (var key in EntraStepUpFlowConstants.ContinuationItemKeys.Append(EntraLinkFlowConstants.PurposeItemKey))
            properties.Items.Should().NotContainKey(key, "the continuation never outlives the OIDC state it was carried in");
        NoIdentityLinkWasWritten(rig);
    }

    [Fact]
    public async Task The_callback_never_consults_an_ambient_session_a_different_users_cookie_on_the_request_changes_nothing()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync(source: "Local");

        // A browser that DOES send a session cookie on this cross-site POST (e.g. Playwright's WebKit build) must not change
        // whose session is issued: build a genuine session ticket for user B and send it along.
        var options = rig.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var bIdentity = LocalSessionIdentity.Create(new UserId(UserB), "opb", "Operator B");
        var bCookie = options.TicketDataFormat.Protect(new AuthenticationTicket(new ClaimsPrincipal(bIdentity), new AuthenticationProperties(), CookieAuthenticationDefaults.AuthenticationScheme));
        var withB = new Challenge(challenge.State, challenge.Cookies + "; " + SessionCookie + "=" + bCookie);

        var response = await rig.PostCallbackAsync(withB);

        var (_, principal, _) = rig.ReadSessionCookie(response)!.Value;
        Has(principal, ClaimTypes.NameIdentifier, UserA).Should().BeTrue("the initiating user comes from the protected state");
        Has(principal, ClaimTypes.NameIdentifier, UserB).Should().BeFalse("nothing is merged from an ambient cookie");
    }

    // ---- binding the returned identity to the initiating user ---------------------------------------------------------------

    [Fact]
    public async Task An_identity_that_belongs_to_a_different_apperture_user_is_refused_with_no_session_and_no_link_change()
    {
        await using var rig = new Rig { TokenOid = OidB };
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync();

        var response = await rig.PostCallbackAsync(challenge);

        Location(response).Should().Be("http://localhost:9001/config/governance/users/usr_initiator_aaaa?tab=security&error=identity_mismatch#roles");
        rig.ReadSessionCookie(response).Should().BeNull("no step-up grant, no session");
        rig.Audit.Should().Contain(a => a.Event.Action == StrongAuthenticationAuditActionNames.StepUpIdentityMismatch && a.Actor == AuditActor.AuthenticatedUser(new UserId(UserA)));
        rig.Audit.Should().NotContain(a => a.Event.Action == StrongAuthenticationAuditActionNames.StepUpSatisfied);
        NoIdentityLinkWasWritten(rig);
    }

    [Fact]
    public async Task The_same_object_id_in_another_tenant_is_refused_by_the_tenant_aware_identity_rules()
    {
        await using var rig = new Rig { TokenTenant = OtherTenant };
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync();

        var response = await rig.PostCallbackAsync(challenge);

        Location(response).Should().Contain("error=tenant_mismatch");
        rig.ReadSessionCookie(response).Should().BeNull();
        rig.Audit.Should().Contain(a => a.Event.Action == StrongAuthenticationAuditActionNames.StepUpTenantMismatch);
        NoIdentityLinkWasWritten(rig);
    }

    [Fact]
    public async Task An_entra_identity_with_no_link_is_refused_and_is_never_provisioned_or_linked()
    {
        await using var rig = new Rig { TokenOid = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee" };
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync();

        var response = await rig.PostCallbackAsync(challenge);

        Location(response).Should().Contain("error=identity_mismatch");
        rig.ReadSessionCookie(response).Should().BeNull();
        NoIdentityLinkWasWritten(rig);
    }

    [Fact]
    public async Task A_token_without_the_requested_authentication_context_is_refused_with_no_session()
    {
        await using var rig = new Rig { TokenHasAuthenticationContext = false };
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync();

        var response = await rig.PostCallbackAsync(challenge);

        Location(response).Should().Contain("error=authentication_context_not_satisfied");
        rig.ReadSessionCookie(response).Should().BeNull();
        rig.Audit.Should().Contain(a => a.Event.Action == StrongAuthenticationAuditActionNames.StepUpContextNotSatisfied);
    }

    [Fact]
    public async Task An_initiating_user_who_no_longer_exists_is_refused_with_no_session()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        rig.Users.Setup(u => u.GetByIdAsync(new UserId(UserA), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var challenge = await rig.BeginStepUpAsync();

        var response = await rig.PostCallbackAsync(challenge);

        Location(response).Should().Contain("error=user_not_found");
        rig.ReadSessionCookie(response).Should().BeNull();
    }

    // ---- continuation: missing, tampered, stale, replayed --------------------------------------------------------------------

    [Theory]
    [InlineData("apperture.auth.userId")]
    [InlineData("apperture.auth.requestedAuthenticationContext")]
    [InlineData("apperture.auth.sessionSource")]
    public async Task A_step_up_state_missing_any_part_of_the_continuation_is_refused_with_no_grant(string missingKey)
    {
        await using var rig = new Rig();
        rig.MutateStateBeforeItIsSent = props =>
        {
            props.Items.Remove(missingKey);
            return props;
        };
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync();

        var response = await rig.PostCallbackAsync(challenge);

        Location(response).Should().Contain("error=continuation_invalid");
        rig.ReadSessionCookie(response).Should().BeNull("a valid OIDC principal with no valid continuation earns nothing");
        rig.Audit.Should().NotContain(a => a.Event.Action == StrongAuthenticationAuditActionNames.StepUpSatisfied);
        NoIdentityLinkWasWritten(rig);
    }

    [Fact]
    public async Task A_session_kind_the_callback_does_not_recognise_is_refused()
    {
        await using var rig = new Rig();
        rig.MutateStateBeforeItIsSent = props =>
        {
            props.Items["apperture.auth.sessionSource"] = "BreakGlass";
            return props;
        };
        await rig.StartAsync();

        var response = await rig.PostCallbackAsync(await rig.BeginStepUpAsync());

        Location(response).Should().Contain("error=continuation_invalid");
        rig.ReadSessionCookie(response).Should().BeNull();
    }

    [Fact]
    public async Task A_tampered_state_fails_protection_and_is_refused_with_no_grant()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync();
        var tampered = challenge.State[..^4] + (challenge.State.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var response = await rig.PostCallbackAsync(challenge, stateOverride: tampered);

        Location(response).Should().Be("http://localhost:9001/login?error=oidc", "an unvalidated callback is an ordinary sign-in failure, never a step-up");
        rig.ReadSessionCookie(response).Should().BeNull();
        rig.TokenRequests.Should().Be(0, "the code is never redeemed for an unverifiable state");

        // AUD-1.2: recorded once, as an anonymous attempt: nothing about the caller was validated.
        var (failed, actor) = rig.Audit.Should().ContainSingle(a => a.Event.Action == AuditActionNames.EntraSignInFailed).Subject;
        actor.Should().Be(AuditActor.Anonymous);
        failed.Target.Should().BeNull();
        failed.Fields["flow"].Should().Be("unknown", "the state did not validate, so its purpose is unknown");
    }

    [Fact]
    public async Task A_state_from_another_browser_without_this_browsers_correlation_cookie_is_refused()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync();

        var response = await rig.PostCallbackAsync(new Challenge(challenge.State, Cookies: ""));

        Location(response).Should().Contain("error=oidc");
        rig.ReadSessionCookie(response).Should().BeNull();
        rig.TokenRequests.Should().Be(0);
    }

    [Fact]
    public async Task A_stale_state_is_refused_with_no_grant()
    {
        await using var rig = new Rig();
        rig.MutateStateBeforeItIsSent = props =>
        {
            props.IssuedUtc = DateTimeOffset.UtcNow.AddHours(-3);
            props.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(-2);
            return props;
        };
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync();

        var response = await rig.PostCallbackAsync(challenge);

        Location(response).Should().Contain("error=continuation_invalid", "the freshness rule, not incidental handler behaviour, refuses it");
        Location(response).Should().NotContain("steppedUp=1");
        rig.Audit.Should().Contain(a => a.Event.Fields.ContainsKey("reason") && (string?)a.Event.Fields["reason"] == "continuation_stale");
        rig.ReadSessionCookie(response).Should().BeNull("an expired state cannot mint a step-up");
        rig.Audit.Should().NotContain(a => a.Event.Action == StrongAuthenticationAuditActionNames.StepUpSatisfied);
    }

    [Fact]
    public async Task A_local_session_is_not_reissued_when_the_local_credential_was_disabled_while_the_operator_was_at_microsoft()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync(source: "Local");
        rig.LocalCredential = rig.LocalCredential! with { IsDisabled = true };

        var response = await rig.PostCallbackAsync(challenge);

        Location(response).Should().Contain("error=local_session_unavailable");
        rig.ReadSessionCookie(response).Should().BeNull("local sign-in would refuse this account now, so no fresh local session is minted");
    }

    [Fact]
    public async Task A_local_session_is_not_reissued_for_an_account_with_no_local_credential()
    {
        await using var rig = new Rig { LocalCredential = null };
        await rig.StartAsync();

        var response = await rig.PostCallbackAsync(await rig.BeginStepUpAsync(source: "Local"));

        Location(response).Should().Contain("error=local_session_unavailable");
        rig.ReadSessionCookie(response).Should().BeNull();
    }

    [Fact]
    public async Task An_unproven_entra_link_is_refused_with_no_session()
    {
        await using var rig = new Rig { LinkAIsProven = false };
        await rig.StartAsync();

        var response = await rig.PostCallbackAsync(await rig.BeginStepUpAsync());

        Location(response).Should().Contain("error=unproven");
        rig.ReadSessionCookie(response).Should().BeNull();
    }

    [Theory]
    [InlineData("different-user")]
    [InlineData("other-tenant")]
    [InlineData("no-link")]
    [InlineData("no-context")]
    public async Task Refusals_hold_for_an_entra_session_kind_too_with_no_reissued_session(string kind)
    {
        await using var rig = new Rig
        {
            TokenOid = kind == "different-user" ? OidB : kind == "no-link" ? "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee" : OidA,
            TokenTenant = kind == "other-tenant" ? OtherTenant : ActiveTenant,
            TokenHasAuthenticationContext = kind != "no-context",
        };
        await rig.StartAsync();

        var response = await rig.PostCallbackAsync(await rig.BeginStepUpAsync(source: "Entra"));

        Location(response).Should().Contain("error=");
        Location(response).Should().NotContain("steppedUp=1");
        rig.ReadSessionCookie(response).Should().BeNull("the framework must not sign in a principal whose step-up was refused");
        NoIdentityLinkWasWritten(rig);
    }

    [Fact]
    public async Task A_normal_entra_sign_in_drops_every_externally_supplied_apperture_authority_claim()
    {
        await using var rig = new Rig { TokenExtraClaims = HostileAppertureAuthorityClaims() };
        await rig.StartAsync();
        using var challengeResponse = await rig.Client.GetAsync("/signin");
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var response = await rig.PostCallbackAsync(new Challenge(state, cookies));

        var (_, principal, _) = rig.ReadSessionCookie(response)!.Value;
        AssertNoExternalAppertureAuthority(principal, expectedIdentitySource: "Entra");
        Has(principal, "oid", OidA).Should().BeTrue("identity proof claims are not Apperture authority");
        Has(principal, "tid", ActiveTenant).Should().BeTrue();
        EntraAuthenticationContextEvidence.Satisfies(principal, "c1").Should().BeTrue("acrs is identity-provider evidence, not an Apperture claim");
    }

    [Fact]
    public async Task An_entra_session_step_up_drops_every_externally_supplied_apperture_authority_claim()
    {
        await using var rig = new Rig { TokenExtraClaims = HostileAppertureAuthorityClaims() };
        await rig.StartAsync();

        var response = await rig.PostCallbackAsync(await rig.BeginStepUpAsync(source: "Entra"));

        var (_, principal, _) = rig.ReadSessionCookie(response)!.Value;
        AssertNoExternalAppertureAuthority(principal, expectedIdentitySource: "Entra");
        Has(principal, "oid", OidA).Should().BeTrue();
        Has(principal, "tid", ActiveTenant).Should().BeTrue();
        EntraAuthenticationContextEvidence.Satisfies(principal, "c1").Should().BeTrue();
        Has(principal, AuthenticationAssuranceClaims.MechanismClaimType, AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism)
            .Should().BeTrue("host-issued assurance is added after the external claims are removed");
    }

    /// <summary>
    /// Authority a hostile token (a tenant claims-mapping policy) might try to assert: Apperture-namespace claims,
    /// including the session format and username, and the session identity itself (another user's id).
    /// </summary>
    private static Dictionary<string, object> HostileAppertureAuthorityClaims() => new()
    {
        [ClaimsPrincipalToActorMapper.BreakGlassClaimType] = "1",
        [ClaimsPrincipalToActorMapper.IdentitySourceClaimType] = "Local",
        [ClaimsPrincipalToActorMapper.ApplicationClaimNamespace + "custom_authority"] = "platform-administrator",
        [ApplicationSessionClaims.SessionFormatClaimType] = "9",
        [ApplicationSessionClaims.UsernameClaimType] = "admin",
        [ClaimTypes.NameIdentifier] = UserB,
    };

    /// <summary>The Apperture-namespace claims this host itself issues on every application session.</summary>
    private static bool IsHostIssuedSessionClaim(string type) =>
        type is ClaimsPrincipalToActorMapper.IdentitySourceClaimType
            or ApplicationSessionClaims.SessionFormatClaimType
            or ApplicationSessionClaims.UsernameClaimType;

    private static void AssertNoExternalAppertureAuthority(ClaimsPrincipal principal, string expectedIdentitySource)
    {
        principal.Claims.Should().NotContain(
            c => c.Type.StartsWith(ClaimsPrincipalToActorMapper.ApplicationClaimNamespace, StringComparison.Ordinal)
                 && !IsHostIssuedSessionClaim(c.Type),
            "an external token must not assert any Apperture-namespace claim");
        principal.FindAll(ClaimsPrincipalToActorMapper.IdentitySourceClaimType).Select(c => c.Value).Should().Equal(expectedIdentitySource);
        AssertHostIssuedSessionIdentity(principal);
        principal.Claims.Should().NotContain(c => c.Type == ClaimsPrincipalToActorMapper.BreakGlassClaimType);
        var actor = ClaimsPrincipalToActorMapper.ToActor(principal);
        actor.Should().BeOfType<HumanActor>().Which.IsBreakGlassSession.Should().BeFalse();
        ((HumanActor)actor!).PreferredIdentitySource.Should().Be(IdentitySource.Entra);
        actor!.Id.Value.Should().Be(UserA, "the actor is the resolved user's id, never a token-supplied one");
    }

    /// <summary>Exactly one copy of each session-identity claim, with the host's values for user A.</summary>
    private static void AssertHostIssuedSessionIdentity(ClaimsPrincipal principal)
    {
        principal.FindAll(ClaimTypes.NameIdentifier).Select(c => c.Value).Should().Equal(UserA);
        principal.FindAll(ApplicationSessionClaims.SessionFormatClaimType).Select(c => c.Value).Should().Equal(ApplicationSessionClaims.CurrentSessionFormat);
        principal.FindAll(ApplicationSessionClaims.UsernameClaimType).Select(c => c.Value).Should().Equal("opa");
    }

    [Fact]
    public async Task A_token_carrying_assurance_claims_never_gets_them_on_a_normal_sign_in()
    {
        await using var rig = new Rig { TokenExtraClaims = new Dictionary<string, object> { [AuthenticationAssuranceClaims.MechanismClaimType] = "native-totp", [AuthenticationAssuranceClaims.EstablishedAtClaimType] = DateTimeOffset.UtcNow.ToString("o") } };
        await rig.StartAsync();
        using var challengeResponse = await rig.Client.GetAsync("/signin");
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var response = await rig.PostCallbackAsync(new Challenge(state, cookies));

        var (_, principal, _) = rig.ReadSessionCookie(response)!.Value;
        principal.Claims.Should().NotContain(c => c.Type == AuthenticationAssuranceClaims.MechanismClaimType, "assurance is issued by this host only");
    }

    [Fact]
    public async Task A_normal_entra_sign_in_drops_every_token_supplied_apperture_namespace_claim_before_adding_trusted_ones()
    {
        const string hostileAuthority = "http://schemas.apperture/custom_authority";
        await using var rig = new Rig
        {
            TokenExtraClaims = new Dictionary<string, object>
            {
                [ClaimsPrincipalToActorMapper.BreakGlassClaimType] = "1",
                [ClaimsPrincipalToActorMapper.IdentitySourceClaimType] = "Local",
                [hostileAuthority] = "platform-admin",
            },
        };
        await rig.StartAsync();
        using var challengeResponse = await rig.Client.GetAsync("/signin");
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var response = await rig.PostCallbackAsync(new Challenge(state, cookies));

        var (_, principal, _) = rig.ReadSessionCookie(response)!.Value;
        principal.Claims.Should().NotContain(c => c.Type.StartsWith("http://schemas.apperture/", StringComparison.OrdinalIgnoreCase) && !IsHostIssuedSessionClaim(c.Type));
        Has(principal, ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra").Should().BeTrue("Apperture adds the trusted Entra source after stripping the token");
        Has(principal, ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local").Should().BeFalse();
        Has(principal, "oid", OidA).Should().BeTrue();
        Has(principal, "tid", ActiveTenant).Should().BeTrue();
        var actor = ClaimsPrincipalToActorMapper.ToActor(principal);
        actor.Should().BeOfType<HumanActor>().Which.IsBreakGlassSession.Should().BeFalse();
        ((HumanActor)actor!).PreferredIdentitySource.Should().Be(IdentitySource.Entra);
    }

    [Fact]
    public async Task A_normal_entra_sign_in_issues_the_user_id_session_and_ignores_a_token_supplied_identity()
    {
        await using var rig = new Rig { TokenExtraClaims = HostileAppertureAuthorityClaims() };
        await rig.StartAsync();
        using var challengeResponse = await rig.Client.GetAsync("/signin");
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var response = await rig.PostCallbackAsync(new Challenge(state, cookies));

        var (_, principal, _) = rig.ReadSessionCookie(response)!.Value;
        AssertNoExternalAppertureAuthority(principal, expectedIdentitySource: "Entra");
        Has(principal, ClaimTypes.Name, "Operator A").Should().BeTrue("the session's name is the user's display name");
        Has(principal, "oid", OidA).Should().BeTrue("the raw directory ids stay as link provenance");
        Has(principal, "tid", ActiveTenant).Should().BeTrue();
        ApplicationSessionClaims.IsCurrentFormat(principal).Should().BeTrue();
        rig.Audit.Should().Contain(a => a.Event.Action == "auth.entra.login.success" && a.Actor == AuditActor.AuthenticatedUser(new UserId(UserA)),
            "the sign-in is attributed to the user id, not the directory object id");
    }

    [Fact]
    public async Task An_entra_sign_in_whose_row_cannot_be_written_issues_no_session_and_returns_once_to_the_login_page()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        rig.Recording.FailWrites = true;
        using var challengeResponse = await rig.Client.GetAsync("/signin");
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var response = await rig.PostCallbackAsync(new Challenge(state, cookies));

        rig.ReadSessionCookie(response).Should().BeNull("AUD-1.2: the W2 row commits before the session, so its failure withholds it");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/login").And.Contain("error=");
    }

    [Fact]
    public async Task An_entra_session_step_up_drops_token_supplied_apperture_namespace_claims_and_keeps_the_trusted_entra_source()
    {
        const string hostileAuthority = "http://schemas.apperture/custom_authority";
        await using var rig = new Rig
        {
            TokenExtraClaims = new Dictionary<string, object>
            {
                [ClaimsPrincipalToActorMapper.BreakGlassClaimType] = "1",
                [ClaimsPrincipalToActorMapper.IdentitySourceClaimType] = "Local",
                [hostileAuthority] = "platform-admin",
            },
        };
        await rig.StartAsync();

        var response = await rig.PostCallbackAsync(await rig.BeginStepUpAsync(source: "Entra"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var (_, principal, _) = rig.ReadSessionCookie(response)!.Value;
        principal.Claims.Should().NotContain(c => c.Type.StartsWith("http://schemas.apperture/", StringComparison.OrdinalIgnoreCase) && !IsHostIssuedSessionClaim(c.Type));
        Has(principal, ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra").Should().BeTrue();
        Has(principal, ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Local").Should().BeFalse();
        Has(principal, "oid", OidA).Should().BeTrue();
        Has(principal, "tid", ActiveTenant).Should().BeTrue();
        EntraAuthenticationContextEvidence.Satisfies(principal, "c1").Should().BeTrue("the identity provider's authentication-context evidence is not an Apperture claim");
        Has(principal, AuthenticationAssuranceClaims.MechanismClaimType, AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism).Should().BeTrue("host-issued assurance is added after the token namespace is stripped");
        var actor = ClaimsPrincipalToActorMapper.ToActor(principal);
        actor.Should().BeOfType<HumanActor>().Which.IsBreakGlassSession.Should().BeFalse();
        ((HumanActor)actor!).PreferredIdentitySource.Should().Be(IdentitySource.Entra);
    }

    [Fact]
    public async Task A_state_that_carries_no_expiry_is_not_a_step_up_this_build_issued_and_is_refused()
    {
        await using var rig = new Rig();
        rig.MutateStateBeforeItIsSent = props =>
        {
            props.ExpiresUtc = null;
            return props;
        };
        await rig.StartAsync();

        var response = await rig.PostCallbackAsync(await rig.BeginStepUpAsync());

        Location(response).Should().Contain("error=continuation_invalid");
        rig.ReadSessionCookie(response).Should().BeNull();
    }

    [Fact]
    public async Task The_state_lifetime_is_bounded_and_never_becomes_the_session_lifetime()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var response = await rig.PostCallbackAsync(await rig.BeginStepUpAsync(source: "Entra"));

        var (_, _, properties) = rig.ReadSessionCookie(response)!.Value;
        properties.ExpiresUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(12), TimeSpan.FromMinutes(2), "the 15-minute state expiry must not leak into the Entra session ticket; the session keeps the normal 12 h lifetime");
        EntraStepUpFlowConstants.StateLifetime.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task A_replayed_callback_does_not_grant_a_second_time()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync();

        var first = await rig.PostCallbackAsync(challenge);
        first.StatusCode.Should().Be(HttpStatusCode.Redirect);
        rig.ReadSessionCookie(first).Should().NotBeNull();

        // A browser deletes the correlation and nonce cookies when the callback validates (they are Set-Cookie'd away on the
        // first response), so a real replay carries none of them: refused by the OIDC handler itself.
        var replayFromBrowser = await rig.PostCallbackAsync(new Challenge(challenge.State, Cookies: ""));
        Location(replayFromBrowser).Should().Contain("error=oidc");
        rig.ReadSessionCookie(replayFromBrowser).Should().BeNull();

        // An attacker who kept the cookies and the state still needs the identity provider to redeem the authorization code
        // again, which a real provider refuses (the fake token endpoint models that). The framework adds no replay store of its own.
        var replayWithCookies = await rig.PostCallbackAsync(challenge);
        Location(replayWithCookies).Should().Contain("error=oidc");
        rig.ReadSessionCookie(replayWithCookies).Should().BeNull();
        rig.Audit.Count(a => a.Event.Action == StrongAuthenticationAuditActionNames.StepUpSatisfied).Should().Be(1);
    }

    // ---- regressions: the nearby flows must be untouched --------------------------------------------------------------------

    [Fact]
    public async Task Account_linking_still_completes_from_its_protected_state_with_no_session_cookie_and_returns_to_account_security()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/signin/link");
        request.Headers.Add("X-Test-Auth", "1");
        request.Headers.Add("X-Test-Identity-Source", "Local");
        using var challengeResponse = await rig.Client.SendAsync(request);
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var response = await rig.PostCallbackAsync(new Challenge(state, cookies));

        Location(response).Should().Be("http://localhost:9001/account?linked=1");
        rig.Association.Verify(a => a.CompleteLinkAsync(new UserId(UserA), It.Is<EntraIdentityAssertion>(x => x.ObjectId == OidA && x.TenantId == ActiveTenant), It.IsAny<CancellationToken>()), Times.Once);
        rig.Association.Verify(a => a.CompleteSignInAsync(It.IsAny<EntraIdentityAssertion>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Audit.Should().NotContain(a => a.Event.Action == StrongAuthenticationAuditActionNames.StepUpSatisfied, "linking is not a step-up and grants no assurance");
        var session = rig.ReadSessionCookie(response);
        session.Should().NotBeNull();
        Has(session!.Value.Principal, AuthenticationAssuranceClaims.MechanismClaimType, AuthenticationAssuranceClaims.EntraAuthenticationContextMechanism).Should().BeFalse();
    }

    [Fact]
    public async Task Normal_sign_in_keeps_a_deep_link_with_query_and_fragment_and_issues_an_entra_session_without_step_up_assurance()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        using var challengeResponse = await rig.Client.GetAsync("/signin?returnUrl=" + ReturnEscaped);
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var response = await rig.PostCallbackAsync(new Challenge(state, cookies));

        Location(response).Should().Be("http://localhost:9001" + ReturnDecoded);
        rig.Association.Verify(a => a.CompleteSignInAsync(It.IsAny<EntraIdentityAssertion>(), It.IsAny<CancellationToken>()), Times.Once);
        var (_, principal, _) = rig.ReadSessionCookie(response)!.Value;
        Has(principal, ClaimsPrincipalToActorMapper.IdentitySourceClaimType, "Entra").Should().BeTrue();
        principal.Claims.Should().NotContain(c => c.Type == AuthenticationAssuranceClaims.MechanismClaimType, "a normal sign-in is not a step-up");
    }

    [Fact]
    public async Task A_real_entra_session_is_not_cleared_by_the_historical_front_channel_path()
    {
        const string sessionId = "idp-session-1";
        await using var rig = new Rig
        {
            TokenExtraClaims = new Dictionary<string, object> { ["sid"] = sessionId },
        };
        await rig.StartAsync();
        var options = rig.App.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        options.MapInboundClaims.Should().BeFalse("the host configuration keeps the token's own claim names");
        options.RemoteSignOutPath.HasValue.Should().BeFalse("front-channel logout is disabled on the live options");

        using var challengeResponse = await rig.Client.GetAsync("/signin?returnUrl=" + ReturnEscaped);
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var response = await rig.PostCallbackAsync(new Challenge(state, cookies));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var (value, principal, _) = rig.ReadSessionCookie(response)!.Value;
        Has(principal, "sid", sessionId).Should().BeTrue("the id_token sid survives claim actions under its raw name");
        Has(principal, "iss", Issuer).Should().BeFalse("ASP.NET Core ClaimActions delete iss before the cookie ticket is written");
        principal.HasClaim(c => c.Type == ClaimTypes.Sid).Should().BeFalse("MapInboundClaims is false, so sid is not rewritten to the SOAP claim type");

        var sessionCookie = SessionCookie + "=" + value;
        var query = "?sid=" + Uri.EscapeDataString(sessionId) + "&iss=" + Uri.EscapeDataString(Issuer);
        var body = "sid=" + sessionId + "&iss=" + Issuer;

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/signout-oidc" + query);
        getRequest.Headers.Add("Cookie", sessionCookie);
        using var getResponse = await rig.Client.SendAsync(getRequest);
        ClearsSessionCookie(getResponse).Should().BeFalse("GET /signout-oidc is not a live RemoteSignOutPath");

        using var formRequest = new HttpRequestMessage(HttpMethod.Post, "/signout-oidc")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        formRequest.Headers.Add("Cookie", sessionCookie);
        using var formResponse = await rig.Client.SendAsync(formRequest);
        ClearsSessionCookie(formResponse).Should().BeFalse("a form POST to /signout-oidc is not a live RemoteSignOutPath");
    }

    [Theory]
    [InlineData("%2F%5Cevil.example")]
    [InlineData("%2F%09%2Fevil.example")]
    [InlineData("https%3A%2F%2Fevil.example")]
    public async Task Normal_sign_in_with_a_refused_return_target_falls_back_to_the_default_route(string escaped)
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        using var challengeResponse = await rig.Client.GetAsync("/signin?returnUrl=" + escaped);
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var response = await rig.PostCallbackAsync(new Challenge(state, cookies));

        Location(response).Should().Be("http://localhost:9001/control/center");
    }

    [Fact]
    public async Task A_cancelled_normal_sign_in_still_returns_to_the_stable_login_error()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        using var challengeResponse = await rig.Client.GetAsync("/signin");
        var state = QueryHelpers.ParseQuery(challengeResponse.Headers.Location!.Query)["state"].ToString();
        var cookies = string.Join("; ", challengeResponse.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/signin-oidc")
        {
            Content = new FormUrlEncodedContent([new("state", state), new("error", "access_denied")]),
        };
        request.Headers.Add("Cookie", cookies);

        using var response = await rig.Client.SendAsync(request);

        Location(response).Should().Be("http://localhost:9001/login?error=cancelled");
    }

    // ---- initiation ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Initiation_records_only_the_minimum_continuation_in_the_protected_state_and_no_secret_or_session_content()
    {
        await using var rig = new Rig();
        AuthenticationProperties? seen = null;
        rig.MutateStateBeforeItIsSent = props =>
        {
            seen = new AuthenticationProperties(new Dictionary<string, string?>(props.Items));
            return props;
        };
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync(source: "Local");

        seen.Should().NotBeNull();
        var keys = seen!.Items.Keys.Where(k => k!.StartsWith("apperture.", StringComparison.Ordinal)).OrderBy(k => k).ToArray();
        keys.Should().BeEquivalentTo(
        [
            "apperture.auth.purpose",
            "apperture.auth.requestedAuthenticationContext",
            "apperture.auth.returnPath",
            "apperture.auth.sessionSource",
            "apperture.auth.userId",
        ]);
        seen.Items["apperture.auth.userId"].Should().Be(UserA);
        seen.Items["apperture.auth.sessionSource"].Should().Be("Local");
        seen.Items["apperture.auth.returnPath"].Should().Be(ReturnDecoded);
        var everything = string.Join('|', seen.Items.Select(kv => kv.Key + "=" + kv.Value));
        everything.Should().NotContainAny("password", "secret", "access_token", "id_token", "refresh_token", ".AspNetCore.Cookies");
        challenge.State.Should().NotContain(UserA, "the state is protected, not merely encoded");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SomethingElse")]
    public async Task Step_up_is_not_started_from_a_session_of_an_unrecognised_kind(string? source)
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/signin/step-up?returnUrl=" + ReturnEscaped);
        request.Headers.Add("X-Test-Auth", "1");
        if (source is not null)
            request.Headers.Add("X-Test-Identity-Source", source);

        using var response = await rig.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Step_up_is_not_started_from_an_emergency_break_glass_session()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/signin/step-up");
        request.Headers.Add("X-Test-Auth", "1");
        request.Headers.Add("X-Test-Identity-Source", "Local");
        request.Headers.Add("X-Test-Break-Glass", "1");

        using var response = await rig.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("%2F%5Cevil.example")]
    [InlineData("%2Flogin%3FreturnUrl%3D%252F%255Cevil.example")]
    [InlineData("https%3A%2F%2Fevil.example")]
    public async Task A_hostile_return_target_falls_back_on_the_step_up_leg_and_the_return_is_the_shared_policys(string escaped)
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var challenge = await rig.BeginStepUpAsync(returnEscaped: escaped);

        var response = await rig.PostCallbackAsync(challenge);

        Location(response).Should().Be("http://localhost:9001/account?steppedUp=1");
    }
}
