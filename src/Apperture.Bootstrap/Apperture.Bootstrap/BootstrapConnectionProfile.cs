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
    private readonly IBootstrapConfigurationStore _configurationStore;
    private readonly IBootstrapCredentialResolver _credentialResolver;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<BootstrapConnectionProfile> _logger;
    private string? _connectionString;

    public BootstrapConnectionProfile(
        IOptions<BootstrapSqlConfig> config,
        IConfiguration configuration,
        IBootstrapConfigurationStore configurationStore,
        IBootstrapCredentialResolver credentialResolver,
        IHostEnvironment hostEnvironment,
        ILogger<BootstrapConnectionProfile> logger)
    {
        _config = config.Value;
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection");
        _configurationStore = configurationStore;
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
        // 1. Development orchestration: Aspire/scripts inject DefaultConnection — wins over file bootstrap store.
        if (!string.IsNullOrEmpty(_defaultConnectionString))
        {
            if (!_hostEnvironment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Bootstrap SQL credentials (User/Password or DefaultConnection) must not be used in Production. " +
                    "Configure CredentialReference with a secure source (Key Vault, Managed Identity, etc.) and " +
                    "register a non-stub IBootstrapCredentialResolver.");
            }

            _logger.LogInformation(
                "Bootstrap SQL credentials are being read from ConnectionStrings:DefaultConnection. " +
                "This is allowed only in Development. For Production, use CredentialReference and IBootstrapCredentialResolver.");
            return LogAndReturn(_defaultConnectionString, "ConnectionStrings:DefaultConnection");
        }

        // 2. File-backed bootstrap configuration (non-secret profile + credential reference).
        var fileDoc = _configurationStore.GetAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        if (fileDoc is not null)
        {
            if (fileDoc.CredentialReference is { } fileCredRef)
            {
                var resolved = _credentialResolver.TryResolveAsync(fileCredRef).ConfigureAwait(false).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(resolved))
                {
                    return LogAndReturn(resolved, "bootstrap-file:CredentialReference");
                }
            }

            if (TryBuildConnectionStringFromBootstrapProfile(fileDoc.Profile, out var fromFileProfile))
            {
                return LogAndReturn(fromFileProfile, "bootstrap-file:Profile");
            }
        }

        // 3. Prefer IBootstrapCredentialResolver when CredentialReference is configured in appsettings (production path)
        if (_config.CredentialReference is { } credRef)
        {
            var resolved = _credentialResolver.TryResolveAsync(credRef).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(resolved))
                return LogAndReturn(resolved, "CredentialReference");
        }

        // 4. Config-based SqlServer:User/Password (+ Server): allowed only in Development (script/docker-compose path).
        if (HasSqlServerConfigCredentials())
        {
            if (!_hostEnvironment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Bootstrap SQL credentials (User/Password or DefaultConnection) must not be used in Production. " +
                    "Configure CredentialReference with a secure source (Key Vault, Managed Identity, etc.) and " +
                    "register a non-stub IBootstrapCredentialResolver.");
            }

            _logger.LogWarning(
                "Bootstrap SQL credentials are being read from SqlServer:User/Password config. " +
                "This is allowed only in Development. For Production, use CredentialReference and IBootstrapCredentialResolver.");
            return LogAndReturn(BuildConnectionStringFromSqlServerConfig(), "SqlServer");
        }

        // 5. Dev-only hardening:
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

                return LogAndReturn(new SqlConnectionStringBuilder
                {
                    DataSource = _config.Server,
                    InitialCatalog = _config.Database ?? "Apperture",
                    UserID = _config.User,
                    Password = envPassword,
                    TrustServerCertificate = _config.TrustServerCertificate
                }.ConnectionString, "SqlServer+environment");
            }
        }

        // 6. LocalDB default: allowed only in Development
        if (_hostEnvironment.IsDevelopment())
        {
            _logger.LogWarning(
                "Using default LocalDB connection string. For Production, configure CredentialReference and IBootstrapCredentialResolver.");
            return LogAndReturn(
                "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;",
                "LocalDB-default");
        }

        throw new InvalidOperationException(
            "Bootstrap SQL credentials are not configured. Either set SqlServer.CredentialReference and register " +
            "a non-stub IBootstrapCredentialResolver, or (Development only) set SqlServer.User/Password or ConnectionStrings.DefaultConnection.");
    }

    private bool HasSqlServerConfigCredentials() =>
        !string.IsNullOrEmpty(_config.User) && !string.IsNullOrEmpty(_config.Password);

    private static bool TryBuildConnectionStringFromBootstrapProfile(
        SqlConnectionBootstrapProfile profile,
        out string connectionString)
    {
        connectionString = string.Empty;
        if (string.IsNullOrWhiteSpace(profile.Server) || string.IsNullOrWhiteSpace(profile.Database))
        {
            return false;
        }

        switch (profile.AuthMode)
        {
            case BootstrapAuthMode.WindowsIntegrated:
                connectionString = new SqlConnectionStringBuilder
                {
                    DataSource = profile.Server,
                    InitialCatalog = profile.Database,
                    IntegratedSecurity = true,
                    TrustServerCertificate = profile.TrustServerCertificate
                }.ConnectionString;
                return true;
            case BootstrapAuthMode.ManagedIdentity:
                connectionString = new SqlConnectionStringBuilder
                {
                    DataSource = profile.Server,
                    InitialCatalog = profile.Database,
                    TrustServerCertificate = profile.TrustServerCertificate
                }.ConnectionString;
                return true;
            case BootstrapAuthMode.Development:
                connectionString = new SqlConnectionStringBuilder
                {
                    DataSource = profile.Server,
                    InitialCatalog = profile.Database,
                    IntegratedSecurity = true,
                    TrustServerCertificate = profile.TrustServerCertificate
                }.ConnectionString;
                return true;
            default:
                return false;
        }
    }

    private string BuildConnectionStringFromSqlServerConfig()
    {
        if (!string.IsNullOrEmpty(_config.Server) && !string.IsNullOrEmpty(_config.User) && !string.IsNullOrEmpty(_config.Password))
        {
            return new SqlConnectionStringBuilder
            {
                DataSource = _config.Server,
                InitialCatalog = _config.Database ?? "Apperture",
                UserID = _config.User,
                Password = _config.Password,
                TrustServerCertificate = _config.TrustServerCertificate
            }.ConnectionString;
        }

        throw new InvalidOperationException(
            "SqlServer:User and SqlServer:Password are set but SqlServer:Server is missing.");
    }

    private string LogAndReturn(string connectionString, string source)
    {
        if (_hostEnvironment.IsDevelopment())
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                _logger.LogInformation(
                    "Bootstrap SQL resolved from {Source}: DataSource={DataSource}, InitialCatalog={InitialCatalog}",
                    source,
                    builder.DataSource,
                    builder.InitialCatalog);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Bootstrap SQL resolved from {Source} (could not parse for logging).", source);
            }
        }

        return connectionString;
    }
}
