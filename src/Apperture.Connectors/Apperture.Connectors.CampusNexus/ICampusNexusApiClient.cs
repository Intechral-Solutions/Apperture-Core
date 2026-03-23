using Apperture.Connectors.CampusNexus.Models;

namespace Apperture.Connectors.CampusNexus;

/// <summary>
/// Campus Nexus (CNS) HTTP API: command POSTs and OData GETs. Implementations own <see cref="HttpClient"/> disposal.
/// </summary>
public interface ICampusNexusApiClient : IDisposable
{
    Task<ApiCommandResponse> ExecuteCommandAsync(ApiCommand apiCommand, CancellationToken cancellationToken = default);

    Task<T?> ExecuteCommandAsync<T>(ApiCommand apiCommand, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(ApiQuery apiQuery, CancellationToken cancellationToken = default);

    Task<T?> QueryFirstAsync<T>(ApiQuery apiQuery, CancellationToken cancellationToken = default);

    /// <summary>Builds OData filter fragment: (col eq v1) or (col eq v2) ...</summary>
    string ColumnInFilter(string columnName, IEnumerable<string> values);

    string ColumnInFilter(string columnName, string commaSeparatedValues);
}
