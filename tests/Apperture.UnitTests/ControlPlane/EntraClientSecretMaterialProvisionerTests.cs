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
    public async Task StoreNewAsync_CreatesActivatesAndReturnsGuid()
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
        var secretRef = await sut.StoreNewAsync("reg-1", "plaintext-secret");

        secretRef.Should().Be(secretId.Value.ToString("D"));
        store.Verify(s => s.ActivateVersionAsync(secretId, "v1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StoreNewAsync_never_touches_a_secret_a_registration_already_names()
    {
        // AUD-1.2: a replacement is a secret of its own. Adding and activating a version on the live secret would change the
        // credential the committed registration resolves before (or without) the registration change committing.
        var created = SecretId.New();
        var store = new Mock<ISecretStore>(MockBehavior.Strict);
        store.Setup(s => s.CreateAsync(It.IsAny<SecretCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecretDescriptor(
                created,
                EntraClientSecretMaterialProvisioner.BuildSecretName("reg-1"),
                EntraClientSecretMaterialProvisioner.SecretProvider,
                SecretMaterialType.OpaqueString,
                ActiveVersion: null,
                SecretState.Pending,
                DateTimeOffset.UtcNow));
        store.Setup(s => s.AddVersionAsync(created, It.IsAny<SecretMaterial>(), It.IsAny<CancellationToken>())).ReturnsAsync("v1");
        store.Setup(s => s.ActivateVersionAsync(created, "v1", It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        var sut = new EntraClientSecretMaterialProvisioner(store.Object);
        var secretRef = await sut.StoreNewAsync("reg-1", "rotated");

        secretRef.Should().Be(created.Value.ToString("D"));
        store.VerifyAll();
    }
}
