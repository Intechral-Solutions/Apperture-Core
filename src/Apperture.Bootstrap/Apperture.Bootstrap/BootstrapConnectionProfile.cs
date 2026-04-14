using System.Net.Sockets;
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
        // Development: SQL (e.g. Docker) is often not ready the instant compose reports "started".
        // A single OpenAsync fails intermittently; brief retries remove flaky dev startup without masking auth errors.
        const int maxAttempts = 30;
        var delay = TimeSpan.FromSeconds(1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var conn = new SqlConnection(ConnectionString);
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                if (attempt > 1)
                {
                    _logger.LogInformation("Bootstrap SQL connection opened on attempt {Attempt}.", attempt);
                }

                return;
            }
            catch (Exception ex) when (ShouldRetryBootstrapOpen(ex, attempt, maxAttempts))
            {
                _logger.LogWarning(
                    ex,
                    "Bootstrap SQL connection attempt {Attempt}/{Max} failed; retrying after {Delay}.",
                    attempt,
                    maxAttempts,
                    delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private bool ShouldRetryBootstrapOpen(Exception ex, int attempt, int maxAttempts)
    {
        if (attempt >= maxAttempts)
        {
            return false;
        }

        if (!_hostEnvironment.IsDevelopment())
        {
            return false;
        }

        return IsLikelyTransientSqlConnectivityFailure(ex);
    }

    /// <summary>
    /// True for transport / instance / "not ready" failures. False for typical login misconfiguration (fail fast).
    /// </summary>
    private static bool IsLikelyTransientSqlConnectivityFailure(Exception ex)
    {
        switch (ex)
        {
            case TimeoutException:
                return true;
            case IOException:
                return true;
            case SocketException:
                return true;
            case SqlException sqlEx:
                // Login failures — do not spin.
                if (sqlEx.Number is 18456 or 18487)
                {
                    return false;
                }

                // Common transient / connectivity codes (overlap with SqlClient retry guidance).
                return sqlEx.Number is -2
                    or 20
                    or 53
                    or 64
                    or 233
                    or 258
                    or 10053
                    or 10054
                    or 10060
                    or 10061
                    or 40197
                    or 40501
                    or 4060
                    or 40613
                    or 49918
                    or 49919
                    or 49920;
            default:
                return false;
        }
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

        // 2.5 Dev-only hardening:
        // Sometimes the dev scripts pass the SQL password via environment variables, but config binding may
        // not populate BootstrapSqlConfig.Password (e.g., host builder differences). In that case, if we
        // already have SqlServer.Server + SqlServer.User from appsettings.Development.json, allow the
        // password to come from environment variables directly.
        if (_hostEnvironment.IsDevelopment())
        {
            var envPassword =
                Environment.GetEnvironmentVariable("SqlServer__Password") ??
                Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");

            if (!string.IsNullOrWhiteSpace(envPassword) &&
                !string.IsNullOrWhiteSpace(_config.Server) &&
                !string.IsNullOrWhiteSpace(_config.User))
            {
                _logger.LogWarning(
                    "Bootstrap SQL password is being read from environment variables (SqlServer__Password/MSSQL_SA_PASSWORD) in Development.");

                return new SqlConnectionStringBuilder
                {
                    DataSource = _config.Server,
                    InitialCatalog = _config.Database ?? "Apperture",
                    UserID = _config.User,
                    Password = envPassword,
                    TrustServerCertificate = _config.TrustServerCertificate
                }.ConnectionString;
            }
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
