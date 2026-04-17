using System.Data;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Microsoft.Data.SqlClient;

namespace Apperture.ConnectorResolution;

/// <summary>
/// Creates IDbConnection (SqlConnection) from a connection and resolved material for the platform SQL connector.
/// Reads Server, Database, optional UserId from config and Password (or equivalent) from resolved material.
/// When <c>TrustServerCertificate</c> is omitted, defaults to <c>true</c> so local/Docker SQL with self-signed certs
/// still works with modern SqlClient (which encrypts by default). Set <c>TrustServerCertificate=false</c> in
/// production when the server certificate chains to a trusted root.
/// </summary>
public sealed class PlatformSqlConnectorClientFactory : IConnectorClientFactory<IDbConnection>
{
    private const string ServerKey = "Server";
    private const string DatabaseKey = "Database";
    private const string UserIdKey = "UserId";
    private const string PasswordKey = "Password";
    private const string TrustServerCertificateKey = "TrustServerCertificate";
    private const string EncryptKey = "Encrypt";

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
            TrustServerCertificate = TryGetNullableBool(dict, TrustServerCertificateKey) ?? true,
        };
        if (!string.IsNullOrEmpty(userId))
        {
            builder.UserID = userId;
            builder.Password = password ?? "";
        }

        if (TryGetNullableBool(dict, EncryptKey) is { } encrypt)
            builder.Encrypt = encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional;

        return new SqlConnection(builder.ConnectionString);
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> dict, string key)
    {
        if (dict.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
        throw new InvalidOperationException($"Platform SQL connection requires '{key}' in config or resolved material.");
    }

    private static bool? TryGetNullableBool(IReadOnlyDictionary<string, string> dict, string key)
    {
        foreach (var kv in dict)
        {
            if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(kv.Value))
                return null;
            if (bool.TryParse(kv.Value, out var parsed))
                return parsed;
            if (string.Equals(kv.Value, "1", StringComparison.Ordinal))
                return true;
            if (string.Equals(kv.Value, "0", StringComparison.Ordinal))
                return false;
        }

        return null;
    }
}
