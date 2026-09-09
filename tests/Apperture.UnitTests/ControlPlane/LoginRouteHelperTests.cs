using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class LoginRouteHelperTests
{
    [Theory]
    [InlineData("/login", true)]
    [InlineData("/login/", true)]
    [InlineData("/login/local", true)]
    [InlineData("/account/verify-email", true)]
    [InlineData("/account/setup-password", true)]
    [InlineData("/account/reset-password", true)]
    [InlineData("/account/forgot-password", true)]
    [InlineData("/access-request", true)]
    [InlineData("/account/profile", false)]
    [InlineData("/dashboard", false)]
    public void IsPublicPath_Classifies(string path, bool expected) =>
        LoginRouteHelper.IsPublicPath(path).Should().Be(expected);

    [Theory]
    [InlineData("/ok", "/ok")]
    [InlineData(null, null)]
    [InlineData("https://evil.com/x", null)]
    [InlineData("//evil", null)]
    public void NormalizeReturnUrl_RejectsOpenRedirects(string? input, string? expected) =>
        LoginRouteHelper.NormalizeReturnUrl(input).Should().Be(expected);

    [Fact]
    public void GetReturnUrlRaw_ParsesFirstReturnUrl()
    {
        var uri = "http://localhost:9001/login?returnUrl=%2Fdashboard&x=1";
        LoginRouteHelper.GetReturnUrlRaw(uri).Should().Be("/dashboard");
    }
}
