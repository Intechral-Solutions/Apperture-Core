using System.Data.Common;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Moq;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// IAM-10.2b WP3 review, L-1: a database failure while the viability evaluation resolves a secret inside an audited unit (a
/// deadlock victim, a timeout) is the unit's failure, for its own rollback and retry. It is never the secret store being
/// "unavailable", which would become a business refusal. The default mode, which the host's sign-in and every other caller use,
/// still contains such a failure as it always has.
/// </summary>
public sealed class EntraClientSecretDatabaseFailureTests
{
    private const string StoredRef = "550e8400-e29b-41d4-a716-446655440000";

    private static EntraInstanceRegistration Registration(bool usesDeploymentSecret = false) =>
        new() { ClientSecretRef = StoredRef, UsesDeploymentSecret = usesDeploymentSecret };

    private static ISecretResolver Throwing(Exception failure)
    {
        var resolver = new Mock<ISecretResolver>();
        // Thrown as the call is made, so an aggregate stays an aggregate (a faulted task would be unwrapped by the await).
        resolver.Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>())).Throws(failure);
        return resolver.Object;
    }

    private sealed class DatabaseFailure(string message, Exception? inner = null) : DbException(message, inner);

    public static IEnumerable<object[]> DatabaseFailures() =>
    [
        [new DatabaseFailure("deadlock victim")],
        [new InvalidOperationException("the resolver failed", new DatabaseFailure("deadlock victim"))],
        [new AggregateException(new IOException("other"), new DatabaseFailure("timeout"))],
        [new InvalidOperationException("outer", new InvalidOperationException("middle", new DatabaseFailure("deadlock victim")))],
    ];

    public static IEnumerable<object[]> StoreOutages() =>
    [
        [new IOException("the secret store is unreachable")],
        [new TimeoutException("the secret store timed out")],
        [new InvalidOperationException("No resolver available for SecretRef format.")],
        [new HttpRequestException("the key vault could not be reached")],
    ];

    // ----- the viability mode -----

    [Theory]
    [MemberData(nameof(DatabaseFailures))]
    public async Task In_the_viability_mode_a_database_failure_propagates_and_is_not_read_as_an_unavailable_secret(Exception failure)
    {
        var thrown = await Record.ExceptionAsync(() => EntraClientSecretAccess
            .ResolveAsync(Registration(), Throwing(failure), "deployment", CancellationToken.None, EntraSecretFailureHandling.PropagateDatabaseFailures)
            .AsTask());

        thrown.Should().BeSameAs(failure, "the very exception reaches the unit's retry classification, unwrapped and unreplaced");
    }

    [Theory]
    [MemberData(nameof(StoreOutages))]
    public async Task In_the_viability_mode_a_store_that_cannot_be_reached_is_still_unavailable(Exception failure)
    {
        var result = await EntraClientSecretAccess.ResolveAsync(
            Registration(), Throwing(failure), null, CancellationToken.None, EntraSecretFailureHandling.PropagateDatabaseFailures);

        result.Status.Should().Be(EntraClientSecretStatus.Unavailable);
    }

    [Theory]
    [InlineData(typeof(SecretNotFoundException))]
    [InlineData(typeof(SecretUnavailableException))]
    public async Task In_the_viability_mode_a_confirmed_absence_is_still_absent(Type absence)
    {
        var result = await EntraClientSecretAccess.ResolveAsync(
            Registration(), Throwing((Exception)Activator.CreateInstance(absence)!), null, CancellationToken.None, EntraSecretFailureHandling.PropagateDatabaseFailures);

        result.Status.Should().Be(EntraClientSecretStatus.Absent);
    }

    [Fact]
    public async Task In_the_viability_mode_a_secret_that_resolves_is_resolved()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(new SecretRef(StoredRef), new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "stored"), Version: "1"));

        var result = await EntraClientSecretAccess.ResolveAsync(
            Registration(), resolver.Object, null, CancellationToken.None, EntraSecretFailureHandling.PropagateDatabaseFailures);

        result.Status.Should().Be(EntraClientSecretStatus.Resolved);
        result.Secret.Should().Be("stored");
    }

    [Fact]
    public async Task In_the_viability_mode_cancellation_still_propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .Returns<SecretRef, CancellationToken>((_, ct) => ValueTask.FromException<ResolvedSecret>(new OperationCanceledException(ct)));

        var resolve = () => EntraClientSecretAccess
            .ResolveAsync(Registration(), resolver.Object, null, cancelled.Token, EntraSecretFailureHandling.PropagateDatabaseFailures)
            .AsTask();

        await resolve.Should().ThrowAsync<OperationCanceledException>();
    }

    // ----- everyone else: unchanged -----

    [Theory]
    [MemberData(nameof(DatabaseFailures))]
    public async Task By_default_a_database_failure_is_contained_exactly_as_before(Exception failure)
    {
        var result = await EntraClientSecretAccess.ResolveAsync(Registration(usesDeploymentSecret: true), Throwing(failure), "deployment");
        var legacy = await EntraClientSecretAccess.TryResolveAsync(Registration(usesDeploymentSecret: true), Throwing(failure), "deployment");

        result.Status.Should().Be(EntraClientSecretStatus.Unavailable);
        result.Secret.Should().Be("deployment", "the sign-in falls through to the deployment secret, as it always has");
        legacy.Should().Be("deployment");
    }

    [Theory]
    [MemberData(nameof(DatabaseFailures))]
    public async Task The_overlay_the_host_builds_its_options_with_contains_a_database_failure_as_before(Exception failure)
    {
        var options = new EntraAuthOptions { ClientSecret = "deployment" };

        var result = await EntraAuthOptionsOverlay.ApplyDetailedAsync(Enabled(), options, Throwing(failure));

        result.SecretStatus.Should().Be(EntraClientSecretStatus.Unavailable);
        result.Outcome.Should().Be(EntraOverlayOutcome.Applied);
    }

    // ----- the viability read -----

    [Theory]
    [MemberData(nameof(DatabaseFailures))]
    public async Task The_effective_sign_in_reading_lets_a_database_failure_reach_the_unit(Exception failure)
    {
        var provider = Provider(Throwing(failure));

        var thrown = await Record.ExceptionAsync(() => provider.ReadAsync().AsTask());

        thrown.Should().BeSameAs(failure);
    }

    [Theory]
    [MemberData(nameof(StoreOutages))]
    public async Task The_effective_sign_in_reading_still_reports_a_store_outage_as_an_unknown_entra_reading(Exception failure)
    {
        var configuration = await Provider(Throwing(failure)).ReadAsync();

        configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown);
        configuration.Trust.Local.Should().Be(ConfigurationTrustLevel.Trusted);
    }

    private static EntraInstanceRegistration Enabled() => new()
    {
        Id = "reg",
        Enabled = true,
        IsActive = true,
        Authority = "https://login.microsoftonline.com/" + Tenant,
        ClientId = "client",
        ClientSecretRef = StoredRef,
        UsesDeploymentSecret = true,
        AzureTenantId = Tenant,
    };

    private static EffectiveSignInConfigurationProvider Provider(ISecretResolver resolver)
    {
        var repository = new Mock<IEntraInstanceRegistrationRepository>();
        repository.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Enabled());
        var reading = new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], true,
            new EntraAuthOptions { ClientSecret = "deployment-secret" }, AuthoritativeAboutSecrets: true, Running: null);
        return new EffectiveSignInConfigurationProvider(new FixedDeployment(reading), repository.Object, resolver);
    }

    private sealed class FixedDeployment(DeploymentAuthenticationReading reading) : IDeploymentAuthenticationConfiguration
    {
        public DeploymentAuthenticationReading Read() => reading;
    }
}
