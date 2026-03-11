using System.Collections.Concurrent;
using Apperture.Core.Application.Execution;

namespace Apperture.RuntimePlane.Engine;

/// <summary>
/// In-memory registry of step handlers by (moduleId, stepName).
/// </summary>
public sealed class StepHandlerRegistry : IStepHandlerRegistry
{
    private readonly ConcurrentDictionary<(string ModuleId, string StepName), IStepHandler> _handlers = new();

    /// <inheritdoc />
    public void Register(string moduleId, string stepName, IStepHandler handler)
    {
        if (handler is null)
            throw new ArgumentNullException(nameof(handler));
        _handlers[(moduleId ?? "", stepName ?? "")] = handler;
    }

    /// <inheritdoc />
    public IStepHandler? Get(string moduleId, string stepName) =>
        _handlers.TryGetValue((moduleId ?? "", stepName ?? ""), out var handler) ? handler : null;
}
