using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Management.Host;
using Apperture.Persistence.SqlServer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// A Management Host's authentication wiring over a scratch catalog, as the process builds it and not yet started: the real
/// <c>AddAppertureAuthentication</c> registration, the real options post-configure reading the persisted registration (and its
/// secret) from the database, and the real reader of the running handler. Nothing is built until something asks for it.
/// </summary>
internal sealed class EntraRunningHost : IDisposable
{
    public const string Tenant = "11111111-1111-1111-1111-111111111111";
    public const string OtherTenant = "22222222-2222-2222-2222-222222222222";
    private const string Admin = BootstrapConstants.PlatformAdminRoleId;

    /// <param name="scratch">The catalog.</param>
    /// <param name="optionsDb">Makes the context the options' own build reads through, so that a case can make that read fail or count it.</param>
    /// <param name="extra">Configuration values beyond the host's usual ones.</param>
    public EntraRunningHost(AuditSqlScratch scratch, Func<AppertureDbContext>? optionsDb = null, IDictionary<string, string?>? extra = null)
    {
        Scratch = scratch;
        var values = new Dictionary<string, string?>
        {
            ["LocalAuth:Enabled"] = "true",
            ["Entra:ClientSecret"] = "deployment-secret",
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
            values[key] = value;
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddAppertureAuthentication(Configuration);
        services.AddScoped(_ => optionsDb is null ? scratch.NewDb() : optionsDb());
        services.AddScoped<IEntraInstanceRegistrationRepository, SqlEntraInstanceRegistrationRepository>();
        services.AddScoped<ISecretResolver>(sp => SecretResolver(sp.GetRequiredService<AppertureDbContext>()));
        services.AddSingleton<IPostConfigureOptions<EntraAuthOptions>, EntraAuthOptionsDatabasePostConfigure>();
        Services = services.BuildServiceProvider();
    }

    public AuditSqlScratch Scratch { get; }

    public IConfigurationRoot Configuration { get; }

    public ServiceProvider Services { get; }

    public IEntraOptionsBuildHealth Health => Services.GetRequiredService<IEntraOptionsBuildHealth>();

    /// <summary>The host's reading of its authentication configuration, over the options this host has (or builds on first use).</summary>
    public HostDeploymentAuthenticationConfiguration Deployment => new(
        Configuration,
        Services.GetRequiredService<IOptions<LocalAuthOptions>>(),
        Services.GetRequiredService<IOptions<EntraAuthOptions>>(),
        Services.GetRequiredService<IRunningEntraHandlerBindingSource>(),
        Health);

    /// <summary>The options the sign-in endpoints read; the first use builds them.</summary>
    public EntraAuthOptions Endpoints => Services.GetRequiredService<IOptions<EntraAuthOptions>>().Value;

    /// <summary>The options the OpenID Connect handler holds; the first use builds them.</summary>
    public OpenIdConnectOptions Handler =>
        Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);

    public void Dispose() => Services.Dispose();

    /// <summary>The SQL secret resolver over <paramref name="db"/> with a protector that stores the plaintext, as the audit suites use.</summary>
    public static ISecretResolver SecretResolver(AppertureDbContext db) =>
        new SqlSecretResolver(new SqlSecretStore(db, new ConnectionSqlHarness.PassThroughProtector(), new DefaultSecretAuditActorProvider()), new ConnectionSqlHarness.PassThroughProtector());

    /// <summary>The viability state as this host would read it, over the persisted registration and the administrators.</summary>
    public async Task<AdministratorViabilityState> StateAsync()
    {
        await using var db = Scratch.NewDb();
        return await new AdministratorViabilityService(
            new SqlAdministratorFactsReader(db),
            new EffectiveSignInConfigurationProvider(Deployment, new SqlEntraInstanceRegistrationRepository(db), SecretResolver(db))).ReadAsync();
    }

    /// <summary>
    /// Clears the administrator state and the registrations of the catalog, then arranges one ordinary administrator who can sign in
    /// only through Microsoft as the one active registration (tenant <see cref="Tenant"/>) provides it, and an actor.
    /// </summary>
    public static async Task<(RoleAuthorityWorld World, string Reliant, string Registration, string Actor)> ArrangeAsync(AuditSqlScratch scratch)
    {
        await scratch.ExecuteAsync($"""
            DELETE FROM UserMfaRecoveryCodes;
            DELETE FROM UserTotpCredentials;
            DELETE FROM UserRoles WHERE RoleId = N'{Admin}';
            UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;
            DELETE FROM EntraRegistrationDiagnostics;
            DELETE FROM EntraInstanceRegistrations;
            """);
        var world = new RoleAuthorityWorld(scratch);
        var reliant = await world.NewUserAsync("reliant", Admin);
        await scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{reliant}';");
        await world.AddEntraLinkAsync(reliant, Tenant, proven: true);
        var actor = await world.NewUserAsync("actor");
        var id = "reg" + Guid.NewGuid().ToString("N")[..16];
        await scratch.ExecuteAsync($"""
            INSERT INTO EntraInstanceRegistrations
                (Id, DisplayName, Authority, ClientId, UsesDeploymentSecret, Enabled, IsActive, CreatedUtc, UpdatedUtc, AzureTenantId, AllowMemberAccessRequests, AllowGuestAccessRequests)
            VALUES (N'{id}', N'Registration', N'https://login.microsoftonline.com/{Tenant}/v2.0', N'client', 1, 1, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{Tenant}', 0, 0);
            """);
        return (world, reliant, id, actor);
    }
}
