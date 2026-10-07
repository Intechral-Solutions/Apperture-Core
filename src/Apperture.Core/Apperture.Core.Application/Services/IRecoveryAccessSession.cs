namespace Apperture.Core.Application.Services;

/// <summary>
/// Implemented by an actor whose session was established through Recovery Access (the break-glass login). The audit
/// recorder reads it from the authenticated principal to mark the rows of such a session <c>viaRecoveryAccess</c>
/// (AUD-1 design gate §22, AUD-1.2). It is never read from a caller's input.
/// </summary>
public interface IRecoveryAccessSession
{
    /// <summary>True when the session was established through Recovery Access.</summary>
    bool IsRecoveryAccessSession { get; }
}
