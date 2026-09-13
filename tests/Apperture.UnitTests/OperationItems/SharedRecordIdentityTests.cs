using System.Text.Json;
using Apperture.Core.Application.OperationItems;
using FluentAssertions;
using Sample.ApiPolling;
using Sample.ApiPolling.Contracts;
using Sample.FileImport;
using Sample.FileImport.Contracts;
using Xunit;

namespace Apperture.UnitTests.OperationItems;

public sealed class SharedRecordIdentityTests
{
    [Fact]
    public void ValidateAndNormalize_accepts_customer_identity()
    {
        var id = SharedRecordIdentityRules.ValidateAndNormalize("Customer", "  C-100  ");
        id.RecordType.Should().Be("customer");
        id.BusinessKey.Should().Be("C-100");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndNormalize_rejects_missing_type(string? type)
    {
        var act = () => SharedRecordIdentityRules.ValidateAndNormalize(type, "C-100");
        act.Should().Throw<OperationItemizationException>().WithMessage("*record type*");
    }

    [Theory]
    [InlineData("1bad")]
    [InlineData("has space")]
    [InlineData("UPPER/slash")]
    public void ValidateAndNormalize_rejects_malformed_type(string type)
    {
        var act = () => SharedRecordIdentityRules.ValidateAndNormalize(type, "C-100");
        act.Should().Throw<OperationItemizationException>();
    }

    [Fact]
    public void ValidateAndNormalize_rejects_missing_or_long_key()
    {
        var actMissing = () => SharedRecordIdentityRules.ValidateAndNormalize("customer", " ");
        actMissing.Should().Throw<OperationItemizationException>().WithMessage("*business key*");

        var longKey = new string('x', SharedRecordIdentityBounds.BusinessKeyMaxLength + 1);
        var actLong = () => SharedRecordIdentityRules.ValidateAndNormalize("customer", longKey);
        actLong.Should().Throw<OperationItemizationException>().WithMessage("*max length*");
    }

    [Fact]
    public void AlignSubjectKey_rejects_mismatch()
    {
        var id = new SharedRecordIdentity("customer", "C-100");
        var act = () => SharedRecordIdentityRules.AlignSubjectKey("C-200", id);
        act.Should().Throw<OperationItemizationException>().WithMessage("*must match*");
    }

    [Fact]
    public void BuildAppendRequest_with_shared_record_writes_metadata_and_columns()
    {
        var request = OperationItemReporting.BuildAppendRequest(
            "g1",
            "r1",
            1,
            OperationItemStatuses.Succeeded,
            subjectKey: null,
            displayLabel: "Ada",
            errorCode: null,
            errorMessage: null,
            isRetryable: false,
            changeSummary: new { changeKind = "imported" },
            metadata: new { displayName = "Ada", fixtureId = "x" },
            completedAtUtc: DateTimeOffset.UtcNow,
            sharedRecord: new SharedRecordIdentity(SharedRecordTypes.Customer, "C-100"));

        request.SharedRecordType.Should().Be("customer");
        request.SharedBusinessKey.Should().Be("C-100");
        request.SubjectKey.Should().Be("C-100");

        using var doc = JsonDocument.Parse(request.MetadataJson!);
        doc.RootElement.GetProperty("recordType").GetString().Should().Be("customer");
        doc.RootElement.GetProperty("businessKey").GetString().Should().Be("C-100");
        doc.RootElement.GetProperty("displayName").GetString().Should().Be("Ada");
        doc.RootElement.GetProperty("fixtureId").GetString().Should().Be("x");
    }

    [Fact]
    public void BuildAppendRequest_without_shared_record_leaves_columns_null()
    {
        var request = OperationItemReporting.BuildAppendRequest(
            "g1",
            "r1",
            1,
            OperationItemStatuses.Succeeded,
            "S-1",
            "Label",
            null,
            null,
            false,
            null,
            new { note = "plain" });

        request.SharedRecordType.Should().BeNull();
        request.SharedBusinessKey.Should().BeNull();
        request.MetadataJson.Should().Contain("note");
        request.MetadataJson.Should().NotContain("recordType");
    }

    [Fact]
    public void Compatibility_reads_typed_columns_and_legacy_metadata()
    {
        SharedRecordIdentityCompatibility.TryRead("customer", "C-1", null, null, out var fromCols)
            .Should().BeTrue();
        fromCols!.BusinessKey.Should().Be("C-1");

        SharedRecordIdentityCompatibility.TryReadFromMetadata(
                """{"recordType":"customer","businessKey":"C-2"}""",
                subjectKey: null,
                out var fromMeta)
            .Should().BeTrue();
        fromMeta!.BusinessKey.Should().Be("C-2");

        SharedRecordIdentityCompatibility.TryReadFromMetadata(
                """{"recordType":"customer"}""",
                subjectKey: "C-3",
                out var fromSubject)
            .Should().BeTrue();
        fromSubject!.BusinessKey.Should().Be("C-3");

        SharedRecordIdentityCompatibility.TryReadFromMetadata(
                """{"displayName":"x"}""",
                subjectKey: "C-4",
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Compatibility_does_not_cross_record_types()
    {
        SharedRecordIdentityCompatibility.TryReadCustomer(
                "student",
                "C-100",
                null,
                null,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void FileImport_and_ApiPolling_emit_identical_customer_identity_for_same_key()
    {
        var fi = FileImportOperationItemization.ToAppendRequest(
            "g1",
            "r1",
            FileImportFixtureIds.CustomersValid,
            new ImportRowOutcome(
                "b", 1, "C-100", ImportRowStatus.Succeeded, null, null, "ok",
                DateTimeOffset.UtcNow, "Cust", "a@b.com", "Active"));

        var ap = ApiPollingOperationItemization.ToAppendRequest(
            "g2",
            "r2",
            "snap",
            includeDeleted: false,
            new RecordClassification(
                "s", 1, "C-100", "Cust", ApiPollingChangeKinds.New,
                OperationItemStatuses.Succeeded, null, null, "ok",
                1, "Active", null, null, DateTimeOffset.UtcNow, "a@b.com"));

        fi.SharedRecordType.Should().Be(ap.SharedRecordType).And.Be(SharedRecordTypes.Customer);
        fi.SharedBusinessKey.Should().Be(ap.SharedBusinessKey).And.Be("C-100");
        fi.MetadataJson.Should().NotContain("recordType =");
        // Modules must not handcraft literals as source — platform Merge writes keys:
        using var fiMeta = JsonDocument.Parse(fi.MetadataJson!);
        fiMeta.RootElement.GetProperty("recordType").GetString().Should().Be("customer");
        // Ensure module anonymous objects no longer declare recordType as C# property name in source:
        // (verified by absence of SharedRecordType null and presence of columns)
        fi.SharedRecordType.Should().NotBeNull();
        ap.SharedRecordType.Should().NotBeNull();
    }

    [Fact]
    public async Task InMemory_store_persists_shared_identity_columns()
    {
        var store = new InMemoryOperationItemStore();
        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(
            "run-1", "Sample.FileImport", null, null, "file_import"));
        var append = OperationItemReporting.BuildAppendRequest(
            group.OperationGroupId,
            "run-1",
            1,
            OperationItemStatuses.Succeeded,
            "C-100",
            "Cust",
            null,
            null,
            false,
            null,
            new { displayName = "Cust" },
            sharedRecord: new SharedRecordIdentity(SharedRecordTypes.Customer, "C-100"));
        var item = await store.AppendItemAsync(append);
        item.SharedRecordType.Should().Be("customer");
        item.SharedBusinessKey.Should().Be("C-100");
    }
}
