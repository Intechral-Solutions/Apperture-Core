using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Governance;

/// <summary>
/// Canonical authorization context used across API, application, query, and UI capability checks.
/// </summary>
public sealed record AuthorizationEvaluationContext(
    IActor Actor,
    PermissionDescriptor Permission,
    ResourceDescriptor? Resource,
    IGovernedResourceIdentity? ResourceIdentity = null);

