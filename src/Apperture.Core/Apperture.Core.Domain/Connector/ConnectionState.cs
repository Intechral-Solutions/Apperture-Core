namespace Apperture.Core.Domain.Connector;

/// <summary>
/// Lifecycle state of a connection. Retired connections are excluded from binding eligibility.
/// </summary>
public enum ConnectionState
{
    Active,
    Retired
}
