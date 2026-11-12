using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Moq;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// IAM-10.2b WP3: what the effective sign-in configuration says when it cannot be sure. A secret the store could not be asked for,
/// and running options built while the registration or its secret could not be read, are unknown, never absent and never usable;
/// a secret that is confirmed missing is a known absence; the legacy deployment-secret fallback is read as the host applies it
/// and reported.
/// </summary>
public sealed class EffectiveSignInConfigurationUncertaintyTests
{
    private const string StoredRef = "550e8400-e29b-41d4-a716-446655440000";

    private static string Authority(string tenant) => "https://login.microsoftonline.com/" + tenant;

    private static EntraInstanceRegistration Registration(string? secretRef = null, bool usesDeploymentSecret = true) => new()
    {
        Id = "reg",
        Enabled = true,
        IsActive = true,
        Authority = Authority(Tenant),
        ClientId = "client",
        ClientSecretRef = secretRef,
        UsesDeploymentSecret = usesDeploymentSecret,
        AzureTenantId = Tenant,
    };

    private static EntraAuthOptions UsableEntra() => new()
    {
        Enabled = true,
        Authority = Authority(Tenant),
        ClientId = "client",
        ClientSecret = "secret",
        AzureTenantId = Tenant,
    };

    private static DeploymentAuthenticationReading Host(
        EntraAuthOptions? entraBase = null, RunningAuthenticationReading? running = null, bool authoritative = true) =>
        new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], true, entraBase ?? new EntraAuthOptions { ClientSecret = "deployment-secret" }, authoritative, running);

    private static RunningAuthenticationReading Running(EntraOptionsBuildQuality? endpoints = null, EntraOptionsBuildQuality? handler = null)
    {
        var entra = UsableEntra();
        return new RunningAuthenticationReading(
            new LocalAuthOptions { Enabled = true },
            entra,
            RunningHandlerBinding.Bound(entra.Authority, entra.ClientId, hasClientSecret: true) with { Quality = handler },
            endpoints);
    }

    private static EffectiveSignInConfigurationProvider Provider(
        DeploymentAuthenticationReading reading, EntraInstanceRegistration? active, ISecretResolver? resolver = null)
    {
        var repo = new Mock<IEntraInstanceRegistrationRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(active);
        return new EffectiveSignInConfigurationProvider(new StubDeployment(reading), repo.Object, resolver);
    }

    private static ISecretResolver Failing(Exception failure)
    {
        var resolver = new Mock<ISecretResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        return resolver.Object;
    }

    private sealed class StubDeployment(DeploymentAuthenticationReading reading) : IDeploymentAuthenticationConfiguration
    {
        public DeploymentAuthenticationReading Read() => reading;
    }

    // ----- Restart: the registration's secret -----

    [Fact]
    public async Task A_secret_store_outage_makes_the_entra_reading_unknown_even_where_the_deployment_secret_would_stand_in()
    {
        var configuration = await Provider(Host(), Registration(StoredRef, usesDeploymentSecret: true), Failing(new IOException("down"))).ReadAsync();

        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        configuration.Trust.Reasons.Should().Contain(r => r.Contains("client secret") && r.Contains("could not be read"));
        configuration.Restart.EntraSignInUsable.Should().BeTrue("the sign-in falls through to the deployment secret, as it always has");
        configuration.Trust.Local.Should().Be(ConfigurationTrustLevel.Trusted);
    }

    [Fact]
    public async Task A_secret_store_outage_is_unknown_not_absent_where_no_other_secret_exists()
    {
        var configuration = await Provider(Host(new EntraAuthOptions()), Registration(StoredRef, usesDeploymentSecret: false), Failing(new TimeoutException())).ReadAsync();

        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        configuration.Restart.EntraSignInUsable.Should().BeFalse();
    }

    [Fact]
    public async Task A_secret_the_store_confirms_missing_is_a_known_absence()
    {
        var configuration = await Provider(
            Host(new EntraAuthOptions()), Registration(StoredRef, usesDeploymentSecret: false), Failing(new SecretNotFoundException())).ReadAsync();

        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Trusted, "the store answered; the host knows it has no secret");
        configuration.Restart.EntraSignInUsable.Should().BeFalse();
    }

    [Fact]
    public async Task The_reasons_never_carry_the_reference_or_the_text_of_the_resolver_error()
    {
        var configuration = await Provider(
            Host(), Registration(StoredRef), Failing(new IOException("server=secret-host;password=hunter2"))).ReadAsync();

        string.Join(" ", configuration.Trust.Reasons).Should().NotContain(StoredRef).And.NotContain("hunter2").And.NotContain("secret-host");
        string.Join(" ", configuration.Restart.Notes).Should().NotContain(StoredRef).And.NotContain("hunter2");
    }

    [Fact]
    public async Task Cancellation_while_resolving_the_secret_is_not_swallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .Returns<SecretRef, CancellationToken>((_, ct) => ValueTask.FromException<ResolvedSecret>(new OperationCanceledException(ct)));

        var read = () => Provider(Host(), Registration(StoredRef), resolver.Object).ReadAsync(cancelled.Token).AsTask();

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    // ----- The deployment-secret fallback -----

    [Fact]
    public async Task The_legacy_deployment_secret_fallback_is_read_as_the_host_applies_it_and_reported()
    {
        // The registration neither asks for the deployment secret nor holds one; the host still enables Entra with it.
        var configuration = await Provider(Host(), Registration(secretRef: null, usesDeploymentSecret: false)).ReadAsync();

        configuration.Restart.EntraSignInUsable.Should().BeTrue("the viability reading agrees with what the host runs");
        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Trusted);
        configuration.Restart.Notes.Should().Contain(n => n.Contains("deployment") && n.Contains("does not ask for"));
    }

    [Fact]
    public async Task The_deployment_secret_a_registration_asks_for_carries_no_note()
    {
        var configuration = await Provider(Host(), Registration(secretRef: null, usesDeploymentSecret: true)).ReadAsync();

        configuration.Restart.EntraSignInUsable.Should().BeTrue();
        configuration.Restart.Notes.Should().BeEmpty();
    }

    // ----- Now: options built while the registration or its secret could not be read -----

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Running_options_built_in_a_degraded_state_make_the_entra_reading_unknown(bool endpointsDegraded, bool handlerDegraded)
    {
        var running = Running(
            endpoints: endpointsDegraded ? EntraOptionsBuildQuality.Degraded : EntraOptionsBuildQuality.Complete,
            handler: handlerDegraded ? EntraOptionsBuildQuality.Degraded : EntraOptionsBuildQuality.Complete);

        var configuration = await Provider(Host(running: running), Registration()).ReadAsync();

        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        configuration.Trust.Reasons.Should().Contain(r => r.Contains("could not be read") && r.Contains("restart"));
    }

    [Fact]
    public async Task Running_options_built_completely_leave_the_entra_reading_trusted()
    {
        var running = Running(EntraOptionsBuildQuality.Complete, EntraOptionsBuildQuality.Complete);

        var configuration = await Provider(Host(running: running), Registration()).ReadAsync();

        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Trusted);
        configuration.Now.EntraSignInUsable.Should().BeTrue();
    }

    [Fact]
    public async Task Running_options_whose_build_was_never_recorded_are_not_distrusted()
    {
        // A host that registers no build record (a test host, the command line) says nothing about how its options were built.
        var configuration = await Provider(Host(running: Running()), Registration()).ReadAsync();

        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Trusted);
    }
}
