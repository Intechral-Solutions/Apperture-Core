using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Integration.Configuration;

namespace Apperture.RuntimePlane.Engine.Integration;

/// <inheritdoc />
public sealed class IntegrationConnectionAccessor : IIntegrationConnectionAccessor
{
    private readonly IIntegrationExecutionScopeAccessor _scope;
    private readonly IRuntimeSlotResolver _slotResolver;

    public IntegrationConnectionAccessor(
        IIntegrationExecutionScopeAccessor scope,
        IRuntimeSlotResolver slotResolver)
    {
        _scope = scope;
        _slotResolver = slotResolver;
    }

    /// <inheritdoc />
    public async ValueTask<T> GetRequiredAsync<T>(string connectionKey, CancellationToken cancellationToken = default)
        where T : class
    {
        var resolved = await TryGetAsync<T>(connectionKey, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
            throw IntegrationConfigurationException.MissingConnection(connectionKey);
        return resolved;
    }

    /// <inheritdoc />
    public async ValueTask<T?> TryGetAsync<T>(string connectionKey, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionKey);
        var moduleId = RequireModuleId();
        var integrationId = _scope.IntegrationId;
        return await _slotResolver
            .ResolveSlotAsync<T>(moduleId, connectionKey, integrationId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsBoundAsync(string connectionKey, CancellationToken cancellationToken = default)
    {
        var resolved = await TryGetAsync<object>(connectionKey, cancellationToken).ConfigureAwait(false);
        return resolved is not null;
    }

    private string RequireModuleId()
    {
        if (string.IsNullOrWhiteSpace(_scope.ModuleId))
            throw new InvalidOperationException("Integration execution scope is not available for connection access.");
        return _scope.ModuleId;
    }
}
