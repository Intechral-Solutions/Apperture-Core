using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Installable capability type (metadata from manifest). Aggregate root / catalog entry.
/// </summary>
public sealed record Module(
    ModuleId Id,
    string Name,
    string Version,
    string Description,
    IReadOnlyList<ResourceDescriptor> ResourceDescriptors,
    IReadOnlyList<PermissionDescriptor> PermissionDescriptors);
