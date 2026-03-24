using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Integration;

/// <summary>
/// Typed identifier for an Integration resource.
/// </summary>
public sealed record IntegrationId(string Value) : EntityId(Value);
