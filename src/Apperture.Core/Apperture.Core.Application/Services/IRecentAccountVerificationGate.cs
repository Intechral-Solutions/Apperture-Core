namespace Apperture.Core.Application.Services;

/// <summary>
/// Short-lived proof that the current session recently verified the account holder.
/// Satisfied by recent local-password reauthentication or by recent strong authentication
/// (Entra step-up / native MFA). Password-only proof does <em>not</em> satisfy
/// <see cref="IStrongAuthenticationGate"/>.
/// </summary>
public interface IRecentAccountVerificationGate
{
    RecentAccountVerification GetCurrent();

    /// <summary>
    /// Throws <see cref="RecentAccountVerificationException"/> when the session has no
    /// unexpired account verification.
    /// </summary>
    void Require();
}

public sealed record RecentAccountVerification(
    string? Mechanism,
    DateTimeOffset? EstablishedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsSatisfied)
{
    public static RecentAccountVerification None { get; } = new(null, null, null, false);
}

public sealed class RecentAccountVerificationException : InvalidOperationException
{
    public RecentAccountVerificationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public static class RecentAccountVerificationErrorCodes
{
    public const string Required = "auth.account_verification.required";
    public const string InvalidPassword = "auth.account_verification.invalid_password";
    public const string LocalPasswordUnavailable = "auth.account_verification.local_password_unavailable";
}

public static class RecentAccountVerificationMechanisms
{
    public const string LocalPassword = "local-password";
    public const string EntraAuthenticationContext = "entra-authentication-context";
    public const string NativeTotp = "native-totp";
    public const string NativeRecoveryCode = "native-recovery-code";
}

public interface IRecentAccountVerificationRecorder
{
    Task RecordAsync(string mechanism, CancellationToken cancellationToken = default);
}
