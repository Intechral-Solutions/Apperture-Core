namespace Apperture.Core.Application.Execution;

/// <summary>
/// Registry of step handlers by (moduleId, stepName). The workflow executor looks up handlers to run steps.
/// </summary>
public interface IStepHandlerRegistry
{
    /// <summary>
    /// Registers a handler for the given module and step name.
    /// </summary>
    void Register(string moduleId, string stepName, IStepHandler handler);

    /// <summary>
    /// Gets the handler for the given module and step name, or null.
    /// </summary>
    IStepHandler? Get(string moduleId, string stepName);
}
