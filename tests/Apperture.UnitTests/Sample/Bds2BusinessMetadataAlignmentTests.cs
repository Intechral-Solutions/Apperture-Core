using System.Text.Json;
using Apperture.Core.Application.OperationItems;
using FluentAssertions;
using Sample.ApiPolling;
using Sample.ApiPolling.Contracts;
using Sample.FileImport;
using Sample.FileImport.Contracts;
using Xunit;

namespace Apperture.UnitTests.Sample;

/// <summary>BDS-2 — aligned business metadata and shared demo keys across sample modules.</summary>
public sealed class Bds2BusinessMetadataAlignmentTests
{
    [Fact]
    public void FileImport_ToAppendRequest_emits_aligned_business_metadata_and_human_display_label()
    {
        var observedAt = DateTimeOffset.Parse("2026-07-21T15:00:00Z");
        var outcome = new ImportRowOutcome(
            BatchId: "batch-1",
            RowNumber: 1,
            SourceKey: "C-001",
            Status: ImportRowStatus.Succeeded,
            ErrorCode: null,
            ErrorMessage: null,
            SafeRowSummary: "row=1; id=C-001; email=ada@example.com",
            CreatedAtUtc: observedAt,
            DisplayName: "Ada Lovelace",
            Email: "ada@example.com",
            SourceStatus: "Active");

        var request = FileImportOperationItemization.ToAppendRequest(
            "og-1",
            "run-1",
            FileImportFixtureIds.CustomersValid,
            outcome);

        request.SubjectKey.Should().Be("C-001");
        request.DisplayLabel.Should().Be("Ada Lovelace");
        request.DisplayLabel.Should().NotBe(request.SubjectKey);
        request.CompletedAtUtc.Should().Be(observedAt);

        using var meta = JsonDocument.Parse(request.MetadataJson!);
        var root = meta.RootElement;
        root.GetProperty("recordType").GetString().Should().Be(OperationItemBusinessMetadataKeys.CustomerRecordType);
        root.GetProperty("businessKey").GetString().Should().Be(request.SubjectKey);
        root.GetProperty("displayName").GetString().Should().Be("Ada Lovelace");
        root.GetProperty("changeKind").GetString().Should().Be(FileImportChangeKinds.Imported);
        root.GetProperty("observedAt").GetDateTimeOffset().Should().Be(observedAt);
        root.GetProperty("email").GetString().Should().Be("ada@example.com");
        root.GetProperty("status").GetString().Should().Be("Active");
        root.TryGetProperty("rawCsv", out _).Should().BeFalse();

        using var summary = JsonDocument.Parse(request.ChangeSummaryJson!);
        summary.RootElement.GetProperty("changeKind").GetString().Should().Be(FileImportChangeKinds.Imported);
    }

    [Fact]
    public void FileImport_DisplayName_prefers_name_then_email_then_external_id()
    {
        FileImportRowProcessor.BuildDisplayName(new ParsedCustomerRow(
                1, true, "C-001", "Ada", "Lovelace", "ada@example.com", "Premium", "Active", "2024-01-01", null))
            .Should().Be("Ada Lovelace");

        FileImportRowProcessor.BuildDisplayName(new ParsedCustomerRow(
                2, true, "C-009", null, null, "only@example.com", "Standard", "Active", "2024-01-01", null))
            .Should().Be("only@example.com");

        FileImportRowProcessor.BuildDisplayName(new ParsedCustomerRow(
                3, true, "C-010", null, null, null, "Standard", "Active", "2024-01-01", null))
            .Should().Be("C-010");
    }

    [Fact]
    public void ApiPolling_ToAppendRequest_emits_aligned_business_metadata()
    {
        var observedAt = DateTimeOffset.Parse("2026-07-21T16:00:00Z");
        var classification = new RecordClassification(
            SyncBatchId: "sync-1",
            Sequence: 1,
            ExternalId: "C-001",
            DisplayName: "Ada Lovelace",
            ChangeKind: ApiPollingChangeKinds.Updated,
            ItemStatus: OperationItemStatuses.Succeeded,
            ErrorCode: null,
            ErrorMessage: null,
            ChangeSummary: "updated",
            Version: 2,
            Status: "Active",
            Tier: "Premium",
            ChangedFields: ["email"],
            CreatedAtUtc: observedAt);

        var request = ApiPollingOperationItemization.ToAppendRequest(
            "og-1",
            "run-1",
            ApiPollingSnapshotIds.SnapshotInitial,
            includeDeleted: true,
            classification);

        request.SubjectKey.Should().Be("C-001");
        request.DisplayLabel.Should().Be("Ada Lovelace");
        request.CompletedAtUtc.Should().Be(observedAt);

        using var meta = JsonDocument.Parse(request.MetadataJson!);
        var root = meta.RootElement;
        root.GetProperty("recordType").GetString().Should().Be(OperationItemBusinessMetadataKeys.CustomerRecordType);
        root.GetProperty("businessKey").GetString().Should().Be(request.SubjectKey);
        root.GetProperty("displayName").GetString().Should().Be("Ada Lovelace");
        root.GetProperty("changeKind").GetString().Should().Be(ApiPollingChangeKinds.Updated);
        root.GetProperty("observedAt").GetDateTimeOffset().Should().Be(observedAt);
    }

    [Fact]
    public async Task Demo_keys_overlap_with_fileimport_only_apipolling_only_and_conflict_pair()
    {
        FileImportEmbeddedFixtures.TryLoadCsv(FileImportFixtureIds.CustomersValid, out var validCsv, out _).Should().BeTrue();
        FileImportEmbeddedFixtures.TryLoadCsv(FileImportFixtureIds.CustomersMixed, out var mixedCsv, out _).Should().BeTrue();

        var fileImportKeys = FileImportCsvParser.Parse(validCsv!)
            .Rows.Concat(FileImportCsvParser.Parse(mixedCsv!).Rows)
            .Select(r => r.ExternalId?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var client = new EmbeddedSampleDirectoryApiClient();
        var initial = await client.GetSnapshotAsync(
            ApiPollingSnapshotIds.SnapshotInitial,
            new ApiPollQuery(PageSize: 50, IncludeDeleted: true));

        var apiPollingKeys = initial.Records
            .Select(r => r.ExternalId?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var shared = fileImportKeys.Intersect(apiPollingKeys, StringComparer.OrdinalIgnoreCase).ToArray();
        shared.Should().Contain(["C-001", "C-002"]);
        shared.Length.Should().BeGreaterThanOrEqualTo(2);

        fileImportKeys.Except(apiPollingKeys, StringComparer.OrdinalIgnoreCase).Should().Contain("C-003");
        apiPollingKeys.Except(fileImportKeys, StringComparer.OrdinalIgnoreCase).Should().Contain("C-400");

        // Intentional conflict: same business key, differing email and/or status.
        var fiConflict = FileImportCsvParser.Parse(mixedCsv!).Rows
            .First(r => string.Equals(r.ExternalId, "C-100", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Email, "valid.one@example.com", StringComparison.OrdinalIgnoreCase));
        var apConflict = initial.Records.Single(r => r.ExternalId == "C-100");

        fiConflict.Email.Should().Be("valid.one@example.com");
        fiConflict.Status.Should().Be("Active");
        apConflict.Email.Should().Be("conflict.c100@apipolling.example");
        apConflict.Status.Should().Be("Inactive");
        (fiConflict.Email != apConflict.Email || fiConflict.Status != apConflict.Status).Should().BeTrue();
    }
}
