using Apperture.Management.Host;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class EntraStepUpFlowConstantsTests
{
    private static IConfiguration Config() => new ConfigurationBuilder().Build();

    [Fact]
    public void Safe_local_return_path_is_preserved()
    {
        var properties = EntraStepUpFlowConstants.CreateChallengeProperties(
            Config(),
            "usr-1",
            "c1",
            "/config/governance/users/usr-1",
            "Local");

        properties.RedirectUri.Should().Be("http://localhost:9001/config/governance/users/usr-1?steppedUp=1");
        EntraStepUpFlowConstants.IsStepUpPurpose(properties).Should().BeTrue();
        EntraStepUpFlowConstants.GetRequestedContextId(properties).Should().Be("c1");
        EntraLinkFlowConstants.GetInitiatingUserId(properties).Should().Be("usr-1");
        EntraStepUpFlowConstants.GetSessionSource(properties).Should().Be("Local");
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example")]
    [InlineData("/signin")]
    [InlineData("/signin/step-up")]
    [InlineData("/signin-oidc")]
    public void Unsafe_or_reserved_return_paths_are_replaced(string returnUrl)
    {
        var properties = EntraStepUpFlowConstants.CreateChallengeProperties(Config(), "usr-1", "c1", returnUrl, "Local");

        properties.RedirectUri.Should().StartWith("http://localhost:9001/account?");
        properties.RedirectUri.Should().NotContain("evil");
    }
}
