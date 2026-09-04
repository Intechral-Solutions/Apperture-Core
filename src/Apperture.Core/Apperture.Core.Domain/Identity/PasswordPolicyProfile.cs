namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Acceptance policy for a password-establishing path. Guessability is scored separately;
/// this type decides whether a given score and value may be stored.
/// </summary>
public sealed record PasswordPolicyProfile(
    string Name,
    int MinimumLength,
    int MaximumLength,
    int MinimumStrengthScore,
    bool BlockCommonPasswords,
    bool BlockContextualPasswords,
    IReadOnlyList<string>? AdditionalContextTerms = null)
{
    /// <summary>
    /// Ordinary local password-only authentication. NIST SP 800-63B-4 SHOULD of 15 characters,
    /// no composition rules, accept only when the estimator reports safely unguessable (score 3+).
    /// </summary>
    public static PasswordPolicyProfile Ordinary { get; } = new(
        Name: "Ordinary",
        MinimumLength: 15,
        MaximumLength: 128,
        MinimumStrengthScore: 3,
        BlockCommonPasswords: true,
        BlockContextualPasswords: true);

    /// <summary>
    /// Recovery Access (Emergency Access CLI/operator tooling). Stricter length because the
    /// password protects platform recovery even though native MFA is also required.
    /// </summary>
    public static PasswordPolicyProfile RecoveryAccess { get; } = new(
        Name: "RecoveryAccess",
        MinimumLength: 20,
        MaximumLength: 128,
        MinimumStrengthScore: 3,
        BlockCommonPasswords: true,
        BlockContextualPasswords: true,
        AdditionalContextTerms: ["Recovery Access", "breakglass", "emergency access"]);
}

/// <summary>
/// Named access to the built-in profiles. Constants remain for existing call sites.
/// </summary>
public static class PasswordPolicy
{
    public static PasswordPolicyProfile Ordinary => PasswordPolicyProfile.Ordinary;

    public static PasswordPolicyProfile RecoveryAccess => PasswordPolicyProfile.RecoveryAccess;

    public static int MinimumLength => Ordinary.MinimumLength;

    public static int MaximumLength => Ordinary.MaximumLength;

    public static int MinimumAcceptableScore => Ordinary.MinimumStrengthScore;
}
