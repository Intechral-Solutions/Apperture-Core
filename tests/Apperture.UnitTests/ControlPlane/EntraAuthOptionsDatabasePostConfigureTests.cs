using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class EntraAuthOptionsDatabasePostConfigureTests
{
    [Fact]
    public void PostConfigure_WhenActiveRegistrationHasNoSecret_DoesNotEnableEntra()
    {
        var repo = new Mock<IEntraInstanceRegistrationRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntraInstanceRegistration
            {
                Id = "reg-1",
                Enabled = true,
                Authority = "https://login.microsoftonline.com/tenant",
                ClientId = "client",
                UsesDeploymentSecret = true
            });

        var sut = CreateSut(repo.Object, secretResolver: null);
        var options = new EntraAuthOptions { Enabled = false, ClientSecret = "" };

        sut.PostConfigure(Options.DefaultName, options);

        options.Enabled.Should().BeFalse();
        options.Authority.Should().Be("https://login.microsoftonline.com/tenant");
        options.ClientId.Should().Be("client");
    }

    [Fact]
    public void PostConfigure_WhenStoredSecretResolves_EnablesEntraWithThatSecret()
    {
        var repo = new Mock<IEntraInstanceRegistrationRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntraInstanceRegistration
            {
                Id = "reg-1",
                Enabled = true,
                Authority = "https://login.microsoftonline.com/tenant",
                ClientId = "client",
                ClientSecretRef = "stored-id",
                UsesDeploymentSecret = true
            });

        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.Is<SecretRef>(s => s.Value == "stored-id"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSecret(
                new SecretRef("stored-id"),
                new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "from-store"),
                Version: "1"));

        var sut = CreateSut(repo.Object, resolver.Object);
        var options = new EntraAuthOptions { Enabled = false, ClientSecret = "from-config" };

        sut.PostConfigure(Options.DefaultName, options);

        options.Enabled.Should().BeTrue();
        options.ClientSecret.Should().Be("from-store");
    }

    private static EntraAuthOptionsDatabasePostConfigure CreateSut(
        IEntraInstanceRegistrationRepository repo,
        ISecretResolver? secretResolver)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        if (secretResolver is not null)
            services.AddSingleton(secretResolver);
        var provider = services.BuildServiceProvider();
        return new EntraAuthOptionsDatabasePostConfigure(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EntraAuthOptionsDatabasePostConfigure>.Instance);
    }
}
