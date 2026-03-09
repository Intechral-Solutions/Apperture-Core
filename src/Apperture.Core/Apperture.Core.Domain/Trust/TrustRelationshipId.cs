using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Trust;

/// <summary>
/// Unique identifier for a Trust Relationship between two Environments.
/// </summary>
public sealed record TrustRelationshipId(string Value) : EntityId(Value);
