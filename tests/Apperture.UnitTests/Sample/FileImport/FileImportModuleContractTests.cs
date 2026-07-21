using FluentAssertions;
using Sample.FileImport;
using Sample.FileImport.Contracts;
using Xunit;

namespace Apperture.UnitTests.Sample.FileImport;

public sealed class FileImportModuleContractTests
{
    [Fact]
    public void Manifest_declares_three_import_signals_and_import_procedure()
    {
        var module = new FileImportModule();
        var manifest = module.GetManifest();

        manifest.Id.Should().Be("Sample.FileImport");
        var signals = manifest.Signals.Should().NotBeNull().And.Subject!;
        signals.Select(s => s.Name).Should().BeEquivalentTo(
            "import.completed",
            "import.completed.with.errors",
            "import.failed");

        foreach (var signal in signals)
        {
            signal.ResolvedRequiredDataProperties.Should().Contain(
            [
                "fixtureId", "batchId", "totalRows", "succeededCount", "failedCount", "skippedCount", "status",
            ]);
        }

        var procedure = manifest.Procedures.Should().NotBeNull().And.ContainSingle().Subject;
        procedure.Id.Should().Be("Sample.FileImport.procedures.import");
        procedure.ParameterSchema.Should().Contain("fixtureId");
        procedure.ParameterSchema.Should().Contain("strictMode");
        procedure.TargetDefinitionId.Should().Be(FileImportModule.DefinitionId.Value);
    }

    [Fact]
    public void Settings_contributor_registers_three_keys_with_defaults()
    {
        var module = new FileImportModule();
        var settings = module.GetSettingDefinitions();

        settings.Select(s => s.Key).Should().BeEquivalentTo(
            FileImportSettingKeys.StrictModeDefault,
            FileImportSettingKeys.MaxRowsPerImport,
            FileImportSettingKeys.FailOnFirstError);

        settings.Should().OnlyContain(s => !s.IsRequired);

        settings.Single(s => s.Key == FileImportSettingKeys.StrictModeDefault).DefaultValue
            .Should().Be(FileImportSettingDefaults.StrictModeDefault);
        settings.Single(s => s.Key == FileImportSettingKeys.MaxRowsPerImport).DefaultValue
            .Should().Be(FileImportSettingDefaults.MaxRowsPerImport);
        settings.Single(s => s.Key == FileImportSettingKeys.FailOnFirstError).DefaultValue
            .Should().Be(FileImportSettingDefaults.FailOnFirstError);
    }

    [Fact]
    public void Store_abstraction_can_be_swapped()
    {
        var fake = new CountingStore();
        var module = new FileImportModule(fake);
        module.OutcomeStore.Should().BeSameAs(fake);
    }

    private sealed class CountingStore : IImportOutcomeStore
    {
        public ImportBatch CreateBatch(ImportBatchCreateRequest request) =>
            new("b1", request.FixtureId, request.RunId, request.CorrelationId, DateTimeOffset.UtcNow, null,
                ImportBatchStatus.Running, 0, 0, 0, 0);

        public void AppendRowOutcome(ImportRowOutcome outcome) { }

        public ImportBatch CompleteBatch(
            string batchId,
            ImportBatchStatus status,
            int totalRows,
            int succeededCount,
            int failedCount,
            int skippedCount,
            DateTimeOffset? completedAtUtc = null) =>
            new(batchId, "x", null, null, DateTimeOffset.UtcNow, completedAtUtc, status, totalRows, succeededCount,
                failedCount, skippedCount);

        public ImportBatch? GetBatch(string batchId) => null;

        public ImportBatch? GetRunningBatchByRunId(string runId) => null;

        public IReadOnlyList<ImportRowOutcome> GetOutcomesByBatchId(string batchId) => [];

        public IReadOnlyList<ImportBatch> GetRecentBatches(int limit) => [];
    }
}
