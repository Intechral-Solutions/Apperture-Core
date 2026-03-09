using Apperture.Bootstrap.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Apperture.Bootstrap;

/// <summary>
/// Bootstrap connection profile. Prefers <see cref="IBootstrapCredentialResolver"/> when
/// <see cref="BootstrapCredentialReference"/> is configured. Falls back to config-based credentials
/// only in Development, with a security warning.
/// </summary>
public sealed class BootstrapConnectionProfile : IBootstrapConnectionProfile
{
    private readonly BootstrapSqlConfig _config;
    private readonly string? _defaultConnectionString;
    private readonly IBootstrapCredentialResolver _credentialResolver;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<BootstrapConnectionProfile> _logger;
    private string? _connectionString;

    public BootstrapConnectionProfile(
        IOptions<BootstrapSqlConfig> config,
        IConfiguration configuration,
        IBootstrapCredentialResolver credentialResolver,
        IHostEnvironment hostEnvironment,
        ILogger<BootstrapConnectionProfile> logger)
    {
        _config = config.Value;
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection");
        _credentialResolver = credentialResolver;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public string ConnectionString => _connectionString ??= BuildConnectionString();

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private string BuildConnectionString()
    {
        // 1. Prefer IBootstrapCredentialResolver when CredentialReference is configured (production path)
        if (_config.CredentialReference is { } credRef)
        {
            var resolved = _credentialResolver.TryResolveAsync(credRef).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(resolved))
                return resolved;
        }

        // 2. Config-based credentials: allowed only in Development
        if (HasConfigCredentials())
        {
            if (!_hostEnvironment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Bootstrap SQL credentials (User/Password or DefaultConnection) must not be used in Production. " +
                    "Configure CredentialReference with a secure source (Key Vault, Managed Identity, etc.) and " +
                    "register a non-stub IBootstrapCredentialResolver.");
            }

            _logger.LogWarning(
                "Bootstrap SQL credentials are being read from config (SqlServer.User/Password or ConnectionStrings.DefaultConnection). " +
                "This is allowed only in Development. For Production, use CredentialReference and IBootstrapCredentialResolver.");
            return BuildConnectionStringFromConfig();
        }

        // 3. LocalDB default: allowed only in Development
        if (_hostEnvironment.IsDevelopment())
        {
            _logger.LogWarning(
                "Using default LocalDB connection string. For Production, configure CredentialReference and IBootstrapCredentialResolver.");
            return _defaultConnectionString ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";
        }

        throw new InvalidOperationException(
            "Bootstrap SQL credentials are not configured. Either set SqlServer.CredentialReference and register " +
            "a non-stub IBootstrapCredentialResolver, or (Development only) set SqlServer.User/Password or ConnectionStrings.DefaultConnection.");
    }

    private bool HasConfigCredentials() =>
        (!string.IsNullOrEmpty(_config.User) && !string.IsNullOrEmpty(_config.Password)) ||
        !string.IsNullOrEmpty(_defaultConnectionString);

    private string BuildConnectionStringFromConfig()
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

        return _defaultConnectionString ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";
    }
}
