namespace Apperture.ControlPlane.Security;

// AUD-1.4 slice 3, work package 4a. The result types of the integration-delete settings inventory and the settings-secret
// classifier. Nothing here has a field or a ToString that shows a secret, a secret reference or a stored setting value: a plan
// can be logged, thrown in an exception and compared in a test without disclosing any of them. The secret ids a plan needs in
// order to retire what it classified are internal to this assembly.

/// <summary>What the delete must do about the secret, if any, that one persisted integration setting override refers to.</summary>
public enum IntegrationSecretDispositionKind
{
    /// <summary>No usable settings-provisioned secret is involved: nothing to retire. The override row is still removed.</summary>
    NoApplicableSecret,

    /// <summary>A settings-provisioned secret this override owns, that nothing surviving the delete refers to: retire it once the override is removed.</summary>
    RetireAfterOverrideRemoval,

    /// <summary>A settings-provisioned secret that a reference surviving the delete still holds: leave it alone.</summary>
    RetainSharedReference,

    /// <summary>Credible evidence of a usable settings-provisioned secret whose ownership or disposition cannot be established: the delete must be refused.</summary>
    Ambiguous,
}

/// <summary>Why an override involves no secret to retire.</summary>
public enum IntegrationSecretNoSecretReason
{
    /// <summary>The override holds no reference, or a value that names no GUID on a setting that is not a credential.</summary>
    NoReference,

    /// <summary>The reference names no secret in the store.</summary>
    ReferenceDoesNotResolve,

    /// <summary>The secret exists but was not provisioned by the settings lifecycle (another provider's secret): it is never retired here.</summary>
    NotSettingsProvisioned,

    /// <summary>The secret was already disabled or revoked.</summary>
    AlreadyUnusable,
}

/// <summary>Why a credible settings-provisioned secret cannot be given a safe disposition.</summary>
public enum IntegrationSecretAmbiguityReason
{
    /// <summary>The setting is a credential setting but its stored value is not one readable secret reference.</summary>
    UnreadableReference,

    /// <summary>The secret carries no setting key that could establish whose it is.</summary>
    OwnershipNotEstablished,

    /// <summary>The secret was provisioned for a different setting key than the override that refers to it, or several overrides refer to it.</summary>
    SettingKeyMismatch,
}

/// <summary>The kinds of reference that can survive an integration delete.</summary>
public enum SurvivingSecretReferenceKind
{
    /// <summary>An integration-scoped setting override of another integration.</summary>
    OtherIntegrationSetting,

    /// <summary>A platform-scoped, or any other non-integration-scoped, setting value.</summary>
    PlatformSetting,

    /// <summary>A connection's secret references.</summary>
    Connection,

    /// <summary>An Entra instance registration's client secret.</summary>
    EntraRegistration,
}

/// <summary>Where a reference to a candidate secret was found, outside the integration being deleted. Names the owner, never the secret.</summary>
public sealed class SurvivingSecretReference
{
    internal SurvivingSecretReference(SurvivingSecretReferenceKind kind, string ownerId)
    {
        Kind = kind;
        OwnerId = ownerId;
    }

    public SurvivingSecretReferenceKind Kind { get; }

    /// <summary>The row that holds the reference: the setting row id, the connection id or the registration id.</summary>
    public string OwnerId { get; }

    public override string ToString() => $"{Kind} {OwnerId}";
}

/// <summary>One persisted integration-scoped setting override. Exists whether or not a definition for its key still does.</summary>
public sealed class IntegrationSettingOverride
{
    internal IntegrationSettingOverride(string rowId, string key, string valueJson)
    {
        RowId = rowId;
        Key = key;
        ValueJson = valueJson;
    }

    /// <summary>The row's id.</summary>
    public string RowId { get; }

    /// <summary>The setting key the override was stored under.</summary>
    public string Key { get; }

    /// <summary>The stored value. For a credential setting this is the secret reference, so it is internal and never printed.</summary>
    internal string ValueJson { get; }

    public override string ToString() => $"Override {Key}";
}

/// <summary>The classification of one override.</summary>
public sealed class IntegrationOverrideDisposition
{
    internal IntegrationOverrideDisposition(
        string key,
        IntegrationSecretDispositionKind kind,
        IntegrationSecretNoSecretReason? noSecretReason = null,
        IntegrationSecretAmbiguityReason? ambiguityReason = null,
        IReadOnlyList<SurvivingSecretReference>? survivingReferences = null)
    {
        Key = key;
        Kind = kind;
        NoSecretReason = noSecretReason;
        AmbiguityReason = ambiguityReason;
        SurvivingReferences = survivingReferences ?? [];
    }

    public string Key { get; }

    public IntegrationSecretDispositionKind Kind { get; }

    /// <summary>Set when <see cref="Kind"/> is <see cref="IntegrationSecretDispositionKind.NoApplicableSecret"/>.</summary>
    public IntegrationSecretNoSecretReason? NoSecretReason { get; }

    /// <summary>Set when <see cref="Kind"/> is <see cref="IntegrationSecretDispositionKind.Ambiguous"/>.</summary>
    public IntegrationSecretAmbiguityReason? AmbiguityReason { get; }

    /// <summary>Set when <see cref="Kind"/> is <see cref="IntegrationSecretDispositionKind.RetainSharedReference"/>: the references that keep the secret alive.</summary>
    public IReadOnlyList<SurvivingSecretReference> SurvivingReferences { get; }

    public override string ToString() => $"{Key}: {Kind}";
}

/// <summary>One secret the delete must retire, and the override that owned it.</summary>
public sealed class PlannedSecretRetirement
{
    internal PlannedSecretRetirement(string settingKey, Guid secretId)
    {
        SettingKey = settingKey;
        SecretId = secretId;
    }

    /// <summary>The setting key of the override that referenced the secret; its retirement row is recorded under this setting.</summary>
    public string SettingKey { get; }

    /// <summary>The secret's id, which is the reference the override held. Never printed or recorded.</summary>
    internal Guid SecretId { get; }

    public override string ToString() => $"Retire the secret of {SettingKey}";
}

/// <summary>The typed refusal of an integration delete whose settings hold a credential that cannot be safely disposed of.</summary>
public sealed class IntegrationSettingDispositionRefusal
{
    internal IntegrationSettingDispositionRefusal(
        IReadOnlyList<string> settingKeys,
        IReadOnlyList<IntegrationSecretAmbiguityReason> reasons)
    {
        SettingKeys = settingKeys;
        Reasons = reasons;
    }

    /// <summary>The keys of the overrides whose disposition is ambiguous, in key order.</summary>
    public IReadOnlyList<string> SettingKeys { get; }

    /// <summary>The distinct reasons, in enum order.</summary>
    public IReadOnlyList<IntegrationSecretAmbiguityReason> Reasons { get; }

    public override string ToString() => $"Refused: {SettingKeys.Count} setting(s) with an undetermined credential disposition";
}

/// <summary>
/// The outcome of classifying every persisted integration-scoped setting override of an integration, ahead of removing them.
/// Read-only evidence: producing it changes nothing. A plan that <see cref="IsRefused"/> carries no retirement, so nothing can be
/// retired from it.
/// </summary>
/// <remarks>
/// Which rows to remove and which secrets to retire are separate questions. <see cref="Overrides"/> lists every persisted override
/// of the integration, found by ownership alone, to be removed whatever its definition now says. <see cref="Retirements"/> lists
/// only secrets that passed the ownership, key-correspondence and surviving-reference rules.
/// </remarks>
public sealed class IntegrationSettingDeletionPlan
{
    internal IntegrationSettingDeletionPlan(
        string integrationId,
        IReadOnlyList<IntegrationSettingOverride> overrides,
        IReadOnlyList<IntegrationOverrideDisposition> dispositions,
        IReadOnlyList<PlannedSecretRetirement> retirements,
        IntegrationSettingDispositionRefusal? refusal)
    {
        IntegrationId = integrationId;
        Overrides = overrides;
        Dispositions = dispositions;
        Retirements = refusal is null ? retirements : [];
        Refusal = refusal;
    }

    public string IntegrationId { get; }

    /// <summary>Every persisted override of the integration, in key order: all of them are removed with the integration.</summary>
    public IReadOnlyList<IntegrationSettingOverride> Overrides { get; }

    /// <summary>One classification per override, in key order.</summary>
    public IReadOnlyList<IntegrationOverrideDisposition> Dispositions { get; }

    /// <summary>The distinct secrets to retire after the overrides are removed. Empty when the plan is refused.</summary>
    public IReadOnlyList<PlannedSecretRetirement> Retirements { get; }

    /// <summary>Set when any override's disposition is ambiguous. The delete must then refuse, as a whole.</summary>
    public IntegrationSettingDispositionRefusal? Refusal { get; }

    public bool IsRefused => Refusal is not null;

    /// <summary>Throws <see cref="IntegrationSettingDispositionRefusedException"/> when the plan is refused.</summary>
    public void ThrowIfRefused()
    {
        if (Refusal is not null)
            throw new IntegrationSettingDispositionRefusedException(Refusal);
    }

    public override string ToString() =>
        $"Plan for an integration: {Overrides.Count} override(s), {Retirements.Count} secret(s) to retire, {(IsRefused ? "refused" : "not refused")}";
}

/// <summary>Stable machine codes of an integration delete's refusals, for the API.</summary>
public static class IntegrationDeleteRefusalCodes
{
    /// <summary>A setting holds a stored credential whose ownership or disposition could not be established (owner ruling H1).</summary>
    public const string UndeterminedCredential = "integration_setting_credential_undetermined";
}

/// <summary>
/// An integration delete is refused because one or more of its setting overrides hold a credential whose ownership or disposition
/// could not be determined. Nothing was changed. The message names no secret, reference or metadata of one; the keys of the
/// affected settings are on <see cref="Refusal"/> for the caller to present as it chooses. It is an
/// <see cref="InvalidOperationException"/> like the delete's other blockers.
/// </summary>
public sealed class IntegrationSettingDispositionRefusedException : InvalidOperationException
{
    public IntegrationSettingDispositionRefusedException(IntegrationSettingDispositionRefusal refusal)
        : base(
            "Cannot delete integration: one or more of its settings hold a stored credential whose ownership could not be "
            + "established, so it cannot be safely removed. Nothing was changed.")
    {
        Refusal = refusal;
    }

    public IntegrationSettingDispositionRefusal Refusal { get; }
}

/// <summary>
/// Retiring a secret the plan classified as safe failed. The message names no secret; <see cref="SettingKey"/> and
/// <see cref="InnerExceptionType"/> are the diagnostic. The store's own failure is deliberately not attached: its message embeds
/// the secret's id.
/// </summary>
public sealed class IntegrationSettingSecretRetirementException : InvalidOperationException
{
    public IntegrationSettingSecretRetirementException(string settingKey, string? innerExceptionType)
        : base("A stored credential of the integration could not be retired.")
    {
        SettingKey = settingKey;
        InnerExceptionType = innerExceptionType;
    }

    public string SettingKey { get; }

    public string? InnerExceptionType { get; }
}
