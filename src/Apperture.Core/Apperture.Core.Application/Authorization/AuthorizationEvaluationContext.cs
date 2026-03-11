using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Authorization;

/// <summary>
/// Lightweight context for authorization evaluation results. Used for future audit of allow/deny decisions.
/// Phase 3: define only; do not yet emit to IAuditService.
/// </summary>
public sealed record AuthorizationEvaluationContext(
    ActorId ActorId,
    PermissionDescriptor Permission,
    ResourceDescriptor? Resource,
    AuthorizationEvaluationResult Result,
    AuthorizationEvaluationPath EvaluationPath);

/// <summary>
/// Outcome of an authorization evaluation.
/// </summary>
public enum AuthorizationEvaluationResult
{
    Allow,
    Deny
}

/// <summary>
/// Path taken during evaluation. Per ADR-0012 order.
/// </summary>
public enum AuthorizationEvaluationPath
{
    BreakGlass,
    Relationship,
    Rbac,
    ResourceOwnership,
    None
}
