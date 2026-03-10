namespace Apperture.Core.Security.Secrets.Abstractions;

/// <summary>
/// Provides the actor identity for secret audit records. Enables Control Plane to pass authenticated user/service identity.
/// </summary>
public interface ISecretAuditActorProvider
{
    /// <summary>
    /// Returns the current actor identifier (e.g. user principal name, service account name).
    /// </summary>
    string GetActor();
}
