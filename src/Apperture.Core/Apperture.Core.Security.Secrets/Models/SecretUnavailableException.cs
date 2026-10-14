namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// The one refusal of an owned resolution (<c>ISecretResolver.ResolveOwnedAsync</c>). It is raised identically when the reference
/// is unreadable, names no secret, names a disabled or ineligible secret, or names a secret the holder does not own (another
/// provider, another owner or another field). Its message is fixed and carries nothing of the reference, the stored descriptor or
/// the owner, so which condition failed cannot be learned from it, and a foreign secret's existence is not confirmed.
/// </summary>
public sealed class SecretUnavailableException : InvalidOperationException
{
    public const string FixedMessage = "The secret could not be resolved.";

    public SecretUnavailableException()
        : base(FixedMessage)
    {
    }
}
