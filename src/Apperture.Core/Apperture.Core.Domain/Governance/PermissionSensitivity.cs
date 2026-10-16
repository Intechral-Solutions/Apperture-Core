namespace Apperture.Core.Domain.Governance;

/// <summary>
/// How much harm holding a permission can do. A declared classification for operators, not an enforcement boundary.
/// Values are ordered: a higher value is more sensitive. There is deliberately no zero value, so an unset field is invalid.
/// </summary>
public enum PermissionSensitivity
{
    /// <summary>Reads of operational data and routine authoring.</summary>
    Standard = 1,

    /// <summary>
    /// Changes configuration, runtime or alerting behaviour, deletes data, makes an outbound call with a stored credential,
    /// or reads the audit log.
    /// </summary>
    Elevated = 2,

    /// <summary>
    /// Can change who holds authority, replace credentials or identity trust, read or write secrets, or introduce
    /// executable code.
    /// </summary>
    Critical = 3,
}
