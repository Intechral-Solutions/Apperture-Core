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

    [Fact]
    public void GetReturnUrlRaw_ParsesFirstReturnUrl()
    {
        var uri = "http://localhost:9001/login?returnUrl=%2Fdashboard&x=1";
        LoginRouteHelper.GetReturnUrlRaw(uri).Should().Be("/dashboard");
    }

    [Fact]
    public void GetReturnUrlRaw_decodes_exactly_once()
    {
        // %252F is an encoded '%' followed by "2F": one decode leaves the literal text "%2F", it is never turned into '/'.
        LoginRouteHelper.GetReturnUrlRaw("http://localhost:9001/login?returnUrl=%2Fruns%3Fq%3Da%252Fb")
            .Should().Be("/runs?q=a%2Fb");
    }

    // SEC-1A1: one builder for every automatic sign-in navigation (boot-time gate and expired-session recovery).
    [Theory]
    [InlineData("/connections", null, "/login?returnUrl=%2Fconnections")]
    [InlineData("/connections?status=active&context=all", null, "/login?returnUrl=%2Fconnections%3Fstatus%3Dactive%26context%3Dall")]
    [InlineData("/connections?status=active", "expired", "/login?returnUrl=%2Fconnections%3Fstatus%3Dactive&reason=expired")]
    [InlineData("/", null, "/login?returnUrl=%2F")]
    // SEC-1A2a: the fragment travels inside the escaped value.
    [InlineData("/connections/abc?x=1#notes", "expired", "/login?returnUrl=%2Fconnections%2Fabc%3Fx%3D1%23notes&reason=expired")]
    [InlineData("/connections/a%2Fb", null, "/login?returnUrl=%2Fconnections%2Fa%252Fb")]
    public void BuildLoginUrl_uses_the_existing_returnUrl_form_and_appends_the_reason(string target, string? reason, string expected) =>
        LoginRouteHelper.BuildLoginUrl(target, reason).Should().Be(expected);

    [Theory]
    [InlineData("/\\evil.example")]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("/login?returnUrl=%2Fx")]
    [InlineData("")]
    [InlineData(null)]
    public void BuildLoginUrl_leaves_out_a_target_the_shared_policy_refuses(string? target)
    {
        LoginRouteHelper.BuildLoginUrl(target).Should().Be("/login");
        LoginRouteHelper.BuildLoginUrl(target, "expired").Should().Be("/login?reason=expired");
    }

    [Theory]
    [InlineData("/connections/abc?x=1&y=%2F")]
    [InlineData("/connections/abc?x=1#notes")]
    [InlineData("/runs?status=failed&q=a%20b%2Fc")]
    [InlineData("/connections/a%2Fb")]
    [InlineData("/components/connector/Apperture.Platform.Sql")]
    public void BuildLoginUrl_round_trips_through_ReadReturnTarget(string target)
    {
        var url = LoginRouteHelper.BuildLoginUrl(target, LoginRouteHelper.ExpiredReason);
        LoginRouteHelper.GetReturnUrlRaw("http://localhost:9001" + url).Should().Be(target);
        LoginRouteHelper.ReadReturnTarget("http://localhost:9001" + url).Should().Be(target);
    }

    [Theory]
    [InlineData("http://localhost:9001/login?returnUrl=%2F%5Cevil.example")]
    [InlineData("http://localhost:9001/login?returnUrl=%2F%09%2Fevil.example")]
    [InlineData("http://localhost:9001/login?returnUrl=%2Flogin%3FreturnUrl%3D%252F%255Cevil.example")]
    [InlineData("http://localhost:9001/login?returnUrl=https%3A%2F%2Fevil.example")]
    [InlineData("http://localhost:9001/login")]
    public void ReadReturnTarget_is_null_for_a_hostile_or_missing_target(string uri) =>
        LoginRouteHelper.ReadReturnTarget(uri).Should().BeNull();

    [Theory]
    [InlineData("/connections/abc", "returnUrl=%2Fconnections%2Fabc")]
    [InlineData("/connections/abc?x=1#n", "returnUrl=%2Fconnections%2Fabc%3Fx%3D1%23n")]
    [InlineData("/\\evil.example", "")]
    [InlineData(null, "")]
    public void BuildReturnQuery_only_carries_a_valid_target_between_the_sign_in_pages(string? target, string expected) =>
        LoginRouteHelper.BuildReturnQuery(target).Should().Be(expected);

    [Theory]
    [InlineData("http://localhost:9001/login", LoginErrorKind.None)]
    [InlineData("http://localhost:9001/login?returnUrl=%2Fx", LoginErrorKind.None)]
    [InlineData("http://localhost:9001/login?error=cancelled", LoginErrorKind.Cancelled)]
    [InlineData("http://localhost:9001/login?error=CANCELLED", LoginErrorKind.Cancelled)]
    [InlineData("http://localhost:9001/login?error=account_not_linked", LoginErrorKind.AccountNotLinked)]
    [InlineData("http://localhost:9001/login?error=oidc", LoginErrorKind.SignInFailed)]
    [InlineData("http://localhost:9001/login?error=tenant_mismatch", LoginErrorKind.SignInFailed)]
    [InlineData("http://localhost:9001/login?returnUrl=%2Fx&error=whatever", LoginErrorKind.SignInFailed)]
    [InlineData("http://localhost:9001/login?error=", LoginErrorKind.SignInFailed)]
    [InlineData("http://localhost:9001/login?error", LoginErrorKind.SignInFailed)]
    public void ReadLoginError_reads_only_fixed_codes_and_treats_anything_else_as_a_failure(string uri, LoginErrorKind expected) =>
        LoginRouteHelper.ReadLoginError(uri).Should().Be(expected);

    [Theory]
    [InlineData("http://localhost:9001/login?returnUrl=%2Fx&reason=expired", true)]
    [InlineData("http://localhost:9001/login?reason=EXPIRED", true)]
    [InlineData("http://localhost:9001/login?reason=other", false)]
    [InlineData("http://localhost:9001/login?returnUrl=%2Fx", false)]
    [InlineData("http://localhost:9001/login", false)]
    public void IsExpiredReason_reads_the_reason_query(string uri, bool expected) =>
        LoginRouteHelper.IsExpiredReason(uri).Should().Be(expected);
}
