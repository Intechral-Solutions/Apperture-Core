using System.Text;
using System.Text.Json;
using Apperture.Connectors.CampusNexus.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apperture.Connectors.CampusNexus;

public sealed class CampusNexusApiClient : ICampusNexusApiClient
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ILogger<CampusNexusApiClient> _logger;
    private bool _disposed;

    public CampusNexusApiClient(HttpClient http, ILogger<CampusNexusApiClient>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? NullLogger<CampusNexusApiClient>.Instance;
    }

    public async Task<ApiCommandResponse> ExecuteCommandAsync(ApiCommand apiCommand, CancellationToken cancellationToken = default)
    {
        string requestUri = $"api/commands/{apiCommand.Module}/{apiCommand.EntityName}/{apiCommand.Operation}";
        _logger.LogDebug("CNS command {Uri}", requestUri);

        using var content = new StringContent(apiCommand.Payload, Encoding.UTF8, "application/json");
        using HttpResponseMessage httpResponse = await _http.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);
        string messageContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        using JsonDocument jsonDoc = JsonDocument.Parse(messageContent);

        if (!httpResponse.IsSuccessStatusCode)
            throw new CampusNexusApiException(ExtractCommandError(jsonDoc));

        if (!jsonDoc.RootElement.TryGetProperty("payload", out _))
            throw new CampusNexusApiException("Invalid or unexpected command response (missing payload).");

        return jsonDoc.RootElement.Deserialize<ApiCommandResponse>(ReadOptions)
               ?? throw new CampusNexusApiException("Could not deserialize command response.");
    }

    public async Task<T?> ExecuteCommandAsync<T>(ApiCommand apiCommand, CancellationToken cancellationToken = default)
    {
        ApiCommandResponse response = await ExecuteCommandAsync(apiCommand, cancellationToken).ConfigureAwait(false);
        return response.Deserialize<T>();
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(ApiQuery apiQuery, CancellationToken cancellationToken = default)
    {
        string requestUri = $"ds/campusnexus/{apiQuery.EntityCollectionName}?{AppendFieldSelectorToQuery(apiQuery.ODataQuery, apiQuery.SelectField)}";
        _logger.LogDebug("CNS query {Uri}", requestUri);

        using HttpResponseMessage httpResponse = await _http.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        string messageContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument jsonDoc = JsonDocument.Parse(messageContent);

        if (!httpResponse.IsSuccessStatusCode)
            throw new CampusNexusApiException(ExtractQueryError(jsonDoc));

        if (!jsonDoc.RootElement.TryGetProperty("value", out JsonElement valueElement) || valueElement.ValueKind != JsonValueKind.Array)
            throw new CampusNexusApiException("Query result does not contain a 'value' array.");

        var list = new List<T>();
        foreach (JsonElement arrayElement in valueElement.EnumerateArray())
        {
            T? item = !string.IsNullOrEmpty(apiQuery.SelectField) && arrayElement.TryGetProperty(apiQuery.SelectField, out JsonElement selected)
                ? selected.Deserialize<T>(ReadOptions)
                : arrayElement.Deserialize<T>(ReadOptions);
            if (item is not null)
                list.Add(item);
        }

        return list;
    }

    public async Task<T?> QueryFirstAsync<T>(ApiQuery apiQuery, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<T> rows = await QueryAsync<T>(apiQuery, cancellationToken).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : default;
    }

    public string ColumnInFilter(string columnName, IEnumerable<string> values) =>
        string.Join(" or ", values.Select(v => $"{columnName} eq {QuoteODataValue(v)}"));

    public string ColumnInFilter(string columnName, string commaSeparatedValues) =>
        ColumnInFilter(columnName, commaSeparatedValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// GET request to the API base URL. Used by connection verification to validate network, TLS, and configured auth headers without calling a specific CNS resource.
    /// </summary>
    public async Task<(int StatusCode, string Summary)> ProbeRootAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("", cancellationToken).ConfigureAwait(false);
        var code = (int)response.StatusCode;
        var requestUri = response.RequestMessage?.RequestUri?.ToString() ?? "";
        var summary = $"GET {requestUri}: HTTP {code} {response.ReasonPhrase ?? ""}".Trim();
        return (code, summary);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _http.Dispose();
    }

    private static string QuoteODataValue(string v)
    {
        if (int.TryParse(v, out _) || decimal.TryParse(v, out _))
            return v;
        return $"'{v.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static string ExtractCommandError(JsonDocument jsonDoc)
    {
        string message = "Command response resulted in an unspecified error.";

        if (jsonDoc.RootElement.TryGetProperty("notifications", out JsonElement notificationsElement) && notificationsElement.ValueKind == JsonValueKind.Array)
        {
            int n = notificationsElement.GetArrayLength();
            if (n > 0)
            {
                message = $"Command failed with {n} notification(s): ";
                message += string.Join(", ", notificationsElement.EnumerateArray().Select((el, i) =>
                    el.TryGetProperty("message", out JsonElement m) ? $"({i + 1}) {m}" : $"({i + 1})"));
            }
        }

        if (jsonDoc.RootElement.TryGetProperty("message", out JsonElement simpleMessage))
            message = $"Command failed: {simpleMessage}";

        return message;
    }

    private static string ExtractQueryError(JsonDocument jsonDoc)
    {
        if (jsonDoc.RootElement.TryGetProperty("error", out JsonElement errorElement) && errorElement.TryGetProperty("message", out JsonElement messageElement))
            return $"Query error: {messageElement}";
        return "Response resulted in an unspecified error.";
    }

    private static string AppendFieldSelectorToQuery(string oDataQuery, string? fieldToSelect)
    {
        if (fieldToSelect is not null && oDataQuery.IndexOf("$select", StringComparison.OrdinalIgnoreCase) < 0)
        {
            if (oDataQuery.Trim().Length > 0)
                oDataQuery += "&";
            oDataQuery += $"$select={Uri.EscapeDataString(fieldToSelect)}";
        }

        return oDataQuery;
    }
}
