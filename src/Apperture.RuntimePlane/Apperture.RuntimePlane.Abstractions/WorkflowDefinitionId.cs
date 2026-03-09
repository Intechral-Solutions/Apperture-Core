using Apperture.Shared.Kernel;

namespace Apperture.RuntimePlane.Abstractions;

public sealed record WorkflowDefinitionId(string Value) : EntityId(Value);
