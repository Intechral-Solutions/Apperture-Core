using FluentAssertions;
using Sample.ApiPolling;
using Sample.ApiPolling.Contracts;
using Xunit;

namespace Apperture.UnitTests.Sample.ApiPolling;

public sealed class ApiPollingModuleContractTests
{
    [Fact]
    public void Manifest_declares_four_sync_signals_and_poll_procedure()
    {
        var module = new ApiPollingModule();
        var manifest = module.GetManifest();

        manifest.Id.Should().Be("Sample.ApiPolling");
        var signals = manifest.Signals.Should().NotBeNull().And.Subject!;
        signals.Select(s => s.Name).Should().BeEquivalentTo(
            "sync.completed",
            "sync.completed.with.changes",
            "sync.completed.with.errors",
            "sync.failed");

        foreach (var signal in signals)
        {
            signal.ResolvedRequiredDataProperties.Should().Contain(
            [
                "snapshotId", "operationGroupId", "totalRecords", "newCount", "updatedCount",
                "unchangedCount", "deletedCount", "succeededCount", "failedCount", "skippedCount", "status",
            ]);
        }

        var procedure = manifest.Procedures.Should().NotBeNull().And.ContainSingle().Subject;
        procedure.Id.Should().Be("Sample.ApiPolling.procedures.poll");
        procedure.ParameterSchema.Should().Contain("snapshotId");
        procedure.ParameterSchema.Should().Contain("resetState");
        procedure.ParameterSchema.Should().Contain("strictMode");
        procedure.ParameterSchema.Should().Contain("failOnRecordError");
        procedure.TargetDefinitionId.Should().Be(ApiPollingModule.DefinitionId.Value);
    }

    [Fact]
    public void Settings_contributor_registers_five_keys_with_defaults()
    {
        var module = new ApiPollingModule();
        var settings = module.GetIntegrationSettingDefinitions();

        settings.Select(s => s.Key).Should().BeEquivalentTo(
            ApiPollingSettingKeys.PageSize,
            ApiPollingSettingKeys.MaxRecordsPerSync,
            ApiPollingSettingKeys.IncludeDeleted,
            ApiPollingSettingKeys.FailOnRecordError,
            ApiPollingSettingKeys.StrictModeDefault);

        settings.Single(s => s.Key == ApiPollingSettingKeys.PageSize).DefaultValue
            .Should().Be(ApiPollingSettingDefaults.PageSize);
        settings.Single(s => s.Key == ApiPollingSettingKeys.MaxRecordsPerSync).DefaultValue
            .Should().Be(ApiPollingSettingDefaults.MaxRecordsPerSync);
        settings.Single(s => s.Key == ApiPollingSettingKeys.IncludeDeleted).DefaultValue
            .Should().Be(ApiPollingSettingDefaults.IncludeDeleted);
        settings.Single(s => s.Key == ApiPollingSettingKeys.FailOnRecordError).DefaultValue
            .Should().Be(ApiPollingSettingDefaults.FailOnRecordError);
        settings.Single(s => s.Key == ApiPollingSettingKeys.StrictModeDefault).DefaultValue
            .Should().Be(ApiPollingSettingDefaults.StrictModeDefault);
    }

    [Fact]
    public void Store_and_client_abstractions_can_be_swapped()
    {
        var store = new InMemoryApiPollingStateStore();
        var client = new EmbeddedSampleDirectoryApiClient();
        var module = new ApiPollingModule(store, client);
        module.StateStore.Should().BeSameAs(store);
        module.ApiClient.Should().BeSameAs(client);
    }
}
