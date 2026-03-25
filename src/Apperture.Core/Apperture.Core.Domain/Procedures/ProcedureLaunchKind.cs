namespace Apperture.Core.Domain.Procedures;

/// <summary>
/// How a module-declared procedure is executed when launched from the management plane.
/// </summary>
public enum ProcedureLaunchKind
{
    Workflow = 0,
    Operation = 1
}
