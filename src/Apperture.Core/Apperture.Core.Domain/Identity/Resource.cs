using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Declarative resource that can be protected (e.g. "Connector:X", "Workflow:Y"). Value object / entity.
/// </summary>
public sealed record Resource(
    string ResourceType,
    string ScopeKey,
    string? ModuleId);
