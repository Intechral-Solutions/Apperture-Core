using System.Net;

namespace Apperture.Management.Ui.Shared.Auth;

/// <summary>
/// What the local sign-in form says when a step fails. A refusal (401) means the credentials or the code were not accepted.
/// Anything else means the sign-in could not be completed, possibly after the server accepted what was entered: in the MFA
/// step a recovery code may already be used up (it stays consumed if the sign-in is not recorded), so the message never
/// invites entering that code again and never says nothing happened.
/// </summary>
public static class LocalSignInMessages
{
    public const string PasswordRejected = "Sign-in failed. Check your username and password.";

    public const string CodeRejected = "Verification failed. Check the authenticator or recovery code.";

    public const string SignInNotCompleted = "Sign-in could not be completed. Try again in a moment.";

    public const string MfaNotCompleted =
        "Sign-in could not be completed. A recovery code you entered may now be used up, so continue with your authenticator or a different recovery code.";

    /// <summary>The message for a failed step; <paramref name="status"/> is null when no response arrived.</summary>
    public static string ForFailure(bool mfaStep, HttpStatusCode? status) =>
        IsRefusal(status)
            ? mfaStep ? CodeRejected : PasswordRejected
            : mfaStep ? MfaNotCompleted : SignInNotCompleted;

    /// <summary>
    /// True when an entered recovery code must be cleared rather than offered again: the MFA step failed other than by a
    /// refusal, so the code may have been accepted and used up.
    /// </summary>
    public static bool ClearsRecoveryCode(bool mfaStep, HttpStatusCode? status) => mfaStep && !IsRefusal(status);

    private static bool IsRefusal(HttpStatusCode? status) => status == HttpStatusCode.Unauthorized;
}
