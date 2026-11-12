using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Management.Host;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The Now view follows the OpenID Connect handler the running host really holds, against real SQL Server (IAM-10.2b WP1 review,
/// finding 2; A10.2b.16). The host is built with the real registration (<c>AddAppertureAuthentication</c>), the real options
/// post-configure reading the persisted registration from the database, and a configuration source that really reloads. Every case
/// runs on a catalog with the default locking READ COMMITTED and on one with READ_COMMITTED_SNAPSHOT. Fails, never skips,
/// without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class RunningHandlerViabilitySqlServerTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenant = "22222222-2222-2222-2222-222222222222";

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private sealed class ReloadableSource : ConfigurationProvider, IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

        public void Replace(IDictionary<string, string?> values)
        {
            Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
            OnReload();
        }
    }

    /// <summary>A Management Host's authentication wiring over the scratch catalog.</summary>
    private sealed class RunningHost : IDisposable
    {
        private readonly ReloadableSource _source = new();
        private int _reloads;

        public RunningHost(AuditSqlScratch scratch)
        {
            Scratch = scratch;
            Configuration = new ConfigurationBuilder().Add(_source).Build();
            _source.Replace(Values());

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(Configuration);
            services.AddAppertureAuthentication(Configuration);
            services.AddScoped(_ => scratch.NewDb());
            services.AddScoped<IEntraInstanceRegistrationRepository, SqlEntraInstanceRegistrationRepository>();
            services.AddSingleton<IPostConfigureOptions<EntraAuthOptions>, EntraAuthOptionsDatabasePostConfigure>();
            Services = services.BuildServiceProvider();
        }

        public AuditSqlScratch Scratch { get; }
        public IConfigurationRoot Configuration { get; }
        public ServiceProvider Services { get; }

        private IDictionary<string, string?> Values() => new Dictionary<string, string?>
        {
            ["LocalAuth:Enabled"] = "true",
            ["Entra:ClientSecret"] = "deployment-secret",
            ["Reload:Count"] = _reloads.ToString(),
        };

        /// <summary>What the host does at start and on its first request: build the Entra options and the handler's options.</summary>
        public OpenIdConnectOptions Start()
        {
            _ = Services.GetRequiredService<IOptions<EntraAuthOptions>>().Value;
            return Handler();
        }

        public OpenIdConnectOptions Handler() =>
            Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);

        public void ReloadConfiguration()
        {
            _reloads++;
            _source.Replace(Values());
        }

        public void RebuildHandler() => Services.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>().Clear();

        public AdministratorViabilityService Viability(AppertureDbContext db) => new(
            new SqlAdministratorFactsReader(db),
            new EffectiveSignInConfigurationProvider(
                new HostDeploymentAuthenticationConfiguration(
                    Configuration,
                    Services.GetRequiredService<IOptions<LocalAuthOptions>>(),
                    Services.GetRequiredService<IOptions<EntraAuthOptions>>(),
                    Services.GetRequiredService<IRunningEntraHandlerBindingSource>()),
                new SqlEntraInstanceRegistrationRepository(db)));

        public void Dispose() => Services.Dispose();
    }

    /// <summary>
    /// A covered writer's procedure with the gate: take it, read the state, disable the credential, read the state, and commit only
    /// if the guard allows it.
    /// </summary>
    private static async Task<ViabilityVerdict> TryDisableAsync(RunningHost host, string userId)
    {
        await using var db = host.Scratch.NewDb();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await new SqlPlatformAdministratorSetGate(db).AcquireAsync();
            var service = host.Viability(db);
            var pre = await service.ReadAsync();
            await PlatformAdministratorSetGateSqlServerTests.ExecuteAsync(db, $"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{userId}';");
            var post = await service.ReadAsync();
            var verdict = AdministratorViabilityGuard.Evaluate(pre, post);
            await tx.RollbackAsync(); // the test only asks the question; nothing is committed, whatever the verdict
            return verdict;
        });
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_registration_switched_in_the_database_and_a_reload_do_not_move_the_handler_so_now_keeps_following_it(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabHandler", rcsi);
        await AdministratorViabilitySqlServerTests.SeedRegistrationAsync(s, Tenant);
        await AdministratorViabilitySqlServerTests.SeedAsync(s, "local", credential: true);
        await AdministratorViabilitySqlServerTests.SeedAsync(s, "on-a", entraTenant: Tenant);
        await AdministratorViabilitySqlServerTests.SeedAsync(s, "on-b", entraTenant: OtherTenant);

        using var host = new RunningHost(s);
        var started = host.Start();
        started.Authority.Should().Contain(Tenant, "the host started on tenant A");

        // The persisted registration is switched to tenant B and the appsettings are reloaded, without a restart.
        await AdministratorViabilitySqlServerTests.SeedRegistrationAsync(s, OtherTenant);
        host.ReloadConfiguration();

        host.Services.GetRequiredService<IOptionsMonitor<EntraAuthOptions>>().CurrentValue.AzureTenantId.Should().Be(OtherTenant);
        host.Handler().Should().BeSameAs(started, "an Entra reload does not rebuild the handler's options");

        await using var db = s.NewDb();
        var state = await host.Viability(db).ReadAsync();

        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo(
            ["local"], "the handler is on A and the sign-in checks B: no Entra administrator can sign in until the restart");
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["local", "on-b"]);
        state.Configuration.Now.Notes.Should().Contain(n => n.Contains("different directory"));
        state.Trust.EntraKnown.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_shared_guard_refuses_the_reduction_that_leaves_nobody_able_to_sign_in_now_until_the_handler_follows(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabHandlerG", rcsi);
        await AdministratorViabilitySqlServerTests.SeedRegistrationAsync(s, Tenant);
        await AdministratorViabilitySqlServerTests.SeedAsync(s, "local", credential: true);
        await AdministratorViabilitySqlServerTests.SeedAsync(s, "on-b", entraTenant: OtherTenant);

        using var host = new RunningHost(s);
        host.Start();
        await AdministratorViabilitySqlServerTests.SeedRegistrationAsync(s, OtherTenant);
        host.ReloadConfiguration();

        // Disabling "local" would leave, in the running host, nobody who can sign in; the stale Entra monitor would have counted on-b.
        var refused = await TryDisableAsync(host, "local");
        refused.Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator);
        refused.View.Should().Be(ViabilityView.Now);
        (await s.ScalarAsync<int>("SELECT COUNT(*) FROM LocalCredentials WHERE UserId = N'local' AND IsDisabled = 0;"))
            .Should().Be(1, "the refused reduction was rolled back");

        // The handler genuinely reconstructs its configuration: now it signs users in through B, and on-b can.
        host.RebuildHandler();
        host.Handler().Authority.Should().Contain(OtherTenant);
        var afterRebuild = await TryDisableAsync(host, "local");
        afterRebuild.Kind.Should().NotBe(
            ViabilityVerdictKind.LastViableAdministrator,
            "with the handler on B an administrator remains who can sign in now and after a restart");
        afterRebuild.Kind.Should().Be(
            ViabilityVerdictKind.EntraIndependentFallbackRequired,
            "the fallback invariant, which does not depend on the handler, still protects the only Entra-independent administrator");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_host_whose_entra_is_off_holds_placeholders_that_no_viability_view_counts(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabHandlerOff", rcsi);
        await AdministratorViabilitySqlServerTests.SeedAsync(s, "local", credential: true);
        await AdministratorViabilitySqlServerTests.SeedAsync(s, "on-a", entraTenant: Tenant);

        using var host = new RunningHost(s);
        host.Start().ClientId.Should().Be("Apperture-Entra-Disabled-Placeholder");

        await using var db = s.NewDb();
        var state = await host.Viability(db).ReadAsync();

        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["local"]);
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["local"]);
        state.Trust.EntraKnown.Should().BeTrue();
    }
}
