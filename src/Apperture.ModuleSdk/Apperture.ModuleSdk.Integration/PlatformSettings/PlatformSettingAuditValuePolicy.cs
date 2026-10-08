namespace Apperture.Core.Application.PlatformSettings;

/// <summary>
/// Whether the audit rows of a change to an ordinary setting may carry its previous and new values (AUD-1 design gate §12.3).
/// The policy is declared on the definition, so it is stated once, reviewed with the setting and applied identically by the
/// platform and the integration settings services. The value of a sensitive setting is never recorded, whatever this says.
/// </summary>
public enum PlatformSettingAuditValuePolicy
{
    /// <summary>
    /// No explicit declaration: the value is recorded only when the definition's shape bounds it so that it cannot carry an
    /// email address (a boolean, an integer, a duration, or a choice validated against declared or provider-supplied
    /// options). Free text is omitted until its definition declares otherwise, so an unreviewed text setting fails closed.
    /// </summary>
    ByShape = 0,

    /// <summary>Reviewed free text that is bounded and is not an email address (for example a host name). The values are recorded.</summary>
    Record = 1,

    /// <summary>
    /// A field whose value is an operational email address, explicitly permitted by the structured audit schema (for example
    /// the sender address of notification mail). The values are recorded. This is the only way an email address enters a
    /// setting audit row.
    /// </summary>
    RecordOperationalEmail = 2,

    /// <summary>
    /// The change is audited by setting name and safe metadata only. Neither value is recorded. For credential-related
    /// identifiers (for example an SMTP username, even when formatted as an email address) and for free text that may carry one.
    /// </summary>
    Omit = 3,
}
