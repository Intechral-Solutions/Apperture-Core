using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;

namespace Apperture.RuntimePlane.Engine;

/// <summary>
/// Step execution context that delegates slot resolution to IRuntimeSlotResolver with a fixed module id.
/// </summary>
public sealed class StepExecutionContext : IStepExecutionContext
{
    private readonly IRuntimeSlotResolver _resolver;
    private readonly string _moduleId;

    public StepExecutionContext(IRuntimeSlotResolver resolver, string moduleId)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _moduleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
    }

    /// <inheritdoc />
    public string ModuleId => _moduleId;

    /// <inheritdoc />
    public ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default) where T : class =>
        _resolver.ResolveSlotAsync<T>(_moduleId, slotName, cancellationToken);
}
