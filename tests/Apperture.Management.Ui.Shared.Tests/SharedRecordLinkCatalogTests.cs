using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class SharedRecordLinkCatalogTests
{
    [Fact]
    public void Customer_metadata_resolves_demo_customer_detail_without_run_context()
    {
        var meta = """{"recordType":"customer","businessKey":"C-100","displayName":"Valid One"}""";

        SharedRecordLinkCatalog.TryResolve(meta, subjectKey: "C-100", out var link)
            .Should().BeTrue();
        link.Should().NotBeNull();
        link!.Href.Should().Be("/business-data/customers/C-100");
        link.Href.Should().NotContain("from=");
        link.Label.Should().Be(SharedRecordLinkCatalog.CustomerLinkLabel);
        link.Label.Should().NotContain("BDS");
        link.RecordType.Should().Be("customer");
        link.BusinessKey.Should().Be("C-100");
    }

    [Fact]
    public void Business_key_falls_back_to_SubjectKey_when_metadata_key_absent()
    {
        var meta = """{"recordType":"customer","displayName":"Valid One"}""";

        SharedRecordLinkCatalog.TryResolve(meta, subjectKey: "C-002", out var link)
            .Should().BeTrue();
        link!.Href.Should().Be("/business-data/customers/C-002");
        link.BusinessKey.Should().Be("C-002");
    }

    [Fact]
    public void Encodes_business_key_in_href()
    {
        var meta = """{"recordType":"customer","businessKey":"C 100"}""";

        SharedRecordLinkCatalog.TryResolve(meta, subjectKey: null, out var link)
            .Should().BeTrue();
        link!.Href.Should().Be(DemoCustomerUiRoutes.Detail("C 100"));
        link.Href.Should().Contain(Uri.EscapeDataString("C 100"));
    }

    [Fact]
    public void Unknown_record_type_does_not_resolve()
    {
        var meta = """{"recordType":"invoice","businessKey":"INV-1"}""";

        SharedRecordLinkCatalog.TryResolve(meta, subjectKey: "INV-1", out var link)
            .Should().BeFalse();
        link.Should().BeNull();
    }

    [Fact]
    public void SubjectKey_alone_without_recordType_does_not_resolve()
    {
        SharedRecordLinkCatalog.TryResolve(metadataJson: null, subjectKey: "C-100", out var link)
            .Should().BeFalse();
        SharedRecordLinkCatalog.TryResolve("""{"displayName":"x"}""", subjectKey: "C-100", out link)
            .Should().BeFalse();
        link.Should().BeNull();
    }

    [Fact]
    public void Customer_type_without_any_key_does_not_resolve()
    {
        SharedRecordLinkCatalog.TryResolve("""{"recordType":"customer"}""", subjectKey: null, out var link)
            .Should().BeFalse();
        link.Should().BeNull();
    }

    [Fact]
    public void Invalid_json_does_not_resolve()
    {
        SharedRecordLinkCatalog.TryResolve("{not-json", subjectKey: "C-100", out var link)
            .Should().BeFalse();
        link.Should().BeNull();
    }

    [Fact]
    public void Panel_wires_shared_record_catalog()
    {
        var panel = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Apperture.ControlPlane",
            "Apperture.Management.Ui.Client",
            "Organisms",
            "RunOperationItemizationPanel.razor"));

        panel.Should().Contain("SharedRecordLinkCatalog.TryResolve");
        panel.Should().Contain("data-testid=\"shared-record-link\"");
        panel.Should().NotContain("Operation Item Explorer");
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx"))
                || File.Exists(Path.Combine(dir.FullName, "Apperture.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from test base directory.");
    }
}
