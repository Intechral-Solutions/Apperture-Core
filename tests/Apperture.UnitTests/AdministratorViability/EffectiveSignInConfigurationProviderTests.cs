using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// The effective sign-in configuration in the Now and Restart views (rulings D-3 and D-4; A10.2b.16): the same inputs production
/// sign-in uses, the same secret and validation rules, and every disagreement reported rather than hidden.
/// </summary>
public sealed class EffectiveSignInConfigurationProviderTests
{
    private static string Authority(string tenant) => "https://login.microsoftonline.com/" + tenant;

    private static EntraInstanceRegistration Registration(
        string tenant = Tenant,
        bool enabled = true,
        string? secretRef = null,
        bool usesDeploymentSecret = true,
        string? context = null,
        string clientId = "client") => new()
    {
        Id = "reg",
        Enabled = enabled,
        IsActive = true,
        Authority = Authority(tenant),
        ClientId = clientId,
        ClientSecretRef = secretRef,
        UsesDeploymentSecret = usesDeploymentSecret,
        AzureTenantId = tenant,
        AuthenticationContextId = context,
    };

    private static EntraAuthOptions UsableEntra(string tenant = Tenant, string? context = null) => new()
    {
        Enabled = true,
        Authority = Authority(tenant),
        ClientId = "client",
        ClientSecret = "secret",
        AzureTenantId = tenant,
        AuthenticationContextId = context,
    };

    private static DeploymentAuthenticationReading Host(
        bool localDeployment = true,
        EntraAuthOptions? entraBase = null,
        RunningAuthenticationReading? running = null,
        ConfigurationTrustLevel localTrust = ConfigurationTrustLevel.Trusted,
        ConfigurationTrustLevel entraTrust = ConfigurationTrustLevel.Trusted,
        bool authoritative = true,
        string[]? reasons = null) =>
        new(localTrust, entraTrust, reasons ?? [], localDeployment, entraBase ?? new EntraAuthOptions { ClientSecret = "deployment-secret" }, authoritative, running);

    /// <summary>A running host whose endpoints read <paramref name="endpoints"/> and whose handler holds <paramref name="handler"/> (by default the same authority and client).</summary>
    private static RunningAuthenticationReading Running(EntraAuthOptions? endpoints = null, RunningHandlerBinding? handler = null, bool local = true)
    {
        var entra = endpoints ?? UsableEntra();
        return new RunningAuthenticationReading(
            new LocalAuthOptions { Enabled = local },
            entra,
            handler ?? RunningHandlerBinding.Bound(entra.Authority, entra.ClientId, hasClientSecret: !string.IsNullOrWhiteSpace(entra.ClientSecret)));
    }

    private static EffectiveSignInConfigurationProvider Provider(
        DeploymentAuthenticationReading reading,
        EntraInstanceRegistration? active,
        ISecretResolver? resolver = null)
    {
        var repo = new Mock<IEntraInstanceRegistrationRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
        return new EffectiveSignInConfigurationProvider(new StubDeployment(reading), repo.Object, resolver);
    }

    private static ISecretResolver Resolver(string secretRef, string material)
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.Is<SecretRef>(s => s.Value == secretRef), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(new SecretRef(secretRef), new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: material)));
        return resolver.Object;
    }

    private sealed class StubDeployment(DeploymentAuthenticationReading reading) : IDeploymentAuthenticationConfiguration
    {
        public DeploymentAuthenticationReading Read() => reading;
    }

    // ----- Restart: the persisted configuration after a restart -----

    [Fact]
    public async Task Restart_applies_the_persisted_registration_with_the_hosts_own_overlay_and_deployment_secret()
    {
        var provider = Provider(Host(), Registration(OtherTenant));

        var configuration = await provider.ReadAsync();

        configuration.Restart.EntraSignInUsable.Should().BeTrue();
        configuration.Restart.AcceptedEntraTenantId.Should().Be(OtherTenant);
        configuration.Restart.LocalSignInEnabled.Should().BeTrue();
        configuration.Restart.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public async Task Restart_prefers_the_registrations_stored_secret_which_the_old_lockout_simulation_could_not_see()
    {
        var deploymentWithoutSecret = new EntraAuthOptions();
        var provider = Provider(
            Host(entraBase: deploymentWithoutSecret),
            Registration(secretRef: "stored", usesDeploymentSecret: false),
            Resolver("stored", "from-store"));

        (await provider.ReadAsync()).Restart.EntraSignInUsable.Should().BeTrue(
            "the runtime resolves the stored secret first, so the registration is usable although no deployment secret exists");
    }

    [Fact]
    public async Task Restart_is_not_usable_when_no_secret_exists_and_the_reader_is_authoritative_about_that()
    {
        var provider = Provider(Host(entraBase: new EntraAuthOptions()), Registration());

        var configuration = await provider.ReadAsync();

        configuration.Restart.EntraSignInUsable.Should().BeFalse();
        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Trusted, "the host knows there is no secret");
    }

    [Fact]
    public async Task Restart_is_unusable_without_an_enabled_active_registration_and_accepts_no_tenant()
    {
        var disabled = await Provider(Host(), Registration(enabled: false)).ReadAsync();
        var none = await Provider(Host(), null).ReadAsync();

        disabled.Restart.EntraSignInUsable.Should().BeFalse();
        none.Restart.EntraSignInUsable.Should().BeFalse();
        none.Restart.AcceptedEntraTenantId.Should().BeNull();
    }

    [Fact]
    public async Task Restart_rejects_an_invalid_authentication_context_as_the_merged_snapshot_does()
    {
        var provider = Provider(Host(), Registration(context: "not-a-context"));

        (await provider.ReadAsync()).Restart.EntraSignInUsable.Should().BeFalse();
    }

    // ----- Now: the running host -----

    [Fact]
    public async Task Now_uses_the_options_the_host_is_running_not_the_persisted_ones()
    {
        // The host started with tenant A and an enabled registration; the persisted registration has since been switched to B.
        var running = Running(UsableEntra(Tenant));
        var provider = Provider(Host(running: running), Registration(OtherTenant));

        var configuration = await provider.ReadAsync();

        configuration.Now.EntraSignInUsable.Should().BeTrue("the running handler is still configured and usable");
        configuration.Now.AcceptedEntraTenantId.Should().BeNull(
            "the handler is bound to tenant A while the sign-in checks tokens against tenant B: no token is accepted until a restart");
        configuration.Now.Notes.Should().Contain(n => n.Contains("different directory"));
        configuration.Restart.AcceptedEntraTenantId.Should().Be(OtherTenant, "after a restart tenant B is what is accepted");
    }

    [Fact]
    public async Task Now_accepts_the_live_tenant_when_the_running_handler_is_bound_to_the_same_directory()
    {
        var running = Running(UsableEntra(Tenant));
        var provider = Provider(Host(running: running), Registration(Tenant, clientId: "renamed-client"));

        var configuration = await provider.ReadAsync();

        configuration.Now.EntraSignInUsable.Should().BeTrue();
        configuration.Now.AcceptedEntraTenantId.Should().Be(Tenant, "a same-tenant edit does not change who can sign in");
    }

    [Fact]
    public async Task Now_accepts_no_tenant_when_there_is_no_active_registration_even_though_the_running_options_are_usable()
    {
        var provider = Provider(Host(running: Running()), active: null);

        var configuration = await provider.ReadAsync();

        configuration.Now.EntraSignInUsable.Should().BeTrue();
        configuration.Now.AcceptedEntraTenantId.Should().BeNull("the sign-in matches the token tenant against the active registration, so no registration accepts none");
    }

    [Fact]
    public async Task Now_does_not_compare_a_domain_name_authority_with_a_directory_id()
    {
        var handler = RunningHandlerBinding.Bound("https://login.microsoftonline.com/contoso.onmicrosoft.com", "client", hasClientSecret: true);
        var provider = Provider(Host(running: Running(handler: handler)), Registration(Tenant));

        (await provider.ReadAsync()).Now.AcceptedEntraTenantId.Should().Be(Tenant);
    }

    [Fact]
    public async Task Now_counts_entra_as_usable_only_when_the_endpoints_and_the_handler_both_are_and_reports_a_disagreement()
    {
        var endpoints = UsableEntra();
        var handler = RunningHandlerBinding.Bound(endpoints.Authority, endpoints.ClientId, hasClientSecret: false);
        var provider = Provider(Host(running: Running(endpoints, handler)), Registration());

        var configuration = await provider.ReadAsync();

        configuration.Now.EntraSignInUsable.Should().BeFalse();
        configuration.Now.Notes.Should().Contain(n => n.Contains("disagree"));
    }

    [Fact]
    public async Task Now_reports_when_the_snapshot_and_is_usable_disagree_about_an_invalid_authentication_context_and_uses_the_stricter()
    {
        var invalidContext = UsableEntra(context: "not-a-context");
        var provider = Provider(Host(running: Running(invalidContext)), Registration());

        var configuration = await provider.ReadAsync();

        EntraConfigurationState.IsUsable(invalidContext).Should().BeTrue("IsUsable does not look at the authentication context");
        configuration.Now.EntraSignInUsable.Should().BeFalse("the merged snapshot, the single definition, rejects it");
        configuration.Now.Notes.Should().Contain(n => n.Contains("authentication context"));
    }

    [Fact]
    public async Task A_handler_whose_configuration_cannot_be_established_is_never_read_as_usable_and_makes_the_entra_reading_unknown()
    {
        var running = Running(handler: RunningHandlerBinding.Unavailable("its options could not be built (InvalidOperationException)"));
        var configuration = await Provider(Host(running: running), Registration()).ReadAsync();

        configuration.Now.EntraSignInUsable.Should().BeFalse("an unknown handler is not optimistically a usable one");
        configuration.Now.AcceptedEntraTenantId.Should().BeNull();
        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        configuration.Trust.EntraKnown.Should().BeFalse();
        configuration.Trust.Reasons.Should().Contain(r => r.Contains("running sign-in handler") && r.Contains("InvalidOperationException"));
        configuration.Trust.LocalKnown.Should().BeTrue("the Local reading does not depend on the handler");
    }

    [Fact]
    public async Task A_handler_holding_the_disabled_placeholders_is_a_known_unusable_entra_not_a_sign_in_path()
    {
        var running = Running(handler: RunningHandlerBinding.Disabled());
        var configuration = await Provider(Host(running: running), Registration()).ReadAsync();

        configuration.Now.EntraSignInUsable.Should().BeFalse();
        configuration.Now.AcceptedEntraTenantId.Should().BeNull();
        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Trusted, "it is known that Entra is off in the running host");
    }

    [Fact]
    public async Task A_bound_handler_without_a_secret_is_not_usable()
    {
        var endpoints = UsableEntra();
        var running = Running(endpoints, RunningHandlerBinding.Bound(endpoints.Authority, endpoints.ClientId, hasClientSecret: false));

        (await Provider(Host(running: running), Registration()).ReadAsync()).Now.EntraSignInUsable.Should().BeFalse();
    }

    [Fact]
    public async Task Local_sign_in_follows_only_the_local_option_so_the_direct_login_route_is_independent_of_the_chooser()
    {
        var local = await Provider(Host(running: Running(local: true)), Registration()).ReadAsync();
        var off = await Provider(Host(localDeployment: false, running: Running(local: false)), Registration()).ReadAsync();

        local.Now.LocalSignInEnabled.Should().BeTrue();
        off.Now.LocalSignInEnabled.Should().BeFalse();
        off.Restart.LocalSignInEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_running_host_whose_local_option_differs_from_the_deployment_is_reported_per_view()
    {
        var provider = Provider(Host(localDeployment: false, running: Running(local: true)), Registration());

        var configuration = await provider.ReadAsync();

        configuration.Now.LocalSignInEnabled.Should().BeTrue("the host that is running still serves Local login");
        configuration.Restart.LocalSignInEnabled.Should().BeFalse("after a restart it would not");
    }

    // ----- A reader that is not a running host (the emergency command line) -----

    [Fact]
    public async Task A_reader_that_cannot_observe_a_running_host_estimates_the_now_view_from_the_restart_view_and_says_so()
    {
        var configuration = await Provider(Host(running: null), Registration()).ReadAsync();

        configuration.Now.IsEstimate.Should().BeTrue();
        configuration.Now.View.Should().Be(ViabilityView.Now);
        configuration.Now.EntraSignInUsable.Should().Be(configuration.Restart.EntraSignInUsable);
        configuration.Now.AcceptedEntraTenantId.Should().Be(configuration.Restart.AcceptedEntraTenantId);
        configuration.Now.Notes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_reader_that_cannot_see_the_hosts_secrets_does_not_claim_the_secret_is_absent()
    {
        var provider = Provider(
            Host(entraBase: new EntraAuthOptions(), running: null, authoritative: false),
            Registration());

        var configuration = await provider.ReadAsync();

        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        configuration.Trust.EntraKnown.Should().BeFalse();
        configuration.Trust.Reasons.Should().Contain(r => r.Contains("client secret"));
    }

    [Fact]
    public async Task A_non_authoritative_reader_that_does_find_a_secret_is_trusted_about_entra()
    {
        var provider = Provider(Host(running: null, authoritative: false), Registration());

        (await provider.ReadAsync()).Trust.EntraKnown.Should().BeTrue();
    }

    [Fact]
    public async Task The_trust_and_the_reasons_of_the_deployment_reading_are_passed_through()
    {
        var provider = Provider(
            Host(localTrust: ConfigurationTrustLevel.Unknown, reasons: ["Set LocalAuth:Enabled."], running: null),
            Registration());

        var configuration = await provider.ReadAsync();

        configuration.Trust.Local.Should().Be(ConfigurationTrustLevel.Unknown);
        configuration.Trust.LocalKnown.Should().BeFalse();
        configuration.Trust.Reasons.Should().Contain("Set LocalAuth:Enabled.");
    }

    // ----- The same inputs as production: differential against the host's own options build -----

    /// <summary>The rule as it was written in <c>EntraAuthOptionsDatabasePostConfigure</c> before it moved to the shared overlay.</summary>
    private static async Task ReferenceOverlayAsync(EntraInstanceRegistration? active, EntraAuthOptions options, ISecretResolver? resolver)
    {
        if (active is null || !active.Enabled)
            return;

        options.Authority = active.Authority;
        options.ClientId = active.ClientId;
        if (!string.IsNullOrWhiteSpace(active.AzureTenantId))
            options.AzureTenantId = active.AzureTenantId;
        options.AuthenticationContextId = string.IsNullOrWhiteSpace(active.AuthenticationContextId)
            ? null
            : active.AuthenticationContextId.Trim();
        if (!string.IsNullOrWhiteSpace(active.CallbackPath))
            options.CallbackPath = active.CallbackPath!;

        var resolved = await EntraClientSecretAccess.TryResolveAsync(active, resolver, options.ClientSecret, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(resolved))
            options.ClientSecret = resolved;

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            return;

        options.Enabled = true;
    }

    private static EntraAuthOptions Fresh(string secret, bool enabled, string? azureTenant) => new()
    {
        Enabled = enabled,
        ClientSecret = secret,
        Authority = "https://login.microsoftonline.com/common",
        ClientId = "base-client",
        AzureTenantId = azureTenant,
        CallbackPath = "/signin-oidc",
    };

    public static IEnumerable<object?[]> Corpus()
    {
        var registrations = new EntraInstanceRegistration?[]
        {
            null,
            Registration(enabled: false),
            Registration(),
            Registration(OtherTenant, usesDeploymentSecret: false),
            Registration(secretRef: "stored"),
            Registration(secretRef: "missing", usesDeploymentSecret: false),
            Registration(secretRef: "missing"),
            Registration(context: " c3 "),
            Registration(context: "bad"),
        };

        foreach (var registration in registrations)
        foreach (var secret in new[] { "", "deployment-secret" })
        foreach (var enabled in new[] { false, true })
        foreach (var azureTenant in new string?[] { null, OtherTenant })
            yield return [registration, secret, enabled, azureTenant];
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task The_shared_overlay_builds_exactly_the_options_the_host_built_before_it_was_shared(
        EntraInstanceRegistration? active, string secret, bool enabled, string? azureTenant)
    {
        var resolver = StoredResolver();
        var viaOverlay = Fresh(secret, enabled, azureTenant);
        var viaReference = Fresh(secret, enabled, azureTenant);
        var viaHost = Fresh(secret, enabled, azureTenant);

        await EntraAuthOptionsOverlay.ApplyAsync(active, viaOverlay, resolver);
        await ReferenceOverlayAsync(active, viaReference, resolver);
        HostPostConfigure(active, resolver).PostConfigure(Options.DefaultName, viaHost);

        Same(viaOverlay, viaReference);
        Same(viaHost, viaReference);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task The_restart_view_is_what_the_host_would_build_and_validate_from_the_persisted_registration(
        EntraInstanceRegistration? active, string secret, bool enabled, string? azureTenant)
    {
        var resolver = StoredResolver();
        var built = Fresh(secret, enabled, azureTenant);
        HostPostConfigure(active, resolver).PostConfigure(Options.DefaultName, built);
        var expectedUsable = AuthSignInOptionsMerger
            .Merge(new LocalAuthOptions { Enabled = true }, built, new InstanceAuthUiPolicy())
            .EntraSignInEnabled;

        var provider = Provider(Host(entraBase: Fresh(secret, enabled, azureTenant)), active, resolver);
        var configuration = await provider.ReadAsync();

        configuration.Restart.EntraSignInUsable.Should().Be(expectedUsable);
        configuration.Restart.AcceptedEntraTenantId.Should().Be(EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(active));
    }

    private static ISecretResolver StoredResolver()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.Is<SecretRef>(s => s.Value == "stored"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(new SecretRef("stored"), new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "from-store")));
        resolver
            .Setup(r => r.ResolveAsync(It.Is<SecretRef>(s => s.Value == "missing"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not found"));
        return resolver.Object;
    }

    private static EntraAuthOptionsDatabasePostConfigure HostPostConfigure(EntraInstanceRegistration? active, ISecretResolver resolver)
    {
        var repo = new Mock<IEntraInstanceRegistrationRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
        var services = new ServiceCollection();
        services.AddSingleton(repo.Object);
        services.AddSingleton(resolver);
        var provider = services.BuildServiceProvider();
        return new EntraAuthOptionsDatabasePostConfigure(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EntraAuthOptionsDatabasePostConfigure>.Instance);
    }

    private static void Same(EntraAuthOptions actual, EntraAuthOptions expected)
    {
        actual.Enabled.Should().Be(expected.Enabled);
        actual.Authority.Should().Be(expected.Authority);
        actual.ClientId.Should().Be(expected.ClientId);
        actual.ClientSecret.Should().Be(expected.ClientSecret);
        actual.AzureTenantId.Should().Be(expected.AzureTenantId);
        actual.AuthenticationContextId.Should().Be(expected.AuthenticationContextId);
        actual.CallbackPath.Should().Be(expected.CallbackPath);
    }

    // ----- The tenant the sign-in accepts -----

    [Fact]
    public void The_accepted_tenant_is_the_registrations_azure_tenant_id_trimmed_and_lower_cased()
    {
        var registration = Registration();
        var padded = new EntraInstanceRegistration { Id = "r", Authority = Authority("common"), AzureTenantId = " " + Tenant.ToUpperInvariant() + " " };

        EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(registration).Should().Be(Tenant);
        EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(padded).Should().Be(Tenant);
    }

    [Fact]
    public void Without_an_azure_tenant_id_the_authoritys_tenant_segment_is_used_and_an_unresolvable_tenant_accepts_nothing()
    {
        var fromAuthority = new EntraInstanceRegistration { Id = "r", Authority = Authority(Tenant) };
        var elsewhere = new EntraInstanceRegistration { Id = "r", Authority = "https://example.test/" + Tenant };

        EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(fromAuthority).Should().Be(Tenant);
        EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(elsewhere).Should().BeNull();
        EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(null).Should().BeNull();
    }

    [Fact]
    public void The_multi_tenant_alias_is_accepted_as_a_tenant_text_that_no_token_directory_id_equals()
    {
        var common = new EntraInstanceRegistration { Id = "r", Authority = Authority("common") };

        var accepted = EntraDirectoryTenantResolver.ResolveAcceptedTokenTenantId(common);

        accepted.Should().Be("common");
        EntraTokenClaims.TenantMatches(Tenant, accepted).Should().BeFalse("a token carries a directory id, never 'common'");
    }

    // IAM-10.2b WP5, A10.2b.16: the merged sign-in snapshot and EntraConfigurationState.IsUsable over a generated corpus of running
    // options, not only the one case above. They differ in exactly one way, the difference WP0 recorded (F-2): an authentication context
    // id that is set but not a valid one, which the snapshot rejects and IsUsable ignores. Wherever they differ, the provider reports it
    // and counts Entra as not usable: the stricter, the snapshot, is used.
    [Fact]
    public async Task Over_a_generated_corpus_the_snapshot_and_is_usable_differ_only_on_an_invalid_authentication_context_and_the_stricter_is_used()
    {
        string?[] authorities = [Authority(Tenant), "https://login.microsoftonline.com/contoso.onmicrosoft.com", "not a url", "", "   ", null];
        string?[] clientIds = ["client", "", " ", null];
        string?[] secrets = ["secret", "", " ", null];
        string?[] contexts = [null, "", " ", "c1", "C25", " c3 ", "c26", "c0", "not-a-context"];
        var compared = 0;
        var differing = 0;

        foreach (var enabled in new[] { true, false })
        foreach (var authority in authorities)
        foreach (var clientId in clientIds)
        foreach (var secret in secrets)
        foreach (var context in contexts)
        foreach (var local in new[] { true, false })
        {
            var options = new EntraAuthOptions
            {
                Enabled = enabled,
                Authority = authority!,
                ClientId = clientId!,
                ClientSecret = secret!,
                AzureTenantId = Tenant,
                AuthenticationContextId = context,
            };
            var snapshot = AuthSignInOptionsMerger.Merge(new LocalAuthOptions { Enabled = local }, options, new InstanceAuthUiPolicy()).EntraSignInEnabled;
            var isUsable = EntraConfigurationState.IsUsable(options);
            compared++;
            if (snapshot == isUsable)
                continue;

            differing++;
            var invalidContext = !string.IsNullOrWhiteSpace(context) && EntraAuthenticationContextId.TryNormalize(context) is null;
            (isUsable && !snapshot && invalidContext).Should().BeTrue(
                $"the only recorded difference is an invalid authentication context, with the snapshot the stricter (enabled={enabled}, authority='{authority}', client='{clientId}', secret set={!string.IsNullOrWhiteSpace(secret)}, context='{context}')");

            var configuration = await Provider(Host(running: Running(options, local: local)), Registration()).ReadAsync();
            configuration.Now.EntraSignInUsable.Should().BeFalse("the stricter answer is the one used");
            configuration.Now.Notes.Should().Contain(n => n.Contains("IsUsable disagree", StringComparison.Ordinal), "the difference is reported, not hidden");
        }

        compared.Should().Be(2 * 6 * 4 * 4 * 9 * 2);
        differing.Should().BeGreaterThan(0, "the corpus contains the recorded difference, so the comparison is not vacuous");
    }
}
