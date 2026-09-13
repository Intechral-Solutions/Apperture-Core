namespace Apperture.Core.Application.Execution;

/// <inheritdoc />
public sealed class IntegrationExecutionScopeAccessor : IIntegrationExecutionScopeAccessor
{
    private readonly AsyncLocal<Scope?> _current = new();

    /// <inheritdoc />
    public string? ModuleId => _current.Value?.ModuleId;

    /// <inheritdoc />
    public string? IntegrationId => _current.Value?.IntegrationId;

    /// <inheritdoc />
    public IDisposable BeginScope(string moduleId, string? integrationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        var previous = _current.Value;
        _current.Value = new Scope(moduleId, integrationId);
        return new ScopeHandle(this, previous);
    }

    private sealed record Scope(string ModuleId, string? IntegrationId);

    private sealed class ScopeHandle(IntegrationExecutionScopeAccessor owner, Scope? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            owner._current.Value = previous;
            _disposed = true;
        }
    }
}
