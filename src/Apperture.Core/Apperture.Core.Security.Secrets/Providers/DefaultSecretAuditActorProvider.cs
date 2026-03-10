using Apperture.Core.Security.Secrets.Abstractions;

namespace Apperture.Core.Security.Secrets.Providers;

/// <summary>
/// Default audit actor provider; returns "system" when no authenticated context is available.
/// </summary>
public sealed class DefaultSecretAuditActorProvider : ISecretAuditActorProvider
{
    /// <inheritdoc />
    public string GetActor() => "system";
}
