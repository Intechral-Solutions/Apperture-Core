namespace Apperture.Bootstrap.Models;

/// <summary>
/// Well-known recovery actions. Break-glass only; not for routine credential rotation or admin.
/// </summary>
public enum BootstrapRecoveryAction
{
    /// <summary>Rebind a broken bootstrap credential source (e.g. new Key Vault reference).</summary>
    RebindCredentialSource = 0,

    /// <summary>Switch to a recovery profile (e.g. alternate server/database or auth mode).</summary>
    SwitchToRecoveryProfile = 1,

    /// <summary>Replace a locally protected bootstrap credential (e.g. DPAPI-stored).</summary>
    ReplaceLocalCredential = 2,

    /// <summary>Restore platform access when primary auth is unavailable (generic recovery).</summary>
    RestorePlatformAccess = 3
}
