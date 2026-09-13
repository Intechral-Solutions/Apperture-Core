using Apperture.Core.Application.OperationItems;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

/// <summary>SDK-7 — application-facing reference to a shared-record occurrence (not the record itself).</summary>
public sealed record SharedRecordReference(
    string RecordType,
    string BusinessKey,
    string OperationItemId,
    string OperationGroupId,
    string RunId,
    string? IntegrationId,
    string? IntegrationLabel,
    string? ModuleId,
    string? SourceKind,
    string? SubjectKey,
    string? DisplayLabel,
    string Outcome,
    DateTimeOffset OccurredAtUtc);

public sealed record SharedRecordReferenceQuery(
    string? IntegrationId = null,
    string? ModuleId = null,
    string? Status = null,
    int? LookbackDays = null,
    int Skip = 0,
    int Take = 25);

public sealed record SharedRecordReferencePage(
    SharedRecordIdentity Identity,
    IReadOnlyList<SharedRecordReference> Items,
    int TotalCount,
    int Skip,
    int Take,
    bool HasMore);

public interface ISharedRecordReferenceQueryService
{
    /// <summary>
    /// Bounded cross-integration lookup by shared record type + business key.
    /// Returns operation-item references with provenance; does not merge a canonical entity.
    /// </summary>
    Task<SharedRecordReferencePage> GetReferencesAsync(
        SharedRecordIdentity identity,
        SharedRecordReferenceQuery? query = null,
        CancellationToken cancellationToken = default);
}
