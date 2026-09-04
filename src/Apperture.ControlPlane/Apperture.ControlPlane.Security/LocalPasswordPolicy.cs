using Apperture.Core.Domain.Identity;

namespace Apperture.ControlPlane.Security;

public static class LocalPasswordPolicy
{
    public static PasswordEvaluationContext ForUser(User user) =>
        new(
            Username: user.IdentityLinks.FirstOrDefault(l => l.Source == IdentitySource.Local)?.ExternalId,
            DisplayName: user.DisplayName,
            Email: user.EmailAddress ?? user.PendingEmailAddress);

    public static PasswordEvaluationContext ForLocalIdentity(string? username, string? displayName = null, string? email = null) =>
        new(Username: username, DisplayName: displayName, Email: email);

    public static void Require(string? password, PasswordEvaluationContext? context = null) =>
        PasswordPolicyEvaluator.EnsureAcceptable(password, context);

    public static void Require(string? password, User user) =>
        Require(password, ForUser(user));
}
