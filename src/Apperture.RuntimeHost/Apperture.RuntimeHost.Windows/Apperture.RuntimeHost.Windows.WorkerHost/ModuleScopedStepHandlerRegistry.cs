using Apperture.Core.Application.Execution;

namespace Apperture.RuntimeHost.Windows.WorkerHost;

/// <summary>
/// Guardrail registry wrapper for module-owned handler registration.
/// Enforces that a module registers handlers only for its own module id and prevents collisions.
/// </summary>
internal sealed class ModuleScopedStepHandlerRegistry : IStepHandlerRegistry
{
    private readonly string _moduleId;
    private readonly IStepHandlerRegistry _inner;
    private readonly HashSet<(string ModuleId, string StepName)> _seen;

    public ModuleScopedStepHandlerRegistry(string moduleId, IStepHandlerRegistry inner)
        : this(moduleId, inner, new HashSet<(string, string)>())
    {
    }

    private ModuleScopedStepHandlerRegistry(string moduleId, IStepHandlerRegistry inner, HashSet<(string, string)> seen)
    {
        _moduleId = moduleId ?? "";
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _seen = seen;
    }

    public void Register(string moduleId, string stepName, IStepHandler handler)
    {
        if (!string.Equals(moduleId ?? "", _moduleId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Module '{_moduleId}' attempted to register a handler for moduleId '{moduleId}'.");

        var normalizedStepName = stepName ?? "";
        var key = (_moduleId, normalizedStepName);
        if (!_seen.Add(key) || _inner.Get(_moduleId, normalizedStepName) is not null)
            throw new InvalidOperationException($"Duplicate step handler registration for '{_moduleId}.{stepName}'.");

        _inner.Register(_moduleId, normalizedStepName, handler);
    }

    public IStepHandler? Get(string moduleId, string stepName) => _inner.Get(moduleId, stepName);
}

