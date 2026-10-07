using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.UnitTests.Audit;

/// <summary>
/// The real <see cref="AuditedExecutionStartService"/> over in-memory audit doubles, for controller and service tests that start
/// a run: the start is exercised through its request actor and its audit row rather than around them. The transactional
/// guarantees themselves are proven against SQL Server (OperatorActionAuditSqlServerTests).
/// </summary>
internal static class TestExecutionStarts
{
    public static AuditedExecutionStartService Create(
        IWorkflowStartDispatchService? workflows = null,
        IOperationStartDispatchService? operations = null,
        RecordingAuditedUnitOfWork? unit = null,
        RecordingAuditEventWriter? events = null,
        IPrincipalContext? principal = null) =>
        new(
            unit ?? new RecordingAuditedUnitOfWork(),
            events ?? new RecordingAuditEventWriter(),
            workflows ?? new RefusingWorkflowDispatch(),
            operations ?? new RefusingOperationDispatch(),
            principal ?? new SignedInPrincipal(),
            new AppertureDbContext(new DbContextOptionsBuilder<AppertureDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options));

    private sealed class SignedInPrincipal : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new HumanActor(new ActorId("usr_unit_test_actor"), "test"));
    }

    private sealed class RefusingWorkflowDispatch : IWorkflowStartDispatchService
    {
        public ValueTask<WorkflowStartDispatchResult?> StartAsync(StartWorkflowRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("dispatch should not be called");
    }

    private sealed class RefusingOperationDispatch : IOperationStartDispatchService
    {
        public ValueTask<OperationStartDispatchResult?> StartAsync(StartOperationRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("dispatch should not be called");
    }
}
