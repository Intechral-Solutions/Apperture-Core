using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using System.Reflection;
using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Runtime;

public sealed class OperationProcessorDefinitionResolutionAuditTests
{
    [Fact]
    public async Task TryAuditDefinitionUnavailableAsync_PassesResourceContextIdThrough()
    {
        await using var db = CreateDb();
        var registry = new StepHandlerRegistry();
        var executor = new DefaultWorkflowExecutor(new NoOpSlotResolver(), registry);
        var operationRegistry = new NullOperationRegistry();
        var audit = new CapturingAuditService();

        var processor = new OperationStartRequestProcessor(
            db,
            executor,
            operationRegistry,
            "worker",
            Options.Create(new OperationStartRequestLeaseOptions { LeaseTimeout = TimeSpan.FromMinutes(5) }),
            audit: audit);

        var ex = new DefinitionNotExecutableException("op.def", ExecutionDefinitionKind.Operation, "not effectively active");
        await InvokeAudit(processor, ex, "run-1", "app-a");

        audit.LastRecord.Should().NotBeNull();
        audit.LastRecord!.ResourceContextId.Should().Be("app-a");
    }

    private static async Task InvokeAudit(
        OperationStartRequestProcessor processor, Exception ex, string runId, string? resourceContextId)
    {
        var method = typeof(OperationStartRequestProcessor).GetMethod(
            "TryAuditDefinitionUnavailableAsync",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(Exception), typeof(string), typeof(string), typeof(CancellationToken)],
            null);
        if (method is null)
            throw new InvalidOperationException("TryAuditDefinitionUnavailableAsync not found");
        var result = method.Invoke(processor, new object?[] { ex, runId, resourceContextId, CancellationToken.None });
        if (result is ValueTask vt)
            await vt.ConfigureAwait(false);
        else
            throw new InvalidOperationException($"Unexpected return type {result?.GetType().FullName}");
    }

    private static AppertureDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppertureDbContext(options);
    }

    private sealed class NoOpSlotResolver : IRuntimeSlotResolver
    {
        public ValueTask<T?> ResolveSlotAsync<T>(string moduleId, string slotName, CancellationToken cancellationToken = default)
            where T : class =>
            ResolveSlotAsync<T>(moduleId, slotName, null, cancellationToken);

        public ValueTask<T?> ResolveSlotAsync<T>(
            string moduleId,
            string slotName,
            string? integrationId,
            CancellationToken cancellationToken = default)
            where T : class =>
            ValueTask.FromResult<T?>(null);
    }

    private sealed class NullOperationRegistry : IOperationRegistryService
    {
        public ValueTask<IReadOnlyList<OperationDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>([]);
        public ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationDefinition?>(null);
        public ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinition>>([]);
        public ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class CapturingAuditService : IAuditService
    {
        public Apperture.Core.Domain.Audit.AuditRecord? LastRecord { get; private set; }
        public ValueTask AppendAsync(Apperture.Core.Domain.Audit.AuditRecord record, CancellationToken cancellationToken = default)
        {
            LastRecord = record;
            return ValueTask.CompletedTask;
        }
        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuditPage([], null));
        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask<Apperture.Core.Domain.Audit.AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Core.Domain.Audit.AuditRecord?>(null);
    }
}
