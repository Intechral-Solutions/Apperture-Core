using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class ManagementUiUrlValidationTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Local_http_topologies_keep_the_localhost_fallback(string environmentName)
    {
        var environment = new StubEnvironment(environmentName);
        var act = () => ManagementUiRedirects.EnsurePublicOrigin(Empty(), environment);
        act.Should().NotThrow();
        ManagementUiRedirects.ResolveBaseUrl(Empty()).Should().Be("http://localhost:9001");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://localhost:9001")]
    [InlineData("https://localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("http://management.contoso.com")]
    [InlineData("https://management.contoso.com/app")]
    [InlineData("https://user:pass@management.contoso.com")]
    [InlineData("not-a-url")]
    public void Production_refuses_missing_loopback_or_non_https_origins(string? value)
    {
        var act = () => ManagementUiRedirects.EnsurePublicOrigin(With(value), new StubEnvironment("Production"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*ShellUi:ManagementUiUrl*");
    }

    [Fact]
    public void Production_accepts_a_public_https_origin()
    {
        var act = () => ManagementUiRedirects.EnsurePublicOrigin(
            With("https://management.contoso.com"),
            new StubEnvironment("Production"));
        act.Should().NotThrow();
    }

    [Fact]
    public void Staging_uses_the_same_public_origin_rule()
    {
        var act = () => ManagementUiRedirects.EnsurePublicOrigin(
            With("http://localhost:9001"),
            new StubEnvironment("Staging"));
        act.Should().Throw<InvalidOperationException>();
    }

    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private static IConfiguration With(string? value) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ManagementUiRedirects.BaseUrlConfigurationKey] = value,
        })
        .Build();

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
