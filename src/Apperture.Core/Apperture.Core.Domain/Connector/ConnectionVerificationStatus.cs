namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Last observed connection verification outcome.
/// </summary>
public enum ConnectionVerificationStatus
{
    NeverTested = 0,
    Success = 1,
    Failed = 2,
    Unavailable = 3
}
