using System.Data;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Data.SqlClient;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Creates IDbConnection (SqlConnection) from a connection and resolved material for the platform SQL connector.
/// Reads Server, Database, optional UserId from config and Password (or equivalent) from resolved material.
/// </summary>
public sealed class PlatformSqlConnectorClientFactory : IConnectorClientFactory<IDbConnection>
{
    private const string ServerKey = "Server";
    private const string DatabaseKey = "Database";
    private const string UserIdKey = "UserId";
    private const string PasswordKey = "Password";

    /// <inheritdoc />
    public IDbConnection Create(Connection connection, ResolvedConnectionMaterial material)
    {
        var dict = material.ConfigAndResolvedSecrets;
        var server = GetRequired(dict, ServerKey);
        var database = GetRequired(dict, DatabaseKey);
        var userId = dict.TryGetValue(UserIdKey, out var u) ? u : null;
        var password = dict.TryGetValue(PasswordKey, out var p) ? p : null;

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = string.IsNullOrEmpty(userId),
        };
        if (!string.IsNullOrEmpty(userId))
        {
            builder.UserID = userId;
            builder.Password = password ?? "";
        }

        return new SqlConnection(builder.ConnectionString);
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> dict, string key)
    {
        if (dict.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        throw new InvalidOperationException($"Platform SQL connection requires '{key}' in config or resolved material.");
    }
}
