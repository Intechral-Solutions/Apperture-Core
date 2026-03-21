namespace Apperture.ModuleSdk.Abstractions;

public sealed record ModuleDescriptor(
    string ModuleId,
    string DisplayName,
    string Version,
    string? Description,
    string? Publisher,
    string? MinimumPlatformVersion,
    IReadOnlyList<ModuleContributionDescriptor> Contributions);
