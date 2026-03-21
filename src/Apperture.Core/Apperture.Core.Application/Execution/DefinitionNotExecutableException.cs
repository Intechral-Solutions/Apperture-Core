namespace Apperture.Core.Application.Execution;

/// <summary>
/// Thrown when a workflow or operation definition cannot be executed on this host (e.g. unknown id or module not effectively active).
/// Used to distinguish lifecycle-gated failures for audit and operator messaging.
/// </summary>
public sealed class DefinitionNotExecutableException : InvalidOperationException
{
    public DefinitionNotExecutableException(string definitionId, ExecutionDefinitionKind kind, string message)
        : base(message)
    {
        DefinitionId = definitionId;
        Kind = kind;
    }

    public string DefinitionId { get; }

    public ExecutionDefinitionKind Kind { get; }
}

public enum ExecutionDefinitionKind
{
    Workflow = 0,
    Operation = 1
}
