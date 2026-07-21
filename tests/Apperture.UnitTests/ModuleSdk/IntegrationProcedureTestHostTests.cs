using System.Data;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.CampusNexus.Models;
using Apperture.Core.Application.OperationItems;
using Apperture.ModuleSdk.Integration.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proof.CampusNexus.StudentReview;
using Sample.ApiPolling;
using Sample.FileImport;
using Sample.HelloWorld;
using Unisa.CampusNexus.PaymentPlans;
using Xunit;

namespace Apperture.UnitTests.ModuleSdk;

public sealed class IntegrationProcedureTestHostTests
{
    [Fact]
    public async Task HelloWorld_greet_executes_and_emits_demo_event()
    {
        var module = new HelloWorldModule();
        var execution = await IntegrationProcedureTestHost
            .For(module)
            .WithSlot("Db", new Func<object?>(() => new StubDbConnection()))
            .ExecuteProcedureAsync("Sample.HelloWorld.procedures.greet", new Dictionary<string, object?> { ["name"] = "SDK-6B" });

        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Succeeded);
        execution.PublishedEvents.Should().ContainSingle();
        execution.PublishedEvents[0].SignalName.Should().Be("demo.completed");
    }

    [Fact]
    public async Task FileImport_mixed_fixture_records_items_and_retry_child_run()
    {
        var module = new FileImportModule();
        var retryHandler = new FileImportOperationItemRetryHandler();
        var host = IntegrationProcedureTestHost.For(module, [retryHandler]);

        var execution = await host.ExecuteProcedureAsync(
            "Sample.FileImport.procedures.import",
            new Dictionary<string, object?> { ["fixtureId"] = "customers-mixed" });

        execution.OperationGroups.Should().ContainSingle();
        execution.OperationGroups[0].Status.Should().Be(OperationGroupStatuses.CompletedWithErrors);
        var retryItem = execution.OperationItems.First(x => x.Status == OperationItemStatuses.Failed && x.IsRetryable);

        var retryExecution = await host.ExecuteRetryAsync(retryItem, execution.OperationGroups[0], "test-fileimport-retry");

        retryExecution.Identity.RunId.Should().NotBe(execution.Identity.RunId);
        retryExecution.Identity.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ApiPolling_error_snapshot_records_partial_failure_and_retry_child_run()
    {
        var module = new ApiPollingModule();
        var retryHandler = new ApiPollingOperationItemRetryHandler();
        var host = IntegrationProcedureTestHost.For(module, [retryHandler]);

        _ = await host.ExecuteProcedureAsync(
            "Sample.ApiPolling.procedures.poll",
            new Dictionary<string, object?> { ["snapshotId"] = "snapshot-initial", ["resetState"] = true });

        var execution = await host.ExecuteProcedureAsync(
            "Sample.ApiPolling.procedures.poll",
            new Dictionary<string, object?> { ["snapshotId"] = "snapshot-errors" });

        execution.OperationGroups.Should().ContainSingle();
        execution.OperationGroups[0].Status.Should().Be(OperationGroupStatuses.CompletedWithErrors);
        var retryItem = execution.OperationItems.First(x => x.Status == OperationItemStatuses.Failed && x.IsRetryable);

        var retryExecution = await host.ExecuteRetryAsync(retryItem, execution.OperationGroups[0], "test-apipolling-retry");

        retryExecution.Identity.RunId.Should().NotBe(execution.Identity.RunId);
    }

    [Fact]
    public async Task Proof_procedure_executes_through_persistence_adapter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IProofStudentReviewPersistenceAdapter, FakeProofPersistenceAdapter>();
        using var provider = services.BuildServiceProvider();

        var module = new ProofCampusNexusStudentReviewModule(provider.GetRequiredService<IServiceScopeFactory>(), NullLoggerFactory.Instance);
        var execution = await IntegrationProcedureTestHost
            .For(module)
            .WithSlot(ProofCampusNexusStudentReviewConstants.SlotIntegrationDb, new Func<object?>(() => new StubDbConnection()))
            .WithSlot(ProofCampusNexusStudentReviewConstants.SlotCnsApi, new FakeCampusNexusApiClient())
            .ExecuteProcedureAsync(ProofCampusNexusStudentReviewConstants.ProcedureSyncRecentStudents);

        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Succeeded);
        execution.Logs.Should().Contain(x => x.Code == "proof.studentReview.sync.completed");
    }

    [Fact]
    public async Task Unisa_workflow_executes_without_full_host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IUnisaPaymentPlanManagementService, UnisaPaymentPlanManagementService>();
        using var provider = services.BuildServiceProvider();

        var module = new UnisaCampusNexusPaymentPlansModule(provider.GetRequiredService<IServiceScopeFactory>());
        var execution = await IntegrationProcedureTestHost
            .For(module)
            .ExecuteWorkflowDefinitionAsync("Unisa.CampusNexus.PaymentPlans.ProcessDailyFiles");

        execution.Outcome.Kind.Should().Be(ProcedureOutcomeKind.Succeeded);
        execution.Result.Should().NotBeNull();
    }

    private sealed class FakeProofPersistenceAdapter : IProofStudentReviewPersistenceAdapter
    {
        public Task<ProofStudentReviewPersistenceResult> CompleteRunAsync(
            ProofStudentReviewPersistenceRun run,
            IReadOnlyList<ProofStudentReviewSnapshotInput> rows,
            string? correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProofStudentReviewPersistenceResult(rows.Count, rows.Count, 0, rows.Max(x => x.LastModifiedDateTime)));

        public Task MarkRunFailedAsync(ProofStudentReviewPersistenceRun run, string error, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ProofStudentReviewPersistenceRun> StartRunAsync(
            string connectionString,
            string integrationId,
            string runId,
            string entitySet,
            string odataQuery,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProofStudentReviewPersistenceRun(Guid.NewGuid(), connectionString));
    }

    private sealed class FakeCampusNexusApiClient : ICampusNexusApiClient
    {
        public string ColumnInFilter(string columnName, IEnumerable<string> values) => "";
        public string ColumnInFilter(string columnName, string commaSeparatedValues) => "";
        public void Dispose() { }
        public Task<ApiCommandResponse> ExecuteCommandAsync(ApiCommand apiCommand, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T?> ExecuteCommandAsync<T>(ApiCommand apiCommand, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T?> QueryFirstAsync<T>(ApiQuery apiQuery, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<T>> QueryAsync<T>(ApiQuery apiQuery, CancellationToken cancellationToken = default)
        {
            object[] rows =
            [
                new CnsStudentReviewODataRow
                {
                    Id = 1,
                    StudentNumber = "S-001",
                    FirstName = "Ada",
                    LastName = "Lovelace",
                    CreatedDateTime = DateTimeOffset.UtcNow.AddDays(-1),
                    LastModifiedDateTime = DateTimeOffset.UtcNow
                },
                new CnsStudentReviewODataRow
                {
                    Id = 2,
                    StudentNumber = "S-002",
                    FirstName = "Grace",
                    LastName = "Hopper",
                    CreatedDateTime = DateTimeOffset.UtcNow.AddDays(-2),
                    LastModifiedDateTime = DateTimeOffset.UtcNow.AddMinutes(-1)
                }
            ];
            return Task.FromResult((IReadOnlyList<T>)rows.Cast<T>().ToList());
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
