using System.Data;
using Apperture.Core.Application.Execution;
using Apperture.Core.Application.OperationItems;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.ModuleSdk.Integration.Testing;
using FluentAssertions;
using Apperture.Core.Domain.Procedures;
using Apperture.RuntimePlane.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Sample.ApiPolling;
using Sample.FileImport;
using Sample.HelloWorld;
using Xunit;

namespace Apperture.UnitTests.ModuleSdk;

public sealed class IntegrationProcedureTestHostToolkitContractMatrixTests
{
    private const string TimeProviderSlotName = "TestTimeProvider";
    private const string NormalSettingSlotName = "normal.setting";
    private const string SecretSlotName = "secret.plaintext";

    [Fact]
    public async Task Procedure_execution_partial_success_is_derived_from_operation_group_status()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "itemization",
                ["strictFailOnAnyFailure"] = false,
                ["itemSpecs"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Succeeded, false, null, null),
                    new ToolkitContractsModule.ItemSpec(2, OperationItemStatuses.Failed, true, "E1", "boom", "succeedOnRetry")
                }
            },
            timeProvider: timeProvider);

        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.PartialSuccess);
        execution.Outcome.Totals.Should().NotBeNull();
        execution.Outcome.Totals!.TotalCount.Should().Be(2);
        execution.Outcome.Totals.FailedCount.Should().Be(1);
        execution.Outcome.Totals.SucceededCount.Should().Be(1);
    }

    [Fact]
    public async Task Procedure_execution_explicit_failed_outcome_translates_operator_failure()
    {
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => new TestTimeProvider()));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "throwOperatorFailure",
            });

        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Failed);
        execution.Outcome.ErrorCode.Should().Be("operator_execution_failed");
        execution.Outcome.ErrorMessage.Should().Be("explicit fail");
        execution.Outcome.Summary.Should().BeNull();
    }

    [Fact]
    public async Task Procedure_execution_unexpected_exception_translates_to_unexpected_exception()
    {
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => new TestTimeProvider()));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "throwUnexpected",
            });

        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Failed);
        execution.Outcome.ErrorCode.Should().Be("unexpected_exception");
        execution.Outcome.Summary.Should().BeNull();
        execution.Outcome.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public async Task Procedure_execution_cancellation_is_not_misclassified_as_failure()
    {
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule());
        var timeProvider = new TestTimeProvider();
        host.WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "cancelDuringExecution"
            },
            cancellationToken: cts.Token,
            timeProvider: timeProvider);

        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Cancelled);
    }

    [Fact]
    public async Task Procedure_execution_cancellation_during_execution_is_cancelled()
    {
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule());
        var timeProvider = new TestTimeProvider();
        host.WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var entered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        host.WithSlot("CancelEnteredSignal", entered);

        var executionTask = host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "cancelDuringExecution"
            },
            cancellationToken: cts.Token,
            timeProvider: timeProvider);

        await entered.Task.ConfigureAwait(false);
        cts.Cancel();
        var execution = await executionTask.ConfigureAwait(false);
        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Cancelled);
    }

    [Fact]
    public async Task Procedure_execution_deterministic_timestamps_are_captured_for_events_and_operation_items()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "deterministicTimestamps",
                ["itemSpecs"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Succeeded, false, null, null)
                }
            },
            timeProvider: timeProvider);

        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Succeeded);
        execution.PublishedEvents.Should().ContainSingle();
        execution.PublishedEvents[0].OccurredAtUtc.Should().Be(timeProvider.GetUtcNow());

        execution.OperationItems.Should().HaveCount(1);
        execution.OperationItems[0].CompletedAtUtc.Should().Be(timeProvider.GetUtcNow());
        execution.OperationItems[0].LastAttemptedAtUtc.Should().Be(timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task Procedure_execution_manual_and_scheduled_origin_propagate_identity_into_events()
    {
        var timeProvider = new TestTimeProvider();
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var scheduledId = IntegrationExecutionIdentity.Create(
            ToolkitContractsModule.ModuleId,
            procedureId: ToolkitContractsModule.ProcedureId,
            origin: IntegrationExecutionOrigin.Scheduled,
            scheduleId: "sched-1",
            runId: "run-1",
            correlationId: "corr-1");

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "emitTwoEvents"
            },
            identity: scheduledId,
            timeProvider: timeProvider);

        execution.PublishedEvents.Single(e => e.SignalName == "toolkit.event.one").ScheduleId.Should().Be("sched-1");
        execution.PublishedEvents.Single(e => e.SignalName == "toolkit.event.two").CorrelationId.Should().Be("corr-1");
        execution.Identity.Origin.Should().Be(IntegrationExecutionOrigin.Scheduled);
    }

    [Fact]
    public async Task Event_capture_records_events_in_emit_order_and_includes_run_identity()
    {
        var timeProvider = new TestTimeProvider();
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "emitTwoEvents"
            },
            timeProvider: timeProvider);

        execution.Identity.Origin.Should().Be(IntegrationExecutionOrigin.Manual);
        execution.Identity.ScheduleId.Should().BeNull();
        execution.PublishedEvents.Select(e => e.SignalName).Should().ContainInOrder("toolkit.event.one", "toolkit.event.two");
        execution.PublishedEvents[0].RunId.Should().Be(execution.Identity.RunId);
        execution.PublishedEvents[0].CorrelationId.Should().Be(execution.Identity.CorrelationId);
    }

    [Fact]
    public async Task Settings_and_secrets_capture_omits_plaintext_secrets_everywhere()
    {
        var secret = "super-secret-value";
        var timeProvider = new TestTimeProvider();
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider))
            .WithSlot(NormalSettingSlotName, new Func<object?>(() => "normal-value"))
            .WithSlot(SecretSlotName, new Func<object?>(() => secret));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "settingsSecrets",
                ["itemSpecs"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Succeeded, false, null, null)
                }
            },
            timeProvider: timeProvider);

        // Event payload
        var payload = (Dictionary<string, object?>)execution.PublishedEvents.Single().Payload!;
        payload.Should().ContainKey("normalSetting");
        payload["normalSetting"].Should().Be("normal-value");
        execution.PublishedEvents[0].Payload!.ToString().Should().NotContain(secret);

        // Operational logs
        execution.Logs.Any(l => l.Message.Contains(secret)).Should().BeFalse();

        // Operation metadata
        execution.OperationItems.Select(i => i.MetadataJson).Should().NotContain(s => s.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Settings_and_secrets_capture_omits_keys_when_setting_and_secret_are_missing()
    {
        var timeProvider = new TestTimeProvider();
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "settingsSecrets",
                ["itemSpecs"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Succeeded, false, null, null)
                }
            },
            timeProvider: timeProvider);

        var payload = (Dictionary<string, object?>)execution.PublishedEvents.Single().Payload!;
        payload.ContainsKey("normalSetting").Should().BeFalse();
        payload.ContainsKey("secretRef").Should().BeFalse();

        execution.Logs.Any(l => l.Data?.ContainsKey("hasSecret") == true && (bool)l.Data!["hasSecret"] == true)
            .Should().BeFalse();
        execution.Logs.Any(l => l.Data?.ContainsKey("hasNormalSetting") == true && (bool)l.Data!["hasNormalSetting"] == true)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Settings_and_secrets_capture_omits_plaintext_secret_when_setting_is_missing_but_secret_is_present()
    {
        var secret = "super-secret-value";
        var timeProvider = new TestTimeProvider();
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider))
            .WithSlot(SecretSlotName, new Func<object?>(() => secret));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "settingsSecrets",
                ["itemSpecs"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Succeeded, false, null, null)
                }
            },
            timeProvider: timeProvider);

        var payload = (Dictionary<string, object?>)execution.PublishedEvents.Single().Payload!;
        payload.ContainsKey("normalSetting").Should().BeFalse();
        payload.ContainsKey("secretRef").Should().BeTrue();
        execution.PublishedEvents[0].Payload!.ToString().Should().NotContain(secret);
        execution.OperationItems.Select(i => i.MetadataJson).Should().NotContain(s => s.Contains(secret, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, new[] { OperationItemStatuses.Succeeded, OperationItemStatuses.Succeeded }, OperationGroupStatuses.Succeeded)]
    [InlineData(false, new[] { OperationItemStatuses.Succeeded, OperationItemStatuses.Failed }, OperationGroupStatuses.CompletedWithErrors)]
    [InlineData(true, new[] { OperationItemStatuses.Failed, OperationItemStatuses.Failed }, OperationGroupStatuses.Failed)]
    [InlineData(false, new[] { OperationItemStatuses.Skipped, OperationItemStatuses.Skipped }, OperationGroupStatuses.Succeeded)]
    public async Task Operation_groups_and_items_cover_all_statuses_and_invariant(
        bool strictFailOnAnyFailure,
        string[] itemStatuses,
        string expectedGroupStatus)
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var specs = itemStatuses
            .Select((s, i) =>
                new ToolkitContractsModule.ItemSpec(
                    i + 1,
                    s,
                    s == OperationItemStatuses.Failed,
                    s == OperationItemStatuses.Failed ? "E" + (i + 1) : null,
                    s == OperationItemStatuses.Failed ? "err" : null,
                    "succeedOnRetry"))
            .ToArray();

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "itemization",
                ["strictFailOnAnyFailure"] = strictFailOnAnyFailure,
                ["itemSpecs"] = specs
            },
            timeProvider: timeProvider);

        execution.OperationGroups.Should().ContainSingle();
        var group = execution.OperationGroups[0];
        group.Status.Should().Be(expectedGroupStatus);
        group.TotalCount.Should().Be(itemStatuses.Length);
        group.TotalCount.Should().Be(group.SucceededCount + group.FailedCount + group.SkippedCount);

        execution.OperationItems.Select(i => i.Sequence).Should().Equal(specs.Select(x => x.Sequence));
        execution.OperationItems.Count.Should().Be(itemStatuses.Length);
    }

    [Fact]
    public async Task Operation_items_empty_group_completes_as_succeeded_and_emits_no_items()
    {
        var timeProvider = new TestTimeProvider();
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule())
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "itemization",
                ["strictFailOnAnyFailure"] = false,
                ["itemSpecs"] = Array.Empty<ToolkitContractsModule.ItemSpec>()
            },
            timeProvider: timeProvider);

        execution.OperationGroups.Should().ContainSingle();
        execution.OperationGroups[0].Status.Should().Be(OperationGroupStatuses.Succeeded);
        execution.OperationGroups[0].TotalCount.Should().Be(0);
        execution.OperationItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Retry_matrix_retryable_failed_item_is_accepted_and_retry_success_updates_totals_and_retry_count()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var retryHandler = new ToolkitContractsOperationItemRetryHandler();

        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule(), [retryHandler])
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "retryParent",
                ["strictFailOnAnyFailure"] = false,
                ["retryParentItems"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Failed, true, "E-parent", "parent failed", "succeed"),
                    new ToolkitContractsModule.ItemSpec(2, OperationItemStatuses.Failed, false, "E-nonretry", "non retry failed", "succeed"),
                    new ToolkitContractsModule.ItemSpec(3, OperationItemStatuses.Succeeded, false, null, null, "n/a"),
                }
            },
            timeProvider: timeProvider);

        var group = execution.OperationGroups.Single();
        var retryableFailed = execution.OperationItems.Single(i => i.Status == OperationItemStatuses.Failed && i.IsRetryable);

        var retryExecution = await host.ExecuteRetryAsync(
            retryableFailed,
            group,
            clientIdempotencyKey: "idempotent",
            timeProvider: timeProvider,
            cancellationToken: default);

        retryExecution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Succeeded);
        retryExecution.OperationGroups[0].SucceededCount.Should().Be(1);
        retryExecution.OperationItems[0].RetryCount.Should().Be(retryableFailed.RetryCount + 1);
        retryExecution.OperationItems[0].ErrorCode.Should().BeNull();

        // Deterministic timestamp (no time advance for this scenario)
        retryExecution.OperationItems[0].CompletedAtUtc.Should().Be(timeProvider.GetUtcNow());
        retryExecution.Identity.CorrelationId.Should().NotBeNullOrWhiteSpace();
        retryExecution.Identity.CorrelationId.Should().NotBe(execution.Identity.CorrelationId);
    }

    [Fact]
    public async Task Retry_matrix_retry_fails_again_preserves_failure_evidence_and_records_retry_metadata_keys()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var retryHandler = new ToolkitContractsOperationItemRetryHandler();
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule(), [retryHandler])
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "retryParent",
                ["strictFailOnAnyFailure"] = false,
                ["retryParentItems"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Failed, true, "E-again", "still failing", "failsAgain")
                }
            },
            timeProvider: timeProvider);

        var group = execution.OperationGroups.Single();
        var retryableFailed = execution.OperationItems.Single();

        // Advance time deterministically before retry.
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        var retryExecution = await host.ExecuteRetryAsync(
            retryableFailed,
            group,
            clientIdempotencyKey: "idempotent-2",
            timeProvider: timeProvider);

        retryExecution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.PartialSuccess);

        var retryItem = retryExecution.OperationItems.Single();
        retryItem.Status.Should().Be(OperationItemStatuses.Failed);
        retryItem.ErrorCode.Should().Be("E-again");
        retryItem.ErrorMessage.Should().Be("still failing");
        retryItem.RetryCount.Should().Be(retryableFailed.RetryCount + 1);

        retryItem.MetadataJson.Should().Contain("\"retryAttemptId\"");
        retryItem.MetadataJson.Should().Contain("\"parentOperationItemId\"");

        // Deterministic retry timestamp.
        retryItem.CompletedAtUtc.Should().Be(timeProvider.GetUtcNow());
        retryItem.LastAttemptedAtUtc.Should().Be(timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task Retry_matrix_full_run_retry_is_distinct_from_item_level_retry_metadata()
    {
        var timeProvider = new TestTimeProvider();
        var retryHandler = new ToolkitContractsOperationItemRetryHandler();
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule(), [retryHandler])
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var parentExecution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "retryParent",
                ["strictFailOnAnyFailure"] = false,
                ["retryParentItems"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Failed, true, "E-parent", "parent failed", "succeed")
                }
            },
            timeProvider: timeProvider);

        var group = parentExecution.OperationGroups.Single();
        var retryableFailed = parentExecution.OperationItems.Single();

        var retryExecution = await host.ExecuteRetryAsync(
            retryableFailed,
            group,
            clientIdempotencyKey: "idempotent-3",
            timeProvider: timeProvider);

        var fullRunRetryExecution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "retryParent",
                ["strictFailOnAnyFailure"] = false,
                ["retryParentItems"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Failed, true, "E-parent", "parent failed", "succeed")
                }
            },
            timeProvider: timeProvider);

        retryExecution.OperationItems[0].MetadataJson.Should().Contain("retryAttemptId");
        fullRunRetryExecution.OperationItems[0].MetadataJson.Should().NotContain("retryAttemptId");
        fullRunRetryExecution.Identity.CorrelationId.Should().NotBe(retryExecution.Identity.CorrelationId);
    }

    [Fact]
    public async Task Retry_matrix_non_retryable_failed_or_success_items_are_rejected()
    {
        var timeProvider = new TestTimeProvider();
        var retryHandler = new ToolkitContractsOperationItemRetryHandler();
        var host = IntegrationProcedureTestHost.For(new ToolkitContractsModule(), [retryHandler])
            .WithSlot(TimeProviderSlotName, new Func<object?>(() => timeProvider));

        var execution = await host.ExecuteProcedureAsync(
            ToolkitContractsModule.ProcedureId,
            new Dictionary<string, object?>
            {
                ["mode"] = "retryParent",
                ["strictFailOnAnyFailure"] = false,
                ["retryParentItems"] = new[]
                {
                    new ToolkitContractsModule.ItemSpec(1, OperationItemStatuses.Failed, false, "E-nonretry", "non retry failed", "succeed"),
                    new ToolkitContractsModule.ItemSpec(2, OperationItemStatuses.Succeeded, false, null, null, "n/a"),
                }
            },
            timeProvider: timeProvider);

        var group = execution.OperationGroups.Single();
        var nonRetryableFailed = execution.OperationItems.Single(i => i.Sequence == 1);
        var succeededItem = execution.OperationItems.Single(i => i.Sequence == 2);

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.ExecuteRetryAsync(nonRetryableFailed, group, "id", timeProvider: timeProvider));
        ex1.Message.Should().Contain("not retryable");

        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.ExecuteRetryAsync(succeededItem, group, "id", timeProvider: timeProvider));
        ex2.Message.Should().Contain("not Failed");
    }

    // ------------------------------------------------------------
    // Representative module adoption checks (toolkit host)
    // ------------------------------------------------------------

    [Fact]
    public async Task HelloWorld_minimal_execution_emits_demo_event()
    {
        var execution = await IntegrationProcedureTestHost.For(new HelloWorldModule())
            .WithSlot("Db", new Func<object?>(() => new StubDbConnection()))
            .ExecuteProcedureAsync("Sample.HelloWorld.procedures.greet", new Dictionary<string, object?> { ["name"] = "SDK-6B" });

        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Succeeded);
        execution.PublishedEvents.Should().ContainSingle(e => e.SignalName == "demo.completed");
        execution.OperationGroups.Should().BeEmpty();
        execution.OperationItems.Should().BeEmpty();
    }

    [Fact]
    public async Task FileImport_valid_fixture_emits_completion_once_and_has_succeeded_group_status()
    {
        var module = new FileImportModule();
        var host = IntegrationProcedureTestHost.For(module)
            .WithSlot("Db", new Func<object?>(() => new StubDbConnection()));

        var execution = await host.ExecuteProcedureAsync(
            "Sample.FileImport.procedures.import",
            new Dictionary<string, object?> { ["fixtureId"] = "customers-valid" });

        execution.OperationGroups.Single().Status.Should().Be(OperationGroupStatuses.Succeeded);
        execution.PublishedEvents.Count.Should().Be(1);
        execution.PublishedEvents[0].SignalName.Should().Be("import.completed");
        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Succeeded);
    }

    [Fact]
    public async Task FileImport_mixed_fixture_emits_partial_failure_event_once_and_has_partial_success_outcome()
    {
        var module = new FileImportModule();
        var host = IntegrationProcedureTestHost.For(module)
            .WithSlot("Db", new Func<object?>(() => new StubDbConnection()));

        var execution = await host.ExecuteProcedureAsync(
            "Sample.FileImport.procedures.import",
            new Dictionary<string, object?> { ["fixtureId"] = "customers-mixed" });

        execution.OperationGroups.Single().Status.Should().Be(OperationGroupStatuses.CompletedWithErrors);
        execution.PublishedEvents.Count.Should().Be(1);
        execution.PublishedEvents[0].SignalName.Should().Be("import.completed.with.errors");
        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.PartialSuccess);
    }

    [Fact]
    public async Task ApiPolling_success_emits_success_event_once_and_has_succeeded_group_status()
    {
        var module = new ApiPollingModule();
        var host = IntegrationProcedureTestHost.For(module)
            .WithSlot("Db", new Func<object?>(() => new StubDbConnection()));

        _ = await host.ExecuteProcedureAsync(
            "Sample.ApiPolling.procedures.poll",
            new Dictionary<string, object?> { ["snapshotId"] = "snapshot-initial", ["resetState"] = true });

        var execution = await host.ExecuteProcedureAsync(
            "Sample.ApiPolling.procedures.poll",
            new Dictionary<string, object?> { ["snapshotId"] = "snapshot-initial" });

        execution.OperationGroups.Single().Status.Should().Be(OperationGroupStatuses.Succeeded);
        execution.PublishedEvents.Count.Should().Be(1);
        execution.PublishedEvents[0].SignalName.Should().Be("sync.completed");
        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Succeeded);
    }

    // ------------------------------------------------------------
    // Testing-only module + retry handler
    // ------------------------------------------------------------

    internal sealed class ToolkitContractsModule : IModule, IModuleStepHandlerRegistrar
    {
        public const string ModuleId = "Test.ToolkitContracts";
        public const string ProcedureId = "Test.ToolkitContracts.procedures.run";
        public const string WorkflowDefinitionIdValue = "Test.ToolkitContracts.Main";
        public const string OperationKind = "test_contracts";

        public static readonly WorkflowDefinitionId WorkflowDefinitionId = new(WorkflowDefinitionIdValue);

        public ModuleManifest GetManifest() => new(ModuleId, "ToolkitContracts", "1.0.0", "Testing module for SDK-6B toolkit contracts.")
        {
            Signals =
            [
                new ModuleSignalDeclaration("toolkit.event.one", "One", "Emit order test.", "info", RequiredDataProperties: ["message"]),
                new ModuleSignalDeclaration("toolkit.event.two", "Two", "Emit order test.", "info", RequiredDataProperties: ["message"]),
            ],
            Procedures =
            [
                new ModuleProcedureDeclaration(
                    ProcedureId,
                    "Run",
                    "Runs toolkit contract scenarios",
                    Category: "Toolkit",
                    ProcedureLaunchKind.Workflow,
                    WorkflowDefinitionId.Value,
                    """{"fields":[{"key":"mode","type":"string","required":true}]}"""),
            ]
        };

        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(CancellationToken cancellationToken = default)
        {
            yield return new WorkflowDefinition(
                WorkflowDefinitionId,
                "Run",
                ModuleId,
                new[] { "Run" },
                WorkflowCategory.Command,
                RequiredPermissions: []
            );

            await Task.CompletedTask.ConfigureAwait(false);
        }

        public void RegisterStepHandlers(IStepHandlerRegistry registry)
        {
            registry.Register(ModuleId, "Run", new RunStepHandler());
        }

        internal sealed record ItemSpec(
            int Sequence,
            string Status,
            bool IsRetryable,
            string? ErrorCode,
            string? ErrorMessage,
            string? RetryOutcome = null);

        internal sealed class RunStepHandler : IStepHandler
        {
            public async ValueTask<object?> ExecuteAsync(
                IStepExecutionContext context,
                IReadOnlyDictionary<string, object?> parameters,
                CancellationToken cancellationToken = default)
            {
                var mode = parameters.TryGetValue("mode", out var m) ? m as string : null;
                mode = string.IsNullOrWhiteSpace(mode) ? "itemization" : mode;

                // Used for deterministic timestamps tests.
                var timeProvider = await context.ResolveSlotAsync<TimeProvider>("TestTimeProvider", cancellationToken)
                    .ConfigureAwait(false);
                var utcNow = timeProvider?.GetUtcNow() ?? DateTimeOffset.UtcNow;

                if (string.Equals(mode, "throwOperatorFailure", StringComparison.Ordinal))
                {
                    throw OperatorExecutionResult.FailSummaryOnly("explicit fail");
                }

                if (string.Equals(mode, "throwUnexpected", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("boom");
                }

                if (string.Equals(mode, "cancelDuringExecution", StringComparison.Ordinal))
                {
                    var enteredSignal = await context.ResolveSlotAsync<TaskCompletionSource<object?>>(
                            "CancelEnteredSignal",
                            cancellationToken)
                        .ConfigureAwait(false);
                    enteredSignal?.TrySetResult(null);

                    var tcs = new TaskCompletionSource<object?>();
                    using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
                    return await tcs.Task.ConfigureAwait(false);
                }

                // Settings/secrets capture uses slots provided by the test host.
                var normalSetting = await context.ResolveSlotAsync<string>("normal.setting", cancellationToken)
                    .ConfigureAwait(false);
                var secretPlaintext = await context.ResolveSlotAsync<string>("secret.plaintext", cancellationToken)
                    .ConfigureAwait(false);

                if (string.Equals(mode, "emitTwoEvents", StringComparison.Ordinal))
                {
                    await context.Signals.Events.EmitAsync(
                        context.Signals.Correlation,
                        "toolkit.event.one",
                        new Dictionary<string, object?> { ["message"] = "first" },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    await context.Signals.Events.EmitAsync(
                        context.Signals.Correlation,
                        "toolkit.event.two",
                        new Dictionary<string, object?> { ["message"] = "second" },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    return OperatorExecutionResult.SuccessInline(null);
                }

                if (string.Equals(mode, "deterministicTimestamps", StringComparison.Ordinal))
                {
                    await context.Signals.Events.EmitAsync(
                        context.Signals.Correlation,
                        "toolkit.event.one",
                        new Dictionary<string, object?> { ["message"] = "timestamp-test" },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                if (string.Equals(mode, "settingsSecrets", StringComparison.Ordinal))
                {
                    // Contract: do not emit secret plaintext; emit only presence or a derived "secretRef".
                    var secretRef = secretPlaintext is null
                        ? null
                        : "secretRef-" + secretPlaintext.Length;

                    var payload = new Dictionary<string, object?>
                    {
                        ["normalSetting"] = normalSetting,
                        ["secretRef"] = secretRef,
                    };
                    // Normalize missing values to null so the payload structure stays deterministic.
                    if (normalSetting is null)
                        payload.Remove("normalSetting");
                    if (secretRef is null)
                        payload.Remove("secretRef");

                    await context.Signals.Events.EmitAsync(
                        context.Signals.Correlation,
                        "toolkit.event.one",
                        payload,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    context.Signals.Logs.LogOperational(
                        OperationalLogLevel.Information,
                        "toolkit.settings.secrets",
                        "captured settings and secrets",
                        new Dictionary<string, object?>
                        {
                            ["hasSecret"] = secretPlaintext is not null,
                            ["hasNormalSetting"] = normalSetting is not null
                        });
                }

                var strictFailOnAnyFailure = parameters.TryGetValue("strictFailOnAnyFailure", out var s)
                    && s is true;

                var itemSpecs = parameters.TryGetValue("itemSpecs", out var specsObj)
                    ? specsObj as ToolkitContractsModule.ItemSpec[]
                    : Array.Empty<ToolkitContractsModule.ItemSpec>();

                if (string.Equals(mode, "retryParent", StringComparison.Ordinal))
                {
                    itemSpecs = parameters["retryParentItems"] as ToolkitContractsModule.ItemSpec[] ?? Array.Empty<ToolkitContractsModule.ItemSpec>();
                }

                if (parameters.TryGetValue(OperationItemRetryParameterKeys.RetryMode, out var retryModeObj)
                    && retryModeObj is true)
                {
                    var retryAttemptId = parameters.TryGetValue(OperationItemRetryParameterKeys.RetryAttemptId, out var ra)
                        ? ra as string
                        : throw new InvalidOperationException("retryAttemptId missing");
                    var parentOperationItemId = parameters.TryGetValue(OperationItemRetryParameterKeys.ParentOperationItemId, out var po)
                        ? po as string
                        : throw new InvalidOperationException("parentOperationItemId missing");

                    var retryOutcome = parameters.TryGetValue("retryOutcome", out var ro) ? ro as string : "succeed";
                    var retryCount = parameters.TryGetValue("retryCount", out var rc) && rc is int i ? i : 1;
                    var retryErrorCode = parameters.TryGetValue("retryErrorCode", out var rec) ? rec as string : null;
                    var retryErrorMessage = parameters.TryGetValue("retryErrorMessage", out var rem) ? rem as string : null;

                    var group = await context.Signals.OperationItems.BeginGroupAsync(
                            ToolkitContractsModule.OperationKind,
                            sourceLabel: "retry",
                            summaryJson: null,
                            metadataJson: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    var itemStatus = string.Equals(retryOutcome, "failsAgain", StringComparison.Ordinal)
                        ? OperationItemStatuses.Failed
                        : OperationItemStatuses.Succeeded;

                    var item = OperationItemReporting.BuildAppendRequest(
                        group.OperationGroupId,
                        context.Signals.Correlation.RunId ?? context.RunId!,
                        sequence: 1,
                        itemStatus,
                        subjectKey: null,
                        displayLabel: null,
                        errorCode: itemStatus == OperationItemStatuses.Failed ? retryErrorCode : null,
                        errorMessage: itemStatus == OperationItemStatuses.Failed ? retryErrorMessage : null,
                        isRetryable: true,
                        changeSummary: new { retryOutcome },
                        metadata: new
                        {
                            retryAttemptId,
                            parentOperationItemId,
                            retryOutcome
                        },
                        completedAtUtc: utcNow);

                    var updatedItem = item with
                    {
                        RetryCount = retryCount,
                        LastAttemptedAtUtc = utcNow,
                        CompletedAtUtc = utcNow,
                    };

                    await context.Signals.OperationItems.AppendItemAsync(updatedItem, cancellationToken).ConfigureAwait(false);

                    var aggregator = new OperationGroupAggregator();
                    aggregator.Record(itemStatus);
                    var totals = aggregator.ToTotals();
                    var complete = aggregator.ToCompleteRequest(
                        group.OperationGroupId,
                        strictFailOnAnyFailure: strictFailOnAnyFailure,
                        summaryJson: null,
                        completedAtUtc: utcNow);

                    await context.Signals.OperationItems.CompleteGroupAsync(complete, cancellationToken).ConfigureAwait(false);

                    return OperatorExecutionResult.SuccessInline(new { retryOutcome });
                }

                if (itemSpecs.Length == 0 && string.Equals(mode, "itemization", StringComparison.Ordinal))
                {
                    // Empty group scenario.
                    var group = await context.Signals.OperationItems.BeginGroupAsync(
                            ToolkitContractsModule.OperationKind,
                            sourceLabel: "empty",
                            summaryJson: null,
                            metadataJson: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    var aggregator = new OperationGroupAggregator();
                    var complete = aggregator.ToCompleteRequest(group.OperationGroupId, strictFailOnAnyFailure: strictFailOnAnyFailure, summaryJson: null, completedAtUtc: utcNow);
                    await context.Signals.OperationItems.CompleteGroupAsync(complete, cancellationToken).ConfigureAwait(false);
                    return OperatorExecutionResult.SuccessInline(null);
                }

                var g = await context.Signals.OperationItems.BeginGroupAsync(
                        ToolkitContractsModule.OperationKind,
                        sourceLabel: mode,
                        summaryJson: null,
                        metadataJson: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var aggregator2 = new OperationGroupAggregator();
                foreach (var spec in itemSpecs)
                {
                    aggregator2.Record(spec.Status);
                    // Note: metadata must be safe for SDK-6A/6B; the testing module only emits "safe" metadata keys.
                    var appendRequest = OperationItemReporting.BuildAppendRequest(
                        g.OperationGroupId,
                        context.Signals.Correlation.RunId ?? context.RunId!,
                        sequence: spec.Sequence,
                        itemStatus: spec.Status,
                        subjectKey: null,
                        displayLabel: null,
                        errorCode: spec.Status == OperationItemStatuses.Failed ? spec.ErrorCode : null,
                        errorMessage: spec.Status == OperationItemStatuses.Failed ? spec.ErrorMessage : null,
                        isRetryable: spec.IsRetryable,
                        changeSummary: new { status = spec.Status, retryOutcome = spec.RetryOutcome },
                        metadata: new { status = spec.Status, retryOutcome = spec.RetryOutcome },
                        completedAtUtc: utcNow);

                    await context.Signals.OperationItems.AppendItemAsync(appendRequest, cancellationToken).ConfigureAwait(false);
                }

                var complete2 = aggregator2.ToCompleteRequest(
                    g.OperationGroupId,
                    strictFailOnAnyFailure: strictFailOnAnyFailure,
                    summaryJson: null,
                    completedAtUtc: utcNow);

                await context.Signals.OperationItems.CompleteGroupAsync(complete2, cancellationToken).ConfigureAwait(false);
                return OperatorExecutionResult.SuccessInline(new { total = itemSpecs.Length });
            }
        }
    }

    internal sealed class ToolkitContractsOperationItemRetryHandler : IOperationItemRetryHandler
    {
        public string ModuleId => ToolkitContractsModule.ModuleId;
        public string OperationKind => ToolkitContractsModule.OperationKind;

        public bool Supports(OperationItem item, OperationGroup group) =>
            string.Equals(item.RunId, item.RunId, StringComparison.Ordinal)
            && string.Equals(group.ModuleId, ModuleId, StringComparison.Ordinal)
            && string.Equals(group.OperationKind, OperationKind, StringComparison.Ordinal);

        public OperationItemRetryEligibility Evaluate(OperationItem item, OperationGroup group)
        {
            _ = group;

            if (!string.Equals(item.Status, OperationItemStatuses.Failed, StringComparison.Ordinal))
                return new OperationItemRetryEligibility(false, "NotFailed", "Item status is not Failed.");

            if (!item.IsRetryable)
                return new OperationItemRetryEligibility(false, "NotRetryable", "Item is not retryable.");

            // retryOutcome is embedded by the testing module in safe metadata.
            var outcome = item.MetadataJson ?? "{}";
            var retryOutcome =
                outcome.Contains("\"retryOutcome\":\"failsAgain\"", StringComparison.Ordinal) ? "failsAgain" :
                outcome.Contains("\"retryOutcome\":\"succeed\"", StringComparison.Ordinal) ? "succeed" :
                outcome.Contains("\"retryOutcome\":\"succeedOnRetry\"", StringComparison.Ordinal) ? "succeedOnRetry" :
                outcome.Contains("\"retryOutcome\":\"succeedOnRetry\"", StringComparison.Ordinal) ? "succeedOnRetry" :
                "succeed";

            return new OperationItemRetryEligibility(true);
        }

        public OperationItemRetryStartPlan BuildStartPlan(OperationItemRetryBuildContext context)
        {
            // This handler interprets retryOutcome from the metadata JSON.
            var retryOutcomeJson = context.Item.MetadataJson ?? "{}";
            var retryOutcome = retryOutcomeJson.Contains("\"retryOutcome\":\"failsAgain\"", StringComparison.Ordinal)
                ? "failsAgain"
                : retryOutcomeJson.Contains("\"retryOutcome\":\"succeed\"", StringComparison.Ordinal)
                    ? "succeed"
                    : "succeed";

            var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [OperationItemRetryParameterKeys.RetryMode] = true,
                [OperationItemRetryParameterKeys.RetryAttemptId] = context.RetryAttemptId,
                [OperationItemRetryParameterKeys.ParentOperationItemId] = context.Item.OperationItemId,
                // Deterministic retry count contract: parent retry count bumps exactly once.
                ["retryCount"] = context.Item.RetryCount + 1,
                ["retryOutcome"] = retryOutcome,
                ["retryErrorCode"] = context.Item.ErrorCode,
                ["retryErrorMessage"] = context.Item.ErrorMessage
            };

            return new OperationItemRetryStartPlan(
                WorkflowDefinitionId: ToolkitContractsModule.WorkflowDefinitionId,
                Parameters: parameters,
                IntegrationId: context.Group.IntegrationId,
                ProcedureId: ToolkitContractsModule.ProcedureId);
        }
    }

    private sealed class StubDbConnection : IDbConnection
    {
        public string ConnectionString { get; set; } = "Server=(local);Database=Test;";
        public int ConnectionTimeout => 1;
        public string Database => "Test";
        public ConnectionState State { get; private set; } = ConnectionState.Closed;
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) { }
        public void Close() => State = ConnectionState.Closed;
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() => State = ConnectionState.Open;
        public void Dispose() => Close();
    }
}

