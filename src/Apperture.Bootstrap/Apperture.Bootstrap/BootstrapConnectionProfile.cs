using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Apperture.Bootstrap;

/// <summary>
/// Bootstrap connection profile using config-based credentials. Validates connection before use.
/// </summary>
public sealed class BootstrapConnectionProfile : IBootstrapConnectionProfile
{
    private readonly BootstrapSqlConfig _config;
    private readonly string? _defaultConnectionString;
    private string? _connectionString;

    public BootstrapConnectionProfile(IOptions<BootstrapSqlConfig> config, IConfiguration configuration)
    {
        _config = config.Value;
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection");
    }

    public string ConnectionString => _connectionString ??= BuildConnectionString();

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private string BuildConnectionString()
    {
        if (!string.IsNullOrEmpty(_config.Server) && !string.IsNullOrEmpty(_config.User) && !string.IsNullOrEmpty(_config.Password))
        {
            var sb = new SqlConnectionStringBuilder
            {
                DataSource = _config.Server,
                InitialCatalog = _config.Database ?? "Apperture",
                UserID = _config.User,
                Password = _config.Password,
                TrustServerCertificate = _config.TrustServerCertificate
            };
            return sb.ConnectionString;
        }

        return _defaultConnectionString
            ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";
    }
}
