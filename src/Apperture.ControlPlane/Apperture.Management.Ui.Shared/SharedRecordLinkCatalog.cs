using System.Text.Json;

namespace Apperture.Management.Ui.Shared;

/// <summary>
/// Resolved navigation from an operation item to a shared business-record surface.
/// Catalog is app-owned; not a platform <c>BusinessRecord</c> entity model.
/// </summary>
public sealed record SharedRecordLink(
    string Href,
    string Label,
    string RecordType,
    string BusinessKey);

/// <summary>
/// Maps well-known shared-record types to Management UI surfaces (SDK-7).
/// Identity extraction mirrors <c>SharedRecordIdentityCompatibility</c> in ModuleSdk
/// (Ui.Shared cannot reference ModuleSdk.Integration — SDK-6D boundary).
/// This catalog only maps destinations — modules do not declare URLs.
/// </summary>
public static class SharedRecordLinkCatalog
{
    // Keep in sync with SharedRecordTypes / OperationItemBusinessMetadataKeys (ModuleSdk).
    public const string RecordTypeMetadataKey = "recordType";
    public const string BusinessKeyMetadataKey = "businessKey";
    public const string CustomerRecordType = "customer";

    /// <summary>Operator-facing label for the demo customer reverse link.</summary>
    public const string CustomerLinkLabel = "Open demo customer";

    /// <summary>
    /// Try to resolve a shared-record link from item metadata and optional subject key.
    /// Requires explicit recordType; business key from metadata else subjectKey.
    /// Never SubjectKey-guesses without a record type.
    /// </summary>
    public static bool TryResolve(string? metadataJson, string? subjectKey, out SharedRecordLink? link)
    {
        link = null;

        if (!TryReadRecordIdentity(metadataJson, subjectKey, out var recordType, out var businessKey))
            return false;

        if (string.Equals(recordType, CustomerRecordType, StringComparison.OrdinalIgnoreCase))
        {
            var detail = DemoCustomerUiRoutes.Detail(businessKey);
            link = new SharedRecordLink(
                Href: AffectedRecordNavigation.WithoutContext(detail),
                Label: CustomerLinkLabel,
                RecordType: CustomerRecordType,
                BusinessKey: businessKey);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Same rules as ModuleSdk <c>SharedRecordIdentityCompatibility.TryReadFromMetadata</c>.
    /// Kept local because Management.Ui.Shared must not reference ModuleSdk.Integration.
    /// </summary>
    internal static bool TryReadRecordIdentity(
        string? metadataJson,
        string? subjectKey,
        out string recordType,
        out string businessKey)
    {
        recordType = "";
        businessKey = "";

        if (string.IsNullOrWhiteSpace(metadataJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty(RecordTypeMetadataKey, out var rtEl)
                || rtEl.ValueKind != JsonValueKind.String)
                return false;

            var type = rtEl.GetString();
            if (string.IsNullOrWhiteSpace(type))
                return false;

            string? key = null;
            if (root.TryGetProperty(BusinessKeyMetadataKey, out var bkEl)
                && bkEl.ValueKind == JsonValueKind.String)
            {
                key = bkEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(key))
                key = subjectKey;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            recordType = type.Trim().ToLowerInvariant();
            businessKey = key.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
