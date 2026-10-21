namespace Apperture.Core.Security.Secrets.Models;

/// <summary>
/// A resolver's answer that the reference names no secret it can serve: nothing is stored under it, or what is stored is disabled
/// or retired. It is the only resolution failure that means "confirmed absent". Every other failure (a store that cannot be
/// reached, a timeout, a key that cannot be unprotected) says nothing about whether the secret exists. It is an
/// <see cref="InvalidOperationException"/>, as the failure always was, so a caller that catches that type is unaffected, and its
/// message names neither the reference nor the secret.
/// </summary>
public sealed class SecretNotFoundException : InvalidOperationException
{
    public const string FixedMessage = "Secret could not be resolved (not found or ineligible).";

    public SecretNotFoundException()
        : base(FixedMessage)
    {
    }
}
