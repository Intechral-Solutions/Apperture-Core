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

    [Fact]
    public async Task TryDisableOwnedSecretAsync_reports_true_only_when_it_disabled_the_registrations_own_usable_secret()
    {
        // AUD-1.4: the caller stages a Secrets-category row only for a retirement that happened.
        var owned = SecretId.New();
        var store = new Mock<ISecretStore>(MockBehavior.Strict);
        store.Setup(s => s.GetAsync(owned, It.IsAny<CancellationToken>())).ReturnsAsync(Descriptor(owned, "reg-1", SecretState.Active));
        store.Setup(s => s.DisableAsync(owned, It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        var sut = new EntraClientSecretMaterialProvisioner(store.Object);
        (await sut.TryDisableOwnedSecretAsync("reg-1", owned.Value.ToString("D"))).Should().BeTrue();
        store.Verify(s => s.DisableAsync(owned, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("retired")]
    [InlineData("no-ref")]
    public async Task TryDisableOwnedSecretAsync_reports_false_and_disables_nothing_otherwise(string situation)
    {
        var id = SecretId.New();
        var store = new Mock<ISecretStore>(MockBehavior.Strict);
        store.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(situation switch
        {
            "missing" => null,
            "foreign" => Descriptor(id, "another-registration", SecretState.Active),
            _ => Descriptor(id, "reg-1", SecretState.Disabled),
        });

        var sut = new EntraClientSecretMaterialProvisioner(store.Object);
        (await sut.TryDisableOwnedSecretAsync("reg-1", situation == "no-ref" ? null : id.Value.ToString("D"))).Should().BeFalse();
        store.Verify(s => s.DisableAsync(It.IsAny<SecretId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static SecretDescriptor Descriptor(SecretId id, string registrationId, SecretState state) =>
        new(
            id,
            EntraClientSecretMaterialProvisioner.BuildSecretName(registrationId),
            EntraClientSecretMaterialProvisioner.SecretProvider,
            SecretMaterialType.OpaqueString,
            ActiveVersion: "v1",
            state,
            DateTimeOffset.UtcNow);
}
