namespace Apperture.Core.Domain.Governance;

/// <summary>
/// Declares a governed action key and its target resource type.
/// </summary>
public sealed record GovernedActionDescriptor(
    string ActionKey,
    string ResourceType,
    string ScopeKey,
    ActionCategory Category,
    bool IsPrivileged = false);

