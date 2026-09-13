using System.Text.RegularExpressions;

namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Typed shared-record identity attached to an operation item (SDK-7).
/// Identifies a logical business record across integrations; not a platform entity or payload.
/// </summary>
public sealed record SharedRecordIdentity(string RecordType, string BusinessKey);

/// <summary>
/// Built-in / reserved shared record type keys.
/// Modules share these constants; they do not reference one another.
/// </summary>
public static class SharedRecordTypes
{
    /// <summary>Demo / sample customer proof type (FileImport + ApiPolling).</summary>
    public const string Customer = OperationItemBusinessMetadataKeys.CustomerRecordType;
}

/// <summary>Bounds and validation for <see cref="SharedRecordIdentity"/> (SDK-7).</summary>
public static class SharedRecordIdentityBounds
{
    public const int RecordTypeMaxLength = 64;
    public const int BusinessKeyMaxLength = OperationItemizationBounds.SubjectKeyMaxLength;

    /// <summary>
    /// Allowed record-type characters after normalization: lowercase letter, digit, '.', '_', '-'.
    /// </summary>
    public static readonly Regex RecordTypePattern = new(
        @"^[a-z][a-z0-9._-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
}

/// <summary>
/// Validation and normalization for shared-record identities.
/// Record types are compared case-insensitively and stored lowercase.
/// Business keys are trimmed and compared case-insensitively for the customer proof
/// (matches Demo Customer projection); stored value preserves source casing after trim.
/// </summary>
public static class SharedRecordIdentityRules
{
    public static StringComparer RecordTypeComparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>Customer keys match Demo Customer projection (OrdinalIgnoreCase).</summary>
    public static StringComparer BusinessKeyComparer { get; } = StringComparer.OrdinalIgnoreCase;

    public static SharedRecordIdentity ValidateAndNormalize(SharedRecordIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return ValidateAndNormalize(identity.RecordType, identity.BusinessKey);
    }

    public static SharedRecordIdentity ValidateAndNormalize(string? recordType, string? businessKey)
    {
        if (string.IsNullOrWhiteSpace(recordType))
            throw new OperationItemizationException("Shared record type is required.");

        var type = recordType.Trim().ToLowerInvariant();
        if (type.Length > SharedRecordIdentityBounds.RecordTypeMaxLength)
        {
            throw new OperationItemizationException(
                $"Shared record type exceeds max length {SharedRecordIdentityBounds.RecordTypeMaxLength}.");
        }

        if (!SharedRecordIdentityBounds.RecordTypePattern.IsMatch(type))
        {
            throw new OperationItemizationException(
                "Shared record type must start with a lowercase letter and contain only lowercase letters, digits, '.', '_', or '-'.");
        }

        if (string.IsNullOrWhiteSpace(businessKey))
            throw new OperationItemizationException("Shared business key is required.");

        var key = businessKey.Trim();
        if (key.Length > SharedRecordIdentityBounds.BusinessKeyMaxLength)
        {
            throw new OperationItemizationException(
                $"Shared business key exceeds max length {SharedRecordIdentityBounds.BusinessKeyMaxLength}.");
        }

        if (key.Contains('/') || key.Contains('\\') || key.Contains('?') || key.Contains('#') || key.Contains(':'))
        {
            throw new OperationItemizationException(
                "Shared business key must not contain route-reserved characters (/ \\ ? # :).");
        }

        return new SharedRecordIdentity(type, key);
    }

    public static bool IsKnownBuiltInType(string? recordType) =>
        !string.IsNullOrWhiteSpace(recordType)
        && RecordTypeComparer.Equals(recordType.Trim(), SharedRecordTypes.Customer);

    /// <summary>
    /// When a shared identity is reported, SubjectKey must match BusinessKey (ignore-case)
    /// or be omitted (platform sets SubjectKey from the business key).
    /// </summary>
    public static string AlignSubjectKey(string? subjectKey, SharedRecordIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(subjectKey))
            return identity.BusinessKey;

        var trimmed = subjectKey.Trim();
        if (!BusinessKeyComparer.Equals(trimmed, identity.BusinessKey))
        {
            throw new OperationItemizationException(
                $"SubjectKey '{trimmed}' must match shared business key '{identity.BusinessKey}' for shared-record items.");
        }

        return identity.BusinessKey;
    }
}
