using Apperture.Core.Application.Execution;
using Apperture.ModuleSdk.Testing;
using FluentAssertions;
using Sample.FileImport;
using Sample.FileImport.Contracts;
using Xunit;

namespace Apperture.UnitTests.Sample.FileImport;

public sealed class FileImportFixtureAndImportTests
{
    [Fact]
    public void Embedded_fixtures_are_discoverable_and_loadable()
    {
        FileImportEmbeddedFixtures.ListFixtureIds().Should().BeEquivalentTo(FileImportFixtureIds.All);

        foreach (var id in FileImportFixtureIds.All)
        {
            FileImportEmbeddedFixtures.TryLoadCsv(id, out var csv, out var error).Should().BeTrue(error);
            csv.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Unknown_fixture_id_fails_to_load()
    {
        FileImportEmbeddedFixtures.TryLoadCsv("nope", out _, out var error).Should().BeFalse();
        error.Should().Contain("Unknown fixture");
    }

    [Fact]
    public void Invalid_structure_fixture_fails_parse_without_rows()
    {
        FileImportEmbeddedFixtures.TryLoadCsv(FileImportFixtureIds.CustomersInvalidStructure, out var csv, out _).Should().BeTrue();
        var parsed = FileImportCsvParser.Parse(csv);
        parsed.StructureValid.Should().BeFalse();
        parsed.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Valid_fixture_emits_only_import_completed()
    {
        var (store, harness, summary) = await RunImportAsync(
            FileImportFixtureIds.CustomersValid,
            runId: "run-valid");

        summary.status.Should().Be(ImportBatchStatus.Succeeded.ToString());
        summary.signal.Should().Be("import.completed");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle(e => e.SignalName == "import.completed");
        harness.PlatformEvents.EmittedEvents.Should().NotContain(e => e.SignalName == "import.completed.with.errors");
        harness.PlatformEvents.EmittedEvents.Should().NotContain(e => e.SignalName == "import.failed");

        var batch = store.GetRecentBatches(1).Single();
        batch.SucceededCount.Should().Be(3);
        batch.FailedCount.Should().Be(0);
        store.GetOutcomesByBatchId(batch.BatchId).Should().OnlyContain(o => o.Status == ImportRowStatus.Succeeded);
    }

    [Fact]
    public async Task Mixed_fixture_records_row_outcomes_and_emits_only_completed_with_errors()
    {
        var (store, harness, summary) = await RunImportAsync(
            FileImportFixtureIds.CustomersMixed,
            runId: "run-mixed");

        summary.status.Should().Be(ImportBatchStatus.CompletedWithErrors.ToString());
        summary.signal.Should().Be("import.completed.with.errors");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle();
        harness.PlatformEvents.EmittedEvents[0].SignalName.Should().Be("import.completed.with.errors");

        var batch = store.GetRecentBatches(1).Single();
        var outcomes = store.GetOutcomesByBatchId(batch.BatchId);
        outcomes.Should().Contain(o => o.Status == ImportRowStatus.Succeeded);
        outcomes.Should().Contain(o => o.ErrorCode == ImportRowErrorCodes.MissingExternalId);
        outcomes.Should().Contain(o => o.ErrorCode == ImportRowErrorCodes.InvalidEmail);
        outcomes.Should().Contain(o => o.ErrorCode == ImportRowErrorCodes.InvalidStatus);
        outcomes.Should().Contain(o => o.ErrorCode == ImportRowErrorCodes.DuplicateExternalId);
        outcomes.Should().Contain(o => o.ErrorCode == ImportRowErrorCodes.InvalidDate);
        outcomes.Should().Contain(o => o.ErrorCode == ImportRowErrorCodes.ParseError);
        outcomes.Should().OnlyContain(o => o.SafeRowSummary.Length < 200 && !o.SafeRowSummary.Contains(','));
        batch.SucceededCount.Should().BeGreaterThan(0);
        batch.FailedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Structurally_invalid_fixture_fails_in_Parse_without_row_outcomes()
    {
        var store = new InMemoryImportOutcomeStore();
        var module = new FileImportModule(store);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-fi")
            .WithRunId("run-structure")
            .Build();

        var parameters = Params(FileImportFixtureIds.CustomersInvalidStructure);
        await new FileImportValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);

        var act = async () => await new FileImportParseStepHandler(store)
            .ExecuteAsync(harness.CreateStepContext("Parse"), parameters);

        await act.Should().ThrowAsync<OperatorExecutionFailureException>()
            .Where(ex => ex.Message.Contains("Structurally unusable", StringComparison.OrdinalIgnoreCase));

        store.GetRecentBatches(10).Should().BeEmpty();
        harness.PlatformEvents.EmittedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task All_bad_fixture_records_outcomes_and_emits_only_import_failed()
    {
        var (store, harness, summary) = await RunImportAsync(
            FileImportFixtureIds.CustomersAllBad,
            runId: "run-all-bad");

        summary.status.Should().Be(ImportBatchStatus.Failed.ToString());
        summary.signal.Should().Be("import.failed");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle(e => e.SignalName == "import.failed");

        var batch = store.GetRecentBatches(1).Single();
        batch.SucceededCount.Should().Be(0);
        batch.FailedCount.Should().BeGreaterThan(0);
        store.GetOutcomesByBatchId(batch.BatchId).Should().OnlyContain(o => o.Status == ImportRowStatus.Failed);
    }

    [Fact]
    public async Task Strict_mode_mixed_fixture_emits_import_failed()
    {
        var (_, harness, summary) = await RunImportAsync(
            FileImportFixtureIds.CustomersMixed,
            runId: "run-strict",
            strictMode: true);

        summary.status.Should().Be(ImportBatchStatus.Failed.ToString());
        summary.signal.Should().Be("import.failed");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle(e => e.SignalName == "import.failed");
    }

    [Fact]
    public async Task Complete_fails_loud_when_platform_events_disabled()
    {
        var store = new InMemoryImportOutcomeStore();
        var module = new FileImportModule(store);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-fi")
            .WithRunId("run-disabled")
            .WithPlatformEventsEnabled(false)
            .Build();

        var parameters = Params(FileImportFixtureIds.CustomersValid);
        await new FileImportValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);
        await new FileImportParseStepHandler(store).ExecuteAsync(harness.CreateStepContext("Parse"), parameters);
        await new FileImportProcessRowsStepHandler(store).ExecuteAsync(harness.CreateStepContext("ProcessRows"), parameters);

        var act = async () => await new FileImportCompleteStepHandler(store)
            .ExecuteAsync(harness.CreateStepContext("Complete"), parameters);

        await act.Should().ThrowAsync<OperatorExecutionFailureException>()
            .Where(ex => ex.Message.Contains("import.completed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Workflow_definition_id_is_schedule_targetable()
    {
        FileImportModule.DefinitionId.Value.Should().Be("Sample.FileImport.Import");
    }

    private static async Task<(InMemoryImportOutcomeStore store, ModuleTestHarness harness, ImportSummary summary)> RunImportAsync(
        string fixtureId,
        string runId,
        bool? strictMode = null)
    {
        var store = new InMemoryImportOutcomeStore();
        var module = new FileImportModule(store);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-fi")
            .WithRunId(runId)
            .Build();

        var parameters = Params(fixtureId, strictMode);
        await new FileImportValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);
        await new FileImportParseStepHandler(store).ExecuteAsync(harness.CreateStepContext("Parse"), parameters);
        await new FileImportProcessRowsStepHandler(store).ExecuteAsync(harness.CreateStepContext("ProcessRows"), parameters);
        var complete = await new FileImportCompleteStepHandler(store)
            .ExecuteAsync(harness.CreateStepContext("Complete"), parameters);

        complete.Should().NotBeNull();

        var batch = store.GetRecentBatches(1).Single();
        var signal = harness.PlatformEvents.EmittedEvents.Single().SignalName;
        var summary = new ImportSummary(batch.Status.ToString(), signal);

        harness.PlatformEvents.EmittedEvents[0].DataJson.Should().Contain("batchId");
        harness.PlatformEvents.EmittedEvents[0].DataJson.Should().Contain("fixtureId");
        harness.PlatformEvents.EmittedEvents[0].DataJson.Should().Contain("succeededCount");

        return (store, harness, summary);
    }

    private sealed record ImportSummary(string status, string signal);

    private static Dictionary<string, object?> Params(string fixtureId, bool? strictMode = null)
    {
        var dict = new Dictionary<string, object?> { ["fixtureId"] = fixtureId };
        if (strictMode is not null)
            dict["strictMode"] = strictMode.Value;
        return dict;
    }
}
