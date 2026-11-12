using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// IAM-10.2b WP3: the status-aware resolution of an Entra registration's client secret. <c>TryResolveAsync</c> has always turned a
/// secret-store failure into "fall through to the deployment secret"; that is right for signing in and wrong for deciding whether
/// a change leaves anybody able to sign in, which must tell a secret that is confirmed absent from one that could not be read.
/// The runtime value is unchanged: only the classification is new.
/// </summary>
public sealed class EntraClientSecretResolutionTests
{
    private const string StoredRef = "550e8400-e29b-41d4-a716-446655440000";

    private static EntraInstanceRegistration Registration(string? secretRef = StoredRef, bool usesDeploymentSecret = true) =>
        new() { ClientSecretRef = secretRef, UsesDeploymentSecret = usesDeploymentSecret };

    private static Mock<ISecretResolver> ResolverReturning(string? opaque)
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(new SecretRef(StoredRef), new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: opaque), Version: "1"));
        return resolver;
    }

    private static Mock<ISecretResolver> ResolverThrowing(Exception exception)
    {
        var resolver = new Mock<ISecretResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>())).ThrowsAsync(exception);
        return resolver;
    }

    [Fact]
    public async Task A_stored_secret_that_resolves_is_resolved_from_the_secret_store()
    {
        var result = await EntraClientSecretAccess.ResolveAsync(Registration(), ResolverReturning("from-store").Object, "from-config");

        result.Status.Should().Be(EntraClientSecretStatus.Resolved);
        result.Source.Should().Be(EntraClientSecretSource.SecretStore);
        result.Secret.Should().Be("from-store");
    }

    [Fact]
    public async Task A_stored_secret_that_is_confirmed_missing_is_absent_and_the_deployment_secret_still_applies_where_the_registration_uses_it()
    {
        var resolver = ResolverThrowing(new SecretNotFoundException());

        var withFallback = await EntraClientSecretAccess.ResolveAsync(Registration(), resolver.Object, "from-config");
        var without = await EntraClientSecretAccess.ResolveAsync(Registration(usesDeploymentSecret: false), resolver.Object, "from-config");

        withFallback.Status.Should().Be(EntraClientSecretStatus.Resolved);
        withFallback.Source.Should().Be(EntraClientSecretSource.DeploymentSecret);
        withFallback.Secret.Should().Be("from-config");
        without.Status.Should().Be(EntraClientSecretStatus.Absent, "the store answered that there is no such secret, and the registration does not use the deployment one");
        without.Source.Should().Be(EntraClientSecretSource.None);
        without.Secret.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_stored_secret_with_no_value_is_absent_not_unavailable(string? material)
    {
        var result = await EntraClientSecretAccess.ResolveAsync(Registration(usesDeploymentSecret: false), ResolverReturning(material).Object, null);

        result.Status.Should().Be(EntraClientSecretStatus.Absent);
        result.Secret.Should().BeNull();
    }

    [Fact]
    public async Task The_refusal_of_an_owned_resolution_is_a_confirmed_absence()
    {
        var result = await EntraClientSecretAccess.ResolveAsync(
            Registration(usesDeploymentSecret: false), ResolverThrowing(new SecretUnavailableException()).Object, null);

        result.Status.Should().Be(EntraClientSecretStatus.Absent);
    }

    public static IEnumerable<object[]> Outages() =>
    [
        [new InvalidOperationException("No resolver available for SecretRef format.")],
        [new TimeoutException("the secret store timed out")],
        [new IOException("the secret store is unreachable")],
        [new ArgumentException("Invalid SecretRef: Value must be a Guid.")],
    ];

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task A_failure_to_read_the_store_is_unavailable_and_never_a_confirmed_absence(Exception failure)
    {
        var result = await EntraClientSecretAccess.ResolveAsync(Registration(usesDeploymentSecret: false), ResolverThrowing(failure).Object, null);

        result.Status.Should().Be(EntraClientSecretStatus.Unavailable);
        result.Secret.Should().BeNull();
    }

    [Fact]
    public async Task An_unavailable_store_still_yields_the_deployment_secret_as_the_runtime_value_but_is_never_reported_as_resolved()
    {
        // The runtime behavior is unchanged (the sign-in falls through to the deployment secret); the status says the intended
        // secret could not be read, so the secret that would be used is not the one the registration names.
        var result = await EntraClientSecretAccess.ResolveAsync(Registration(), ResolverThrowing(new IOException("down")).Object, "from-config");
        var legacy = await EntraClientSecretAccess.TryResolveAsync(Registration(), ResolverThrowing(new IOException("down")).Object, "from-config");

        result.Status.Should().Be(EntraClientSecretStatus.Unavailable);
        result.Source.Should().Be(EntraClientSecretSource.DeploymentSecret);
        result.Secret.Should().Be("from-config");
        legacy.Should().Be("from-config", "TryResolveAsync keeps its value");
    }

    [Fact]
    public async Task A_stored_reference_with_no_resolver_to_ask_is_unavailable()
    {
        var result = await EntraClientSecretAccess.ResolveAsync(Registration(usesDeploymentSecret: false), null, null);

        result.Status.Should().Be(EntraClientSecretStatus.Unavailable);
    }

    [Fact]
    public async Task Cancellation_is_not_a_resolution_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .Returns<SecretRef, CancellationToken>((_, ct) => ValueTask.FromException<ResolvedSecret>(new OperationCanceledException(ct)));

        var resolve = () => EntraClientSecretAccess.ResolveAsync(Registration(), resolver.Object, "from-config", cancelled.Token).AsTask();
        var tryResolve = () => EntraClientSecretAccess.TryResolveAsync(Registration(), resolver.Object, "from-config", cancelled.Token);

        await resolve.Should().ThrowAsync<OperationCanceledException>();
        await tryResolve.Should().ThrowAsync<OperationCanceledException>("a cancelled request must not quietly fall through to the deployment secret");
    }

    [Theory]
    [InlineData(true, "from-config", EntraClientSecretStatus.Resolved, EntraClientSecretSource.DeploymentSecret)]
    [InlineData(true, null, EntraClientSecretStatus.Absent, EntraClientSecretSource.None)]
    [InlineData(false, "from-config", EntraClientSecretStatus.Absent, EntraClientSecretSource.None)]
    public async Task A_registration_with_no_stored_reference_resolves_only_through_the_deployment_secret_it_asks_for(
        bool usesDeploymentSecret, string? deployment, EntraClientSecretStatus status, EntraClientSecretSource source)
    {
        var result = await EntraClientSecretAccess.ResolveAsync(Registration(secretRef: null, usesDeploymentSecret), null, deployment);

        result.Status.Should().Be(status);
        result.Source.Should().Be(source);
    }

    [Fact]
    public async Task The_resolution_never_carries_a_reference_or_an_error_message()
    {
        var result = await EntraClientSecretAccess.ResolveAsync(
            Registration(usesDeploymentSecret: false), ResolverThrowing(new IOException("server=secret-host;password=hunter2")).Object, null);

        result.ToString().Should().NotContain(StoredRef).And.NotContain("hunter2").And.NotContain("secret-host");
    }
}
