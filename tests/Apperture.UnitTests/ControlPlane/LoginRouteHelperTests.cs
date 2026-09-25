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

    // SEC-1A1: one builder for every automatic sign-in navigation (boot-time gate and expired-session recovery).
    [Theory]
    [InlineData("/connections", null, "/login?returnUrl=%2Fconnections")]
    [InlineData("/connections?status=active&context=all", null, "/login?returnUrl=%2Fconnections%3Fstatus%3Dactive%26context%3Dall")]
    [InlineData("/connections?status=active", "expired", "/login?returnUrl=%2Fconnections%3Fstatus%3Dactive&reason=expired")]
    [InlineData("/", null, "/login?returnUrl=%2F")]
    public void BuildLoginUrl_uses_the_existing_returnUrl_form_and_appends_the_reason(string target, string? reason, string expected) =>
        LoginRouteHelper.BuildLoginUrl(target, reason).Should().Be(expected);

    [Fact]
    public void BuildLoginUrl_round_trips_through_GetReturnUrlRaw()
    {
        var url = LoginRouteHelper.BuildLoginUrl("/connections/abc?x=1&y=%2F", LoginRouteHelper.ExpiredReason);
        LoginRouteHelper.GetReturnUrlRaw("http://localhost:9001" + url).Should().Be("/connections/abc?x=1&y=%2F");
    }

    [Theory]
    [InlineData("http://localhost:9001/login?returnUrl=%2Fx&reason=expired", true)]
    [InlineData("http://localhost:9001/login?reason=EXPIRED", true)]
    [InlineData("http://localhost:9001/login?reason=other", false)]
    [InlineData("http://localhost:9001/login?returnUrl=%2Fx", false)]
    [InlineData("http://localhost:9001/login", false)]
    public void IsExpiredReason_reads_the_reason_query(string uri, bool expected) =>
        LoginRouteHelper.IsExpiredReason(uri).Should().Be(expected);
}
