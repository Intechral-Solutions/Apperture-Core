namespace Apperture.RuntimePlane.Abstractions;

public sealed record WorkflowStepInstance(
    string StepName,
    int Attempt,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Error);
