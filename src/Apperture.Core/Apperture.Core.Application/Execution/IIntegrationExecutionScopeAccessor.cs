namespace Apperture.Core.Application.Execution;

/// <summary>
/// Ambient integration execution scope for runtime adapters (settings/connection accessors).
/// Set by the workflow/operation executor for the duration of a step handler invocation.
/// </summary>
public interface IIntegrationExecutionScopeAccessor
{
    string? ModuleId { get; }

    string? IntegrationId { get; }

    IDisposable BeginScope(string moduleId, string? integrationId);
}
