using Apperture.ControlPlane.Security;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraClientSecretMaterialProvisionerTests
{
    [Fact]
    public async Task ReplaceAsync_WhenNoExistingRef_CreatesActivatesAndReturnsGuid()
    {
        var secretId = SecretId.New();
        var store = new Mock<ISecretStore>();
        store.Setup(s => s.CreateAsync(It.IsAny<SecretCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretDescriptor(
                secretId,
                EntraClientSecretMaterialProvisioner.BuildSecretName("reg-1"),
                EntraClientSecretMaterialProvisioner.SecretProvider,
                SecretMaterialType.OpaqueString,
                ActiveVersion: null,
                SecretState.Pending,
                DateTimeOffset.UtcNow));
        store.Setup(s => s.AddVersionAsync(secretId, It.IsAny<SecretMaterial>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("v1");
        store.Setup(s => s.ActivateVersionAsync(secretId, "v1", It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new EntraClientSecretMaterialProvisioner(store.Object);
        var (secretRef, created) = await sut.ReplaceAsync("reg-1", "plaintext-secret", existingSecretRef: null);

        created.Should().BeTrue();
        secretRef.Should().Be(secretId.Value.ToString("D"));
        store.Verify(s => s.ActivateVersionAsync(secretId, "v1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplaceAsync_WhenExistingRefActive_AddsNewVersion()
    {
        var existing = Guid.NewGuid();
        var secretId = new SecretId(existing);
        var store = new Mock<ISecretStore>();
        store.Setup(s => s.GetAsync(secretId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretDescriptor(
                secretId,
                EntraClientSecretMaterialProvisioner.BuildSecretName("reg-1"),
                EntraClientSecretMaterialProvisioner.SecretProvider,
                SecretMaterialType.OpaqueString,
                ActiveVersion: "v1",
                SecretState.Active,
                DateTimeOffset.UtcNow));
        store.Setup(s => s.AddVersionAsync(secretId, It.IsAny<SecretMaterial>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("v2");
        store.Setup(s => s.ActivateVersionAsync(secretId, "v2", It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new EntraClientSecretMaterialProvisioner(store.Object);
        var (secretRef, created) = await sut.ReplaceAsync("reg-1", "rotated", existing.ToString("D"));

        created.Should().BeFalse();
        secretRef.Should().Be(existing.ToString("D"));
        store.Verify(s => s.CreateAsync(It.IsAny<SecretCreateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
