using System.Text.Json;

namespace Apperture.Core.Application.OperationItems;

/// <summary>
/// Reads a <see cref="SharedRecordIdentity"/> from operation-item fields (SDK-7).
/// Single compatibility path for typed columns, typed metadata, and legacy metadata.
/// Never infers a record type from SubjectKey alone.
/// </summary>
public static class SharedRecordIdentityCompatibility
{
    /// <summary>
    /// Prefer normalized columns when present; otherwise parse metadata + SubjectKey fallback.
    /// </summary>
    public static bool TryRead(
        string? sharedRecordType,
        string? sharedBusinessKey,
        string? metadataJson,
        string? subjectKey,
        out SharedRecordIdentity? identity)
    {
        identity = null;

        if (!string.IsNullOrWhiteSpace(sharedRecordType) && !string.IsNullOrWhiteSpace(sharedBusinessKey))
        {
            try
            {
                identity = SharedRecordIdentityRules.ValidateAndNormalize(sharedRecordType, sharedBusinessKey);
                return true;
            }
            catch (OperationItemizationException)
            {
                return false;
            }
        }

        return TryReadFromMetadata(metadataJson, subjectKey, out identity);
    }

    /// <summary>
    /// Legacy / metadata path used by Demo Customer, BDS-6, and reverse links.
    /// Requires explicit recordType; businessKey from metadata else SubjectKey.
    /// </summary>
    public static bool TryReadFromMetadata(
        string? metadataJson,
        string? subjectKey,
        out SharedRecordIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(metadataJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty(OperationItemBusinessMetadataKeys.RecordType, out var rtEl)
                || rtEl.ValueKind != JsonValueKind.String)
                return false;

            var type = rtEl.GetString();
            if (string.IsNullOrWhiteSpace(type))
                return false;

            string? key = null;
            if (root.TryGetProperty(OperationItemBusinessMetadataKeys.BusinessKey, out var bkEl)
                && bkEl.ValueKind == JsonValueKind.String)
            {
                key = bkEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(key))
                key = subjectKey;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            identity = SharedRecordIdentityRules.ValidateAndNormalize(type, key);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (OperationItemizationException)
        {
            return false;
        }
    }

    public static bool TryReadCustomer(
        string? sharedRecordType,
        string? sharedBusinessKey,
        string? metadataJson,
        string? subjectKey,
        out SharedRecordIdentity? identity)
    {
        if (!TryRead(sharedRecordType, sharedBusinessKey, metadataJson, subjectKey, out identity)
            || identity is null)
            return false;

        if (!SharedRecordIdentityRules.RecordTypeComparer.Equals(identity.RecordType, SharedRecordTypes.Customer))
        {
            identity = null;
            return false;
        }

        return true;
    }
}
