namespace Apperture.Core.Domain.Trust;

/// <summary>
/// Business topology of a trust relationship.
/// </summary>
public enum RelationshipType
{
    ParentChild,
    Observer,
    Peer,
    ServiceProvider,
    Auditor
}
