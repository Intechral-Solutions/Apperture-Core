using Apperture.Shared.Kernel;

namespace Apperture.Runtime.Abstractions;

public sealed record WorkflowDefinitionId(string Value) : EntityId(Value);
