using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraClientSecretAccessTests
{
    [Fact]
    public void IsConfigured_WhenStoredRefPresent_IsTrue()
    {
        EntraClientSecretAccess.IsConfigured("550e8400-e29b-41d4-a716-446655440000", usesDeploymentSecret: false, deploymentSecretPresent: false)
            .Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_WhenOnlyDeploymentSecret_IsTrue()
    {
        EntraClientSecretAccess.IsConfigured(null, usesDeploymentSecret: true, deploymentSecretPresent: true)
            .Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_WhenNeither_IsFalse()
    {
        EntraClientSecretAccess.IsConfigured(" ", usesDeploymentSecret: true, deploymentSecretPresent: false)
            .Should().BeFalse();
    }

    [Fact]
    public async Task TryResolveAsync_PrefersStoredSecretOverDeployment()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.Is<SecretRef>(s => s.Value == "stored-id"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(
                new SecretRef("stored-id"),
                new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "from-store"),
                Version: "1"));

        var registration = new EntraInstanceRegistration
        {
            ClientSecretRef = "stored-id",
            UsesDeploymentSecret = true
        };

        var value = await EntraClientSecretAccess.TryResolveAsync(
            registration,
            resolver.Object,
            deploymentSecret: "from-config");

        value.Should().Be("from-store");
    }

    [Fact]
    public async Task TryResolveAsync_FallsBackToDeploymentWhenStoreFails()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<SecretRef>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not found"));

        var registration = new EntraInstanceRegistration
        {
            ClientSecretRef = Guid.NewGuid().ToString("D"),
            UsesDeploymentSecret = true
        };

        var value = await EntraClientSecretAccess.TryResolveAsync(
            registration,
            resolver.Object,
            deploymentSecret: "from-config");

        value.Should().Be("from-config");
    }
}
