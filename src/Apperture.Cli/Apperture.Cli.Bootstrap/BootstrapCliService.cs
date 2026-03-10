using Apperture.Bootstrap;
using Apperture.Bootstrap.Models;

namespace Apperture.Cli.Bootstrap;

/// <summary>
/// Orchestration service for bootstrap CLI commands. Thin; routes to Bootstrap interfaces.
/// Never returns or logs secrets.
/// </summary>
/// <remarks>
/// Routine operations (SetProfileAsync, RotateSqlCredentialAsync, SetPasswordAsync, BindKeyVaultAsync, RepairAsync)
/// do not require and must not use recovery authorization. Only RecoveryUnlockAsync is the break-glass path.
/// </remarks>
public sealed class BootstrapCliService
{
    private readonly IBootstrapCredentialManager _credentialManager;
    private readonly ISqlConnectionBootstrapProfileManager _profileManager;
    private readonly IBootstrapConnectionProfile _connectionProfile;
    private readonly IBootstrapConfigurationValidator _validator;
    private readonly IBootstrapRecoveryService _recoveryService;
    private readonly IBootstrapAuditWriter _auditWriter;
    private readonly IBootstrapConfigurationStore _configStore;

    public BootstrapCliService(
        IBootstrapCredentialManager credentialManager,
        ISqlConnectionBootstrapProfileManager profileManager,
        IBootstrapConnectionProfile connectionProfile,
        IBootstrapConfigurationValidator validator,
        IBootstrapRecoveryService recoveryService,
        IBootstrapAuditWriter auditWriter,
        IBootstrapConfigurationStore configStore)
    {
        _credentialManager = credentialManager;
        _profileManager = profileManager;
        _connectionProfile = connectionProfile;
        _validator = validator;
        _recoveryService = recoveryService;
        _auditWriter = auditWriter;
        _configStore = configStore;
    }

    /// <summary>Returns non-secret profile; no credentials.</summary>
    public async Task<BootstrapCommandResult> ShowAsync(CancellationToken cancellationToken = default)
    {
        var doc = await _configStore.GetAsync(cancellationToken).ConfigureAwait(false);
        if (doc is null)
            return new BootstrapCommandResult { Success = true, Message = "No bootstrap configuration found." };

        var profile = doc.Profile;
        var msg = profile is null ? "No profile configured." : $"Server={profile.Server}, Database={profile.Database}, AuthMode={profile.AuthMode}";
        return new BootstrapCommandResult { Success = true, Message = msg };
    }

    /// <summary>Validates config; returns BootstrapValidationResult. No secrets.</summary>
    public async Task<BootstrapValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var doc = await _configStore.GetAsync(cancellationToken).ConfigureAwait(false);
        if (doc is null)
            return new BootstrapValidationResult { Success = false, Errors = ["No configuration document found."] };
        return await _validator.ValidateAsync(doc, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tests SQL connection. No connection string in output.</summary>
    public async Task<BootstrapCommandResult> TestSqlConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _connectionProfile.ValidateAsync(cancellationToken).ConfigureAwait(false);
            return new BootstrapCommandResult { Success = true, Message = "SQL connection test succeeded." };
        }
        catch (Exception ex)
        {
            return new BootstrapCommandResult { Success = false, Message = $"Connection failed: {ex.Message}" };
        }
    }

    /// <summary>Updates profile via ISqlConnectionBootstrapProfileManager.</summary>
    public async Task<BootstrapCommandResult> SetProfileAsync(SqlConnectionBootstrapProfile profile, CancellationToken cancellationToken = default)
    {
        await _profileManager.UpdateProfileAsync(profile, cancellationToken).ConfigureAwait(false);
        return new BootstrapCommandResult { Success = true, Message = "Profile updated.", ChangedFields = ["Profile"], RestartRecommended = true };
    }

    /// <summary>Updates auth mode.</summary>
    public async Task<BootstrapCommandResult> SetAuthModeAsync(BootstrapAuthMode mode, CancellationToken cancellationToken = default)
    {
        var current = await _profileManager.GetProfileAsync(cancellationToken).ConfigureAwait(false);
        var updated = current is null
            ? new SqlConnectionBootstrapProfile { Server = "localhost", Database = "Apperture", AuthMode = mode }
            : current with { AuthMode = mode };
        await _profileManager.UpdateProfileAsync(updated, cancellationToken).ConfigureAwait(false);
        return new BootstrapCommandResult { Success = true, Message = $"Auth mode set to {mode}.", ChangedFields = ["Profile"], RestartRecommended = true };
    }

    /// <summary>Updates credential; payload is transient; never persist plaintext.</summary>
    public async Task<BootstrapCommandResult> SetPasswordAsync(object securePayload, CancellationToken cancellationToken = default)
    {
        var request = new BootstrapUpdateRequest { SensitivePayload = securePayload };
        await _credentialManager.UpdateAsync(request, cancellationToken).ConfigureAwait(false);
        return new BootstrapCommandResult { Success = true, Message = "Credential updated.", ChangedFields = ["CredentialReference"], RestartRecommended = true };
    }

    /// <summary>Binds Key Vault secret reference.</summary>
    public async Task<BootstrapCommandResult> BindKeyVaultAsync(string secretUri, CancellationToken cancellationToken = default)
    {
        var ref_ = new BootstrapCredentialReference { SourceType = BootstrapCredentialSourceType.KeyVaultReference, Reference = secretUri };
        var request = new BootstrapUpdateRequest { CredentialReference = ref_ };
        await _credentialManager.UpdateAsync(request, cancellationToken).ConfigureAwait(false);
        return new BootstrapCommandResult { Success = true, Message = "Key Vault binding configured.", ChangedFields = ["CredentialReference"], RestartRecommended = true };
    }

    /// <summary>Binds Managed Identity.</summary>
    public async Task<BootstrapCommandResult> BindManagedIdentityAsync(CancellationToken cancellationToken = default)
    {
        var ref_ = new BootstrapCredentialReference { SourceType = BootstrapCredentialSourceType.ManagedIdentity };
        var request = new BootstrapUpdateRequest { CredentialReference = ref_ };
        await _credentialManager.UpdateAsync(request, cancellationToken).ConfigureAwait(false);
        return new BootstrapCommandResult { Success = true, Message = "Managed Identity binding configured.", ChangedFields = ["CredentialReference"], RestartRecommended = true };
    }

    /// <summary>Binds Windows Integrated Security.</summary>
    public async Task<BootstrapCommandResult> BindIntegratedSecurityAsync(CancellationToken cancellationToken = default)
    {
        var ref_ = new BootstrapCredentialReference { SourceType = BootstrapCredentialSourceType.WindowsIntegrated };
        var request = new BootstrapUpdateRequest { CredentialReference = ref_ };
        await _credentialManager.UpdateAsync(request, cancellationToken).ConfigureAwait(false);
        return new BootstrapCommandResult { Success = true, Message = "Integrated Security binding configured.", ChangedFields = ["CredentialReference"], RestartRecommended = true };
    }

    /// <summary>Rotates SQL credential.</summary>
    public async Task<BootstrapCommandResult> RotateSqlCredentialAsync(BootstrapCredentialReference? reference, CancellationToken cancellationToken = default)
    {
        await _credentialManager.RotateAsync(reference, cancellationToken).ConfigureAwait(false);
        return new BootstrapCommandResult { Success = true, Message = "Credential rotation completed.", ChangedFields = ["CredentialReference"], RestartRecommended = true };
    }

    /// <summary>Repair path.</summary>
    public async Task<BootstrapCommandResult> RepairAsync(CancellationToken cancellationToken = default)
    {
        var result = await _credentialManager.ValidateAsync(cancellationToken).ConfigureAwait(false);
        return new BootstrapCommandResult
        {
            Success = result.Success,
            Message = result.Success ? "Validation passed; no repair needed." : string.Join("; ", result.Errors)
        };
    }

    /// <summary>Recovery only; break-glass path. Calls IBootstrapRecoveryService (authorize, audit, execute).</summary>
    public async Task<BootstrapCommandResult> RecoveryUnlockAsync(BootstrapRecoveryRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _recoveryService.ExecuteRecoveryAsync(request, cancellationToken).ConfigureAwait(false);
        return new BootstrapCommandResult
        {
            Success = result.Success,
            Message = result.Message ?? (result.Success ? "Recovery unlock completed." : "Recovery not authorized."),
            RestartRecommended = result.Success
        };
    }
}
