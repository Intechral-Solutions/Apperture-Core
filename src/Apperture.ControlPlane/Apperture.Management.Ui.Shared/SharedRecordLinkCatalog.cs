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
/// Maps well-known operation-item <c>recordType</c> metadata to Management UI surfaces.
/// Keys must stay aligned with <c>OperationItemBusinessMetadataKeys</c> (ModuleSdk).
/// First registration: demo customers. Extend by adding types — do not SubjectKey-guess.
/// </summary>
public static class SharedRecordLinkCatalog
{
    // Keep in sync with OperationItemBusinessMetadataKeys (ModuleSdk.Integration).
    public const string RecordTypeMetadataKey = "recordType";
    public const string BusinessKeyMetadataKey = "businessKey";
    public const string CustomerRecordType = "customer";

    /// <summary>Operator-facing label for the demo customer reverse link.</summary>
    public const string CustomerLinkLabel = "Open demo customer";

    /// <summary>
    /// Try to resolve a shared-record link from item metadata and optional subject key.
    /// Business key: metadata <c>businessKey</c>, else <paramref name="subjectKey"/> (same as demo projector).
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

    static bool TryReadRecordIdentity(
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

            recordType = type.Trim();
            businessKey = key.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
