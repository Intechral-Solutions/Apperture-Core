using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// SEC-1A2a: when the sign-in page may send the operator straight to Microsoft. SEC-1.0 found the feature effectively dead
/// (options unknown at first render, F6/N2); it now runs once the options are known, and never on an error, on a recent
/// attempt, or on a bare visit.
/// </summary>
public sealed class LoginAutoRedirectPolicyTests
{
    private static bool Decide(
        bool optionsLoaded = true,
        bool autoRedirectEnabled = true,
        bool entraSignInEnabled = true,
        LoginErrorKind error = LoginErrorKind.None,
        bool arrivedFromApp = true,
        bool attemptedRecently = false) =>
        LoginAutoRedirectPolicy.ShouldRedirect(optionsLoaded, autoRedirectEnabled, entraSignInEnabled, error, arrivedFromApp, attemptedRecently);

    [Fact]
    public void Redirects_once_the_options_are_known_when_the_policy_is_on_and_the_app_sent_the_operator() =>
        Decide().Should().BeTrue();

    [Fact]
    public void Never_before_the_options_are_known() =>
        Decide(optionsLoaded: false).Should().BeFalse();

    [Fact]
    public void Never_when_the_operator_policy_or_entra_is_off()
    {
        Decide(autoRedirectEnabled: false).Should().BeFalse();
        Decide(entraSignInEnabled: false).Should().BeFalse();
    }

    [Theory]
    [InlineData(LoginErrorKind.Cancelled)]
    [InlineData(LoginErrorKind.SignInFailed)]
    [InlineData(LoginErrorKind.AccountNotLinked)]
    public void Never_when_the_url_carries_an_error(LoginErrorKind error) =>
        Decide(error: error).Should().BeFalse("a failed or cancelled attempt shows its panel; it does not start another");

    [Fact]
    public void Never_within_the_window_of_the_previous_automatic_attempt() =>
        Decide(attemptedRecently: true).Should().BeFalse("a return without an error must not become a loop");

    [Fact]
    public void Never_on_a_bare_visit_so_that_signing_out_lands_on_the_chooser() =>
        Decide(arrivedFromApp: false).Should().BeFalse();

    [Theory]
    [InlineData("http://localhost:9001/login", false)]
    [InlineData("http://localhost:9001/login?returnUrl=%2Fconnections", true)]
    [InlineData("http://localhost:9001/login?reason=expired", true)]
    [InlineData("http://localhost:9001/login?returnUrl=%2F%5Cevil.example", true)]
    [InlineData("http://localhost:9001/login?error=oidc", false)]
    public void ArrivedFromApp_is_the_presence_of_a_return_target_or_reason(string uri, bool expected) =>
        LoginRouteHelper.ArrivedFromApp(uri).Should().Be(expected);
}
