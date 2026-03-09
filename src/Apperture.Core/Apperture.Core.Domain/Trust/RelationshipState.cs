namespace Apperture.Core.Domain.Trust;

/// <summary>
/// Runtime trust enforcement state.
/// </summary>
public enum RelationshipState
{
    Pending,
    Active,
    Suspended,
    Revoked,
    Expired
}
