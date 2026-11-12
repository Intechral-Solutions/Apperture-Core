using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The Now view follows the OpenID Connect handler the running host actually holds (IAM-10.2b WP1 review, finding 2). Every case
/// uses the real host registration (<c>AddAppertureAuthentication</c>), the real options post-configure that overlays the active
/// registration, a configuration source that really reloads, and the real named-options cache the authentication handler is
/// given its options from. Nothing here compares two copies of the provider's own logic.
/// </summary>
public sealed class RunningHandlerBindingTests
{
    private static string Authority(string tenant) => "https://login.microsoftonline.com/" + tenant;

    /// <summary>A configuration source whose values can be replaced and reloaded, as an appsettings file edit does.</summary>
    private sealed class ReloadableSource : ConfigurationProvider, IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

        public void Replace(IDictionary<string, string?> values)
        {
            Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
            OnReload();
        }
    }

    private sealed class MutableRegistrations
    {
        public EntraInstanceRegistration? Active { get; set; }

        public IEntraInstanceRegistrationRepository Repository()
        {
            var repo = new Mock<IEntraInstanceRegistrationRepository>();
            repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Active);
            return repo.Object;
        }
    }

    private sealed class Host : IDisposable
    {
        private readonly ReloadableSource _source = new();
        public MutableRegistrations Registrations { get; } = new();
        public ServiceProvider Services { get; }
        public IConfigurationRoot Configuration { get; }
        public int Reloads { get; private set; }

        public Host(EntraInstanceRegistration? active, bool throwWhenHandlerOptionsAreBuilt = false)
        {
            Registrations.Active = active;
            Configuration = new ConfigurationBuilder().Add(_source).Build();
            _source.Replace(Values());

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(Configuration);
            services.AddAppertureAuthentication(Configuration);
            services.AddSingleton<IPostConfigureOptions<EntraAuthOptions>, EntraAuthOptionsDatabasePostConfigure>();
            services.AddSingleton(Registrations.Repository());
            if (throwWhenHandlerOptionsAreBuilt)
            {
                services.PostConfigure<OpenIdConnectOptions>(
                    OpenIdConnectDefaults.AuthenticationScheme,
                    _ => throw new InvalidOperationException("the secret hunter2 must not be repeated"));
            }

            Services = services.BuildServiceProvider();
        }

        private IDictionary<string, string?> Values() => new Dictionary<string, string?>
        {
            ["LocalAuth:Enabled"] = "true",
            ["Entra:ClientSecret"] = "deployment-secret",
            ["Reload:Count"] = Reloads.ToString(),
        };

        /// <summary>An edit of the appsettings: the Entra options are rebuilt, the OpenID Connect options are not asked to be.</summary>
        public void ReloadConfiguration()
        {
            Reloads++;
            _source.Replace(Values());
        }

        public IOptionsMonitor<OpenIdConnectOptions> HandlerOptions => Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();

        /// <summary>What the authentication middleware does on every request: ask for the handler's named options.</summary>
        public OpenIdConnectOptions Handler() => HandlerOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);

        public EffectiveSignInConfigurationProvider Provider() => new(
            new HostDeploymentAuthenticationConfiguration(
                Configuration,
                Services.GetRequiredService<IOptions<LocalAuthOptions>>(),
                Services.GetRequiredService<IOptions<EntraAuthOptions>>(),
                Services.GetRequiredService<IRunningEntraHandlerBindingSource>()),
            Registrations.Repository());

        public void Dispose() => Services.Dispose();
    }

    private static EntraInstanceRegistration Registration(string tenant) => new()
    {
        Id = "reg-" + tenant[..4],
        Enabled = true,
        IsActive = true,
        Authority = Authority(tenant),
        ClientId = "client",
        UsesDeploymentSecret = true,
        AzureTenantId = tenant,
    };

    private static AdministratorFacts EntraOnly(string id, string tenant) => Admin(id, entraTenant: true, tenant: tenant);

    private static AdministratorViabilityState State(EffectiveSignInConfiguration configuration, params AdministratorFacts[] admins) =>
        new(
            AdministratorViabilityEvaluator.Evaluate(admins, configuration.Now),
            AdministratorViabilityEvaluator.Evaluate(admins, configuration.Restart),
            RecoveryHealthy: false,
            configuration.Trust,
            configuration);

    // ------------------------------------------------------------------ the host as it runs

    [Fact]
    public async Task The_now_view_agrees_with_the_named_handler_options_the_host_really_holds()
    {
        using var host = new Host(Registration(Tenant));
        host.Services.GetRequiredService<IOptions<EntraAuthOptions>>().Value.Enabled.Should().BeTrue("the host overlays the active registration at start");
        var handler = host.Handler();

        var configuration = await host.Provider().ReadAsync();

        handler.Authority.Should().Contain(Tenant);
        configuration.Now.EntraSignInUsable.Should().BeTrue();
        configuration.Now.AcceptedEntraTenantId.Should().Be(Tenant);
        configuration.Trust.EntraKnown.Should().BeTrue();
        configuration.Now.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_reload_refreshes_the_entra_options_but_not_the_handler_and_the_now_view_follows_the_handler()
    {
        using var host = new Host(Registration(Tenant));
        var started = host.Handler();
        started.Authority.Should().Contain(Tenant);
        host.Services.GetRequiredService<IOptions<EntraAuthOptions>>().Value.AzureTenantId.Should().Be(Tenant);

        // The persisted registration is switched to tenant B, and the appsettings are reloaded without a restart.
        host.Registrations.Active = Registration(OtherTenant);
        host.ReloadConfiguration();

        var entraMonitor = host.Services.GetRequiredService<IOptionsMonitor<EntraAuthOptions>>();
        entraMonitor.CurrentValue.AzureTenantId.Should().Be(OtherTenant, "the reload rebuilt the Entra options from the persisted registration");
        host.Services.GetRequiredService<IOptions<EntraAuthOptions>>().Value.AzureTenantId.Should().Be(Tenant, "what the endpoints gate on is fixed at start");
        host.Handler().Should().BeSameAs(started, "the framework does not rebuild the handler's options on an Entra reload");
        host.Handler().Authority.Should().Contain(Tenant).And.NotContain(OtherTenant);

        var configuration = await host.Provider().ReadAsync();

        configuration.Now.AcceptedEntraTenantId.Should().BeNull(
            "the handler still signs users in through tenant A while the sign-in checks tokens against tenant B");
        configuration.Now.Notes.Should().Contain(n => n.Contains("different directory"));
        configuration.Restart.AcceptedEntraTenantId.Should().Be(OtherTenant, "after a restart tenant B is accepted");
    }

    [Fact]
    public async Task An_administrator_proven_only_for_the_new_tenant_is_not_viable_now_but_is_after_the_restart()
    {
        using var host = new Host(Registration(Tenant));
        host.Handler();
        host.Registrations.Active = Registration(OtherTenant);
        host.ReloadConfiguration();

        var configuration = await host.Provider().ReadAsync();
        var state = State(configuration, EntraOnly("on-b", OtherTenant), EntraOnly("on-a", Tenant));

        state.Now.ViableOrdinaryAdministrators.Should().BeEmpty("with the handler on A and the check on B nobody can sign in through Entra yet");
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["on-b"]);
    }

    [Fact]
    public async Task The_guard_refuses_a_reduction_that_the_stale_monitor_would_have_let_through()
    {
        using var host = new Host(Registration(Tenant));
        host.Handler();
        host.Registrations.Active = Registration(OtherTenant);
        host.ReloadConfiguration();
        var configuration = await host.Provider().ReadAsync();

        var local = Admin("local", local: true);
        var disabled = new AdministratorFacts("local", false, HasLocalLink: true, LocalCredentialEnabled: false, []);
        var onB = EntraOnly("on-b", OtherTenant);

        // Before: "local" can sign in now, and "on-b" can after the restart. Disabling "local" leaves nobody who can sign in now.
        var pre = State(configuration, local, onB);
        var post = State(configuration, disabled, onB);
        var verdict = AdministratorViabilityGuard.Evaluate(pre, post);

        verdict.Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator);
        verdict.View.Should().Be(ViabilityView.Now);

        // The stale reading: the Entra monitor's refreshed value, as the first version of the Now view read it.
        var stale = host.Services.GetRequiredService<IOptionsMonitor<EntraAuthOptions>>().CurrentValue;
        stale.AzureTenantId.Should().Be(OtherTenant, "the monitor describes the tenant the handler does not hold");
    }

    [Fact]
    public async Task When_the_handlers_options_are_really_rebuilt_the_now_view_follows_the_new_handler()
    {
        using var host = new Host(Registration(Tenant));
        var started = host.Handler();
        host.Registrations.Active = Registration(OtherTenant);
        host.ReloadConfiguration();
        (await host.Provider().ReadAsync()).Now.AcceptedEntraTenantId.Should().BeNull();

        // The framework rebuilds the options when their cache is invalidated (here as a change token source would).
        host.Services.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>().Clear();
        var rebuilt = host.Handler();
        var configuration = await host.Provider().ReadAsync();

        rebuilt.Should().NotBeSameAs(started);
        rebuilt.Authority.Should().Contain(OtherTenant);
        configuration.Now.EntraSignInUsable.Should().BeTrue();
        configuration.Now.AcceptedEntraTenantId.Should().Be(OtherTenant, "the handler now signs users in through tenant B and the check is on tenant B");
        var state = State(configuration, EntraOnly("on-b", OtherTenant));
        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["on-b"]);
    }

    // ------------------------------------------------------------------ never optimistic

    [Fact]
    public async Task A_handler_whose_options_cannot_be_built_is_unavailable_never_usable_and_the_error_text_is_not_carried()
    {
        using var host = new Host(Registration(Tenant), throwWhenHandlerOptionsAreBuilt: true);
        var binding = host.Services.GetRequiredService<IRunningEntraHandlerBindingSource>().Read();

        var configuration = await host.Provider().ReadAsync();

        binding.State.Should().Be(RunningHandlerState.Unavailable);
        binding.IsUsable.Should().BeFalse();
        binding.Detail.Should().Contain("InvalidOperationException").And.NotContain("hunter2");
        configuration.Now.EntraSignInUsable.Should().BeFalse();
        configuration.Trust.EntraKnown.Should().BeFalse();
        configuration.Trust.Reasons.Should().NotContain(r => r.Contains("hunter2"));

        var admins = new[] { Admin("local", local: true), EntraOnly("on-a", Tenant) };
        var state = State(configuration, admins);
        AdministratorViabilityGuard.Evaluate(state, state).Kind.Should().Be(
            ViabilityVerdictKind.ConfigurationUnavailable,
            "a reduction cannot be checked against a handler nobody can describe");
    }

    [Fact]
    public async Task A_host_with_entra_off_holds_the_disabled_placeholders_and_they_are_not_a_sign_in_path()
    {
        using var host = new Host(active: null);
        host.Services.GetRequiredService<IOptions<EntraAuthOptions>>().Value.Enabled.Should().BeFalse();
        host.Handler().ClientId.Should().Be(EntraOpenIdConnectOptionsConfigurer.DisabledPlaceholder);

        var binding = host.Services.GetRequiredService<IRunningEntraHandlerBindingSource>().Read();
        var configuration = await host.Provider().ReadAsync();

        binding.State.Should().Be(RunningHandlerState.Disabled);
        binding.IsUsable.Should().BeFalse();
        configuration.Now.EntraSignInUsable.Should().BeFalse();
        configuration.Trust.EntraKnown.Should().BeTrue("it is known that Entra is off");
        State(configuration, EntraOnly("on-a", Tenant)).Now.ViableOrdinaryAdministrators.Should().BeEmpty();
    }

    [Fact]
    public void The_binding_never_carries_the_client_secret()
    {
        using var host = new Host(Registration(Tenant));
        host.Handler().ClientSecret.Should().Be("deployment-secret");

        var binding = host.Services.GetRequiredService<IRunningEntraHandlerBindingSource>().Read();

        binding.HasClientSecret.Should().BeTrue();
        System.Text.Json.JsonSerializer.Serialize(binding).Should().NotContain("deployment-secret");
    }

    // ------------------------------------------------------------------ the seam is not a second authority

    [Fact]
    public void Reading_the_binding_does_not_change_what_the_handler_holds()
    {
        using var host = new Host(Registration(Tenant));
        var before = host.Handler();
        var source = host.Services.GetRequiredService<IRunningEntraHandlerBindingSource>();

        source.Read();
        source.Read();

        host.Handler().Should().BeSameAs(before);
    }

    [Fact]
    public void The_host_registers_the_binding_source_beside_the_handler_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppertureAuthentication(new ConfigurationBuilder().Build());

        services.Where(d => d.ServiceType == typeof(IRunningEntraHandlerBindingSource)).Should().ContainSingle()
            .Which.ImplementationType.Should().Be(typeof(OpenIdConnectHandlerBindingSource));
    }
}
