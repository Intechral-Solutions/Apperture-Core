namespace Apperture.Shared.Kernel;

/// <summary>
/// Primitive descriptor for a permission (action + optional resource). No platform lifecycle.
/// </summary>
public sealed record PermissionDescriptor(string Action, ResourceDescriptor? Resource = null);
