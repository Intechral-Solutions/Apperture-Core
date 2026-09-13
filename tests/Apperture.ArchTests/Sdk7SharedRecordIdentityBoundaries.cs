using NetArchTest.Rules;
using Xunit;

namespace Apperture.ArchTests;

/// <summary>SDK-7 — shared-record identity stays a reference contract, not a platform entity.</summary>
public sealed class Sdk7SharedRecordIdentityBoundaries
{
    [Fact]
    public void ModuleSdk_Integration_Has_No_BusinessRecord_Entity_Type()
    {
        var assembly = typeof(Apperture.Core.Application.OperationItems.SharedRecordIdentity).Assembly;
        var names = assembly.GetTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("SharedRecordIdentity", names);
        Assert.DoesNotContain("BusinessRecord", names);
        Assert.DoesNotContain("BusinessRecordEntity", names);
    }

    [Fact]
    public void Persistence_Has_No_BusinessRecords_Table_Entity()
    {
        var assembly = typeof(Apperture.Persistence.SqlServer.AppertureDbContext).Assembly;
        var businessRecordTypes = assembly.GetTypes()
            .Where(t => t.Name.Contains("BusinessRecord", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FullName)
            .ToList();
        Assert.True(
            businessRecordTypes.Count == 0,
            "SDK-7 must not introduce BusinessRecord persistence entities. Found: "
            + string.Join(", ", businessRecordTypes));
    }

    [Fact]
    public void Sample_Modules_Do_Not_Reference_One_Another()
    {
        var fileImport = Types.InAssembly(typeof(Sample.FileImport.FileImportModule).Assembly)
            .Should()
            .NotHaveDependencyOn("Sample.ApiPolling")
            .GetResult();
        Assert.True(fileImport.IsSuccessful, "FileImport must not reference ApiPolling.");

        var apiPolling = Types.InAssembly(typeof(Sample.ApiPolling.ApiPollingModule).Assembly)
            .Should()
            .NotHaveDependencyOn("Sample.FileImport")
            .GetResult();
        Assert.True(apiPolling.IsSuccessful, "ApiPolling must not reference FileImport.");
    }

    [Fact]
    public void SharedRecordIdentity_Lives_In_ModuleSdk_Not_Ui()
    {
        var uiAssembly = typeof(Apperture.Management.Ui.Shared.SharedRecordLinkCatalog).Assembly;
        Assert.DoesNotContain(
            typeof(Apperture.Core.Application.OperationItems.SharedRecordIdentity).Assembly.GetName().Name,
            uiAssembly.GetReferencedAssemblies().Select(a => a.Name));
    }
}
