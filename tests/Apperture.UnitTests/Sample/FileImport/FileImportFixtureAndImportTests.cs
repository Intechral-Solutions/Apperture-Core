using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
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
    public async Task Valid_fixture_writes_succeeded_operation_group_and_items()
    {
        var (store, opStore, harness, summary) = await RunImportAsync(
            FileImportFixtureIds.CustomersValid,
            runId: "run-valid");

        summary.status.Should().Be(ImportBatchStatus.Succeeded.ToString());
        summary.signal.Should().Be("import.completed");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle(e => e.SignalName == "import.completed");

        var batch = store.GetRecentBatches(1).Single();
        batch.SucceededCount.Should().Be(3);
        batch.FailedCount.Should().Be(0);
        batch.OperationGroupId.Should().NotBeNullOrWhiteSpace();
        store.GetOutcomesByBatchId(batch.BatchId).Should().OnlyContain(o => o.Status == ImportRowStatus.Succeeded);

        var groups = await opStore.GetGroupsByRunAsync("run-valid");
        groups.Should().ContainSingle();
        groups[0].Status.Should().Be(OperationGroupStatuses.Succeeded);
        groups[0].OperationKind.Should().Be("file_import");
        groups[0].SourceLabel.Should().Be(FileImportFixtureIds.CustomersValid);
        groups[0].SucceededCount.Should().Be(3);
        groups[0].FailedCount.Should().Be(0);
        groups[0].SummaryJson.Should().Contain(FileImportFixtureIds.CustomersValid);
        groups[0].SummaryJson.Should().NotContain("ExternalId,Name");

        var items = await opStore.GetItemsByGroupAsync(groups[0].OperationGroupId);
        items.Should().HaveCount(3);
        items.Should().OnlyContain(i => i.Status == OperationItemStatuses.Succeeded && !i.IsRetryable);
        items.Should().Contain(i => i.SubjectKey == "C-001" && i.DisplayLabel == "Ada Lovelace");
        items.Should().Contain(i => i.SubjectKey == "C-002" && i.DisplayLabel == "Alan Turing");
        items.Should().Contain(i => i.SubjectKey == "C-003" && i.DisplayLabel == "Grace Hopper");
        foreach (var item in items)
        {
            item.MetadataJson.Should().Contain("\"recordType\":\"customer\"");
            item.MetadataJson.Should().Contain($"\"businessKey\":\"{item.SubjectKey}\"");
            item.MetadataJson.Should().Contain("\"changeKind\":\"imported\"");
            item.MetadataJson.Should().Contain("\"observedAt\"");
            item.DisplayLabel.Should().NotBe(item.SubjectKey);
            (item.ChangeSummaryJson is null || !item.ChangeSummaryJson.Contains("ExternalId,Name")).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Mixed_fixture_records_row_outcomes_and_platform_items()
    {
        var (store, opStore, harness, summary) = await RunImportAsync(
            FileImportFixtureIds.CustomersMixed,
            runId: "run-mixed");

        summary.status.Should().Be(ImportBatchStatus.CompletedWithErrors.ToString());
        summary.signal.Should().Be("import.completed.with.errors");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle();
        harness.PlatformEvents.EmittedEvents[0].SignalName.Should().Be("import.completed.with.errors");
        harness.PlatformEvents.EmittedEvents[0].DataJson.Should().Contain("operationGroupId");

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

        var group = (await opStore.GetGroupsByRunAsync("run-mixed")).Single();
        group.Status.Should().Be(OperationGroupStatuses.CompletedWithErrors);
        group.TotalCount.Should().Be(batch.TotalRows);
        group.SucceededCount.Should().Be(batch.SucceededCount);
        group.FailedCount.Should().Be(batch.FailedCount);
        group.SkippedCount.Should().Be(batch.SkippedCount);

        var items = await opStore.GetItemsByGroupAsync(group.OperationGroupId);
        items.Should().HaveCount(batch.TotalRows);
        items.Should().Contain(i => i.Status == OperationItemStatuses.Succeeded);
        items.Should().Contain(i => i.Status == OperationItemStatuses.Failed && i.IsRetryable && i.ErrorCode == ImportRowErrorCodes.InvalidEmail);
        items.Should().OnlyContain(i => i.RetryCount == 0);
        foreach (var item in items)
        {
            (item.ChangeSummaryJson is null
             || (!item.ChangeSummaryJson.Contains("rawCsv") && item.ChangeSummaryJson.Length < 500)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Structurally_invalid_fixture_fails_in_Parse_without_row_outcomes_or_groups()
    {
        var store = new InMemoryImportOutcomeStore();
        var opStore = new InMemoryOperationItemStore();
        var module = new FileImportModule(store);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-fi")
            .WithRunId("run-structure")
            .WithOperationItemStore(opStore)
            .Build();

        var parameters = Params(FileImportFixtureIds.CustomersInvalidStructure);
        await new FileImportValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);

        var act = async () => await new FileImportParseStepHandler(store)
            .ExecuteAsync(harness.CreateStepContext("Parse"), parameters);

        await act.Should().ThrowAsync<OperatorExecutionFailureException>()
            .Where(ex => ex.Message.Contains("Structurally unusable", StringComparison.OrdinalIgnoreCase));

        store.GetRecentBatches(10).Should().BeEmpty();
        (await opStore.GetGroupsByRunAsync("run-structure")).Should().BeEmpty();
        harness.PlatformEvents.EmittedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task All_bad_fixture_writes_failed_group_and_retryable_items()
    {
        var (store, opStore, harness, summary) = await RunImportAsync(
            FileImportFixtureIds.CustomersAllBad,
            runId: "run-all-bad");

        summary.status.Should().Be(ImportBatchStatus.Failed.ToString());
        summary.signal.Should().Be("import.failed");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle(e => e.SignalName == "import.failed");

        var batch = store.GetRecentBatches(1).Single();
        batch.SucceededCount.Should().Be(0);
        batch.FailedCount.Should().BeGreaterThan(0);
        store.GetOutcomesByBatchId(batch.BatchId).Should().OnlyContain(o => o.Status == ImportRowStatus.Failed);

        var group = (await opStore.GetGroupsByRunAsync("run-all-bad")).Single();
        group.Status.Should().Be(OperationGroupStatuses.Failed);
        group.FailedCount.Should().Be(batch.FailedCount);

        var items = await opStore.GetItemsByGroupAsync(group.OperationGroupId);
        items.Should().OnlyContain(i => i.Status == OperationItemStatuses.Failed && i.IsRetryable);
        items.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.ErrorCode) && !string.IsNullOrWhiteSpace(i.ErrorMessage));
    }

    [Fact]
    public async Task Strict_mode_mixed_fixture_emits_import_failed()
    {
        var (_, opStore, harness, summary) = await RunImportAsync(
            FileImportFixtureIds.CustomersMixed,
            runId: "run-strict",
            strictMode: true);

        summary.status.Should().Be(ImportBatchStatus.Failed.ToString());
        summary.signal.Should().Be("import.failed");
        harness.PlatformEvents.EmittedEvents.Should().ContainSingle(e => e.SignalName == "import.failed");

        var group = (await opStore.GetGroupsByRunAsync("run-strict")).Single();
        group.Status.Should().Be(OperationGroupStatuses.Failed);
    }

    [Fact]
    public async Task Max_rows_creates_skipped_operation_items()
    {
        var (store, opStore, _, _) = await RunImportAsync(
            FileImportFixtureIds.CustomersValid,
            runId: "run-max-rows",
            maxRows: 1);

        var batch = store.GetRecentBatches(1).Single();
        batch.SucceededCount.Should().Be(1);
        batch.SkippedCount.Should().Be(2);

        var group = (await opStore.GetGroupsByRunAsync("run-max-rows")).Single();
        group.SkippedCount.Should().Be(2);

        var items = await opStore.GetItemsByGroupAsync(group.OperationGroupId);
        items.Should().Contain(i => i.Status == OperationItemStatuses.Skipped && !i.IsRetryable
                                                   && i.ErrorCode == ImportRowErrorCodes.SkippedMaxRows);
        items.Count(i => i.Status == OperationItemStatuses.Succeeded).Should().Be(1);
    }

    [Fact]
    public async Task Complete_fails_loud_when_platform_events_disabled()
    {
        var store = new InMemoryImportOutcomeStore();
        var opStore = new InMemoryOperationItemStore();
        var module = new FileImportModule(store);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-fi")
            .WithRunId("run-disabled")
            .WithOperationItemStore(opStore)
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

        // Group + local batch complete before fail-loud signal (same CompleteBatch-then-emit timing as SDK-2).
        store.GetRecentBatches(1).Single().Status.Should().Be(ImportBatchStatus.Succeeded);
        var groups = await opStore.GetGroupsByRunAsync("run-disabled");
        groups.Should().ContainSingle();
        groups[0].Status.Should().Be(OperationGroupStatuses.Succeeded);
    }

    [Fact]
    public async Task Parse_fails_loudly_when_operation_item_store_missing()
    {
        var store = new InMemoryImportOutcomeStore();
        var module = new FileImportModule(store);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-fi")
            .WithRunId("run-no-opstore")
            .Build();

        var parameters = Params(FileImportFixtureIds.CustomersValid);
        await new FileImportValidateStepHandler().ExecuteAsync(harness.CreateStepContext("Validate"), parameters);

        var act = async () => await new FileImportParseStepHandler(store)
            .ExecuteAsync(harness.CreateStepContext("Parse"), parameters);

        await act.Should().ThrowAsync<OperatorExecutionFailureException>()
            .Where(ex => ex.Message.Contains("operation itemization", StringComparison.OrdinalIgnoreCase));

        store.GetRecentBatches(10).Should().BeEmpty();
    }

    [Fact]
    public void Workflow_definition_id_is_schedule_targetable()
    {
        FileImportModule.DefinitionId.Value.Should().Be("Sample.FileImport.Import");
    }

    private static async Task<(InMemoryImportOutcomeStore store, InMemoryOperationItemStore opStore, ModuleTestHarness harness, ImportSummary summary)> RunImportAsync(
        string fixtureId,
        string runId,
        bool? strictMode = null,
        int? maxRows = null)
    {
        var store = new InMemoryImportOutcomeStore();
        var opStore = new InMemoryOperationItemStore();
        var module = new FileImportModule(store);
        var harness = ModuleTestHarness.ForModule(module)
            .WithIntegrationId("int-fi")
            .WithRunId(runId)
            .WithProcedureId("Sample.FileImport.procedures.import")
            .WithOperationItemStore(opStore)
            .Build();

        var parameters = Params(fixtureId, strictMode, maxRows);
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

        var runSummary = await opStore.GetRunSummaryAsync(runId);
        runSummary.HasOperationItems.Should().BeTrue();
        runSummary.GroupCount.Should().Be(1);

        return (store, opStore, harness, summary);
    }

    private sealed record ImportSummary(string status, string signal);

    private static Dictionary<string, object?> Params(string fixtureId, bool? strictMode = null, int? maxRows = null)
    {
        var dict = new Dictionary<string, object?> { ["fixtureId"] = fixtureId };
        if (strictMode is not null)
            dict["strictMode"] = strictMode.Value;
        if (maxRows is not null)
            dict["maxRowsPerImport"] = maxRows.Value;
        return dict;
    }
}
