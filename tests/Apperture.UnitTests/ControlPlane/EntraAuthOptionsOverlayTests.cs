using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// IAM-10.2b WP3: the overlay of the active registration on the deployment's Entra options reports where the client secret it
/// settled on came from, and how far that can be relied on. What it does to the options is the production rule and is unchanged,
/// including the deployment-secret fallback the host has always applied (see <see cref="The_legacy_deployment_secret_fallback_is_unchanged_and_named"/>).
/// </summary>
public sealed class EntraAuthOptionsOverlayTests
{
    private const string StoredRef = "550e8400-e29b-41d4-a716-446655440000";

    private static EntraInstanceRegistration Active(string? secretRef, bool usesDeploymentSecret) =>
        new()
        {
            Id = "reg-1",
            Enabled = true,
            IsActive = true,
            Authority = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
            ClientId = "client",
            ClientSecretRef = secretRef,
            UsesDeploymentSecret = usesDeploymentSecret,
        };

    private static ISecretResolver Resolving(string value)
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(new SecretRef(StoredRef), new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: value), Version: "1"));
        return resolver.Object;
    }

    private static ISecretResolver Failing(Exception failure)
    {
        var resolver = new Mock<ISecretResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        return resolver.Object;
    }

    [Fact]
    public async Task A_stored_secret_enables_entra_and_is_reported_as_the_stored_secret()
    {
        var options = new EntraAuthOptions { ClientSecret = "from-config" };

        var result = await EntraAuthOptionsOverlay.ApplyDetailedAsync(Active(StoredRef, true), options, Resolving("from-store"));

        result.Outcome.Should().Be(EntraOverlayOutcome.Applied);
        result.SecretStatus.Should().Be(EntraClientSecretStatus.Resolved);
        result.Provenance.Should().Be(EntraSecretProvenance.StoredSecret);
        options.ClientSecret.Should().Be("from-store");
    }

    [Fact]
    public async Task The_deployment_secret_a_registration_asks_for_is_reported_as_requested()
    {
        var options = new EntraAuthOptions { ClientSecret = "from-config" };

        var result = await EntraAuthOptionsOverlay.ApplyDetailedAsync(Active(null, usesDeploymentSecret: true), options, null);

        result.Outcome.Should().Be(EntraOverlayOutcome.Applied);
        result.Provenance.Should().Be(EntraSecretProvenance.DeploymentSecretRequested);
    }

    [Fact]
    public async Task The_legacy_deployment_secret_fallback_is_unchanged_and_named()
    {
        // The host has always left the deployment's own client secret in place when the active registration neither asks for it nor
        // holds a secret, and then enabled Entra with it. That behavior is not changed here; it is reported so that nobody mistakes
        // the secret for the registration's own. The owner decision about removing it is recorded in the WP3 record.
        var options = new EntraAuthOptions { ClientSecret = "from-config" };

        var result = await EntraAuthOptionsOverlay.ApplyDetailedAsync(Active(null, usesDeploymentSecret: false), options, null);

        result.Outcome.Should().Be(EntraOverlayOutcome.Applied, "the production rule enables Entra with the deployment secret");
        options.Enabled.Should().BeTrue();
        options.ClientSecret.Should().Be("from-config");
        result.SecretStatus.Should().Be(EntraClientSecretStatus.Absent, "the registration itself has no secret");
        result.Provenance.Should().Be(EntraSecretProvenance.DeploymentSecretLegacyFallback);
    }

    [Fact]
    public async Task No_secret_anywhere_leaves_entra_off_and_the_registration_values_applied()
    {
        var options = new EntraAuthOptions { ClientSecret = "" };

        var result = await EntraAuthOptionsOverlay.ApplyDetailedAsync(Active(null, usesDeploymentSecret: true), options, null);

        result.Outcome.Should().Be(EntraOverlayOutcome.AppliedWithoutSecret);
        result.SecretStatus.Should().Be(EntraClientSecretStatus.Absent);
        result.Provenance.Should().Be(EntraSecretProvenance.None);
        options.Enabled.Should().BeFalse();
        options.ClientId.Should().Be("client");
    }

    [Fact]
    public async Task An_unreadable_store_changes_nothing_at_runtime_but_is_reported_as_unavailable()
    {
        var options = new EntraAuthOptions { ClientSecret = "from-config" };

        var result = await EntraAuthOptionsOverlay.ApplyDetailedAsync(Active(StoredRef, true), options, Failing(new IOException("down")));

        result.Outcome.Should().Be(EntraOverlayOutcome.Applied, "the sign-in has always fallen through to the deployment secret");
        options.ClientSecret.Should().Be("from-config");
        result.SecretStatus.Should().Be(EntraClientSecretStatus.Unavailable);
    }

    [Fact]
    public async Task A_confirmed_missing_store_secret_is_absent_not_unavailable()
    {
        var options = new EntraAuthOptions { ClientSecret = "" };

        var result = await EntraAuthOptionsOverlay.ApplyDetailedAsync(Active(StoredRef, false), options, Failing(new SecretNotFoundException()));

        result.Outcome.Should().Be(EntraOverlayOutcome.AppliedWithoutSecret);
        result.SecretStatus.Should().Be(EntraClientSecretStatus.Absent);
    }

    [Fact]
    public async Task With_no_active_registration_the_options_are_untouched()
    {
        var options = new EntraAuthOptions { ClientSecret = "from-config", ClientId = "deployment-client" };

        var result = await EntraAuthOptionsOverlay.ApplyDetailedAsync(null, options, null);

        result.Outcome.Should().Be(EntraOverlayOutcome.NoActiveRegistration);
        result.Provenance.Should().Be(EntraSecretProvenance.None);
        options.ClientId.Should().Be("deployment-client");
    }

    [Fact]
    public async Task ApplyAsync_keeps_returning_the_outcome_of_the_detailed_form()
    {
        var detailed = await EntraAuthOptionsOverlay.ApplyDetailedAsync(Active(StoredRef, true), new EntraAuthOptions(), Resolving("v"));
        var plain = await EntraAuthOptionsOverlay.ApplyAsync(Active(StoredRef, true), new EntraAuthOptions(), Resolving("v"));

        plain.Should().Be(detailed.Outcome);
    }
}
