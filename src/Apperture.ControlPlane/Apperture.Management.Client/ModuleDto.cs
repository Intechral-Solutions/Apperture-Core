namespace Apperture.Management.Client;

public sealed record ModuleDto(
    string Id,
    string Name,
    string Version,
    string? Description,
    string State,
    bool Enabled,
    bool Installed,
    bool EffectivelyActive,
    IReadOnlyList<ModuleLifecycleBlockerDto>? ValidationIssues,
    IReadOnlyDictionary<string, int>? ContributionCounts);

public sealed record ModuleLifecycleBlockerDto(string Code, string Message);

public sealed record ModuleLifecycleActionResultDto(
    bool Succeeded,
    string ModuleId,
    string Action,
    string State,
    string Effect,
    IReadOnlyList<ModuleLifecycleBlockerDto> Blockers);
