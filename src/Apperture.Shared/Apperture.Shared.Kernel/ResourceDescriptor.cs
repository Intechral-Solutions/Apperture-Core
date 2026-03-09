namespace Apperture.Shared.Kernel;

/// <summary>
/// Primitive descriptor for a protectable resource (type + scope). No platform lifecycle.
/// </summary>
public sealed record ResourceDescriptor(string ResourceType, string ScopeKey);
