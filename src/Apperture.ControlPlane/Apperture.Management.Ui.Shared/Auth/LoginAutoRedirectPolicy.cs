namespace Apperture.Management.Ui.Shared.Auth;

/// <summary>
/// When the sign-in page may send the operator straight to Microsoft (the operator-configured "automatically redirect to
/// Microsoft sign-in" policy). Pure, so the rules are pinned by unit tests rather than by page timing.
/// <para>
/// It runs at most once per page load, and only after the sign-in options are actually known (the page renders before they
/// arrive, so a check made at first render sees nothing: SEC-1.0 finding N2). It never runs:
/// </para>
/// <list type="bullet">
/// <item>when the URL carries an <c>error</c> (a failed or cancelled attempt must show its panel, not start another);</item>
/// <item>within <see cref="AttemptWindow"/> of the previous automatic attempt in this tab (a return without an error, such as a
/// session cookie the browser refused, must not turn into a loop);</item>
/// <item>on a bare visit to the sign-in page. The application sends the operator with a <c>returnUrl</c> or <c>reason</c>
/// (an expired session, a protected route). A bare <c>/login</c> is where sign-out and "sign in with a different account"
/// land, and bouncing that straight back into the same Microsoft account would make signing out impossible.</item>
/// </list>
/// </summary>
public static class LoginAutoRedirectPolicy
{
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromSeconds(30);

    public static bool ShouldRedirect(
        bool optionsLoaded,
        bool autoRedirectEnabled,
        bool entraSignInEnabled,
        LoginErrorKind error,
        bool arrivedFromApp,
        bool attemptedRecently) =>
        optionsLoaded
        && autoRedirectEnabled
        && entraSignInEnabled
        && error == LoginErrorKind.None
        && arrivedFromApp
        && !attemptedRecently;
}
