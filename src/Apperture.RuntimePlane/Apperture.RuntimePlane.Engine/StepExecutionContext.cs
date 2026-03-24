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
    private readonly string? _integrationId;

    public StepExecutionContext(IRuntimeSlotResolver resolver, string moduleId, string? integrationId = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _moduleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
        _integrationId = integrationId;
    }

    /// <inheritdoc />
    public string ModuleId => _moduleId;

    /// <inheritdoc />
    public string? IntegrationId => _integrationId;

    /// <inheritdoc />
    public ValueTask<T?> ResolveSlotAsync<T>(string slotName, CancellationToken cancellationToken = default) where T : class =>
        _resolver.ResolveSlotAsync<T>(_moduleId, slotName, _integrationId, cancellationToken);
}
