namespace Apperture.Core.Domain.Governance;

/// <summary>
/// High-level action categories for permission classification.
/// </summary>
public enum ActionCategory
{
    Create,
    Read,
    Update,
    Delete,
    Execute,
    Manage,
    Bind
}
