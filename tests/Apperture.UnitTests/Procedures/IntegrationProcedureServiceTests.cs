using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.Procedures;
using Apperture.Core.Domain.Workflow;
using Apperture.ControlPlane.Security;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;
using DomainIntegration = Apperture.Core.Domain.Integration.Integration;

namespace Apperture.UnitTests.Procedures;

public sealed class IntegrationProcedureServiceTests
{
    private const string IntegrationKey = "integtestprocedure000001";
    private const string ProcedureId = "Sample.HelloWorld.procedures.greet";
    private const string ModuleId = "Sample.HelloWorld";

    [Fact]
    public async Task LaunchAsync_WhenIntegrationMissing_ReturnsIntegrationNotFound()
    {
        var integrations = new Mock<IIntegrationManagementService>();
        integrations
            .Setup(s => s.GetIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainIntegration?)null);

        var sut = CreateSut(integrations.Object);
        var outcome = await sut.LaunchAsync(
            IntegrationKey,
            ProcedureId,
            new IntegrationProcedureLaunchRequest(null, null, null));

        var failed = outcome.Should().BeOfType<IntegrationProcedureLaunchOutcome.Failed>().Subject;
        failed.Code.Should().Be(IntegrationProcedureLaunchErrorCode.IntegrationNotFound);
    }

    [Fact]
    public async Task LaunchAsync_WhenRequiredParameterMissing_ReturnsInvalidParameters()
    {
        var integration = new DomainIntegration(
            new IntegrationId(IntegrationKey),
            "Test",
            ModuleId,
            null,
            IntegrationState.Active,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var integrations = new Mock<IIntegrationManagementService>();
        integrations
            .Setup(s => s.GetIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readiness = new Mock<IIntegrationReadinessService>();
        readiness
            .Setup(s => s.GetReadinessAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new IntegrationReadinessProjection(IntegrationKey, ModuleId, true, 0, 0, Array.Empty<IntegrationReadinessIssue>()));

        var procedure = new ProcedureDefinition(
            ProcedureId,
            ModuleId,
            "Greet",
            null,
            null,
            ProcedureLaunchKind.Workflow,
            "Sample.HelloWorld.HelloWorld",
            """{"fields":[{"key":"name","type":"string","required":true}]}""",
            null);

        var procedures = new Mock<IProcedureRegistryService>();
        procedures.Setup(s => s.GetProcedureAsync(ProcedureId, It.IsAny<CancellationToken>())).ReturnsAsync(procedure);

        var workflowDefinitions = new Mock<IWorkflowDefinitionResolver>();
        workflowDefinitions
            .Setup(s => s.GetByIdAsync(It.IsAny<WorkflowDefinitionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new WorkflowDefinition(
                    new WorkflowDefinitionId("Sample.HelloWorld.HelloWorld"),
                    "HelloWorld",
                    ModuleId,
                    Array.Empty<string>(),
                    WorkflowCategory.Command,
                    null));

        var principal = new Mock<IPrincipalContext>();
        principal.Setup(s => s.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanActor(new ActorId("a1"), "a1", isBreakGlassSession: false));

        var permissionEvaluator = new Mock<IExecutablePermissionEvaluator>();
        permissionEvaluator
            .Setup(s => s.EvaluateAllAsync(It.IsAny<IActor>(), It.IsAny<IReadOnlyList<PermissionDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var workflowStartAuth = new Mock<IWorkflowStartAuthorizationService>();
        workflowStartAuth
            .Setup(s => s.EnsureCanStartWorkflowAsync(It.IsAny<WorkflowDefinitionId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = CreateSut(
            integrations.Object,
            readiness.Object,
            procedures.Object,
            workflowDefinitions.Object,
            principal.Object,
            permissionEvaluator.Object,
            workflowStartAuth.Object);

        var outcome = await sut.LaunchAsync(
            IntegrationKey,
            ProcedureId,
            new IntegrationProcedureLaunchRequest(new Dictionary<string, object?>(), null, null));

        var failed = outcome.Should().BeOfType<IntegrationProcedureLaunchOutcome.Failed>().Subject;
        failed.Code.Should().Be(IntegrationProcedureLaunchErrorCode.InvalidParameters);
    }

    [Fact]
    public async Task LaunchAsync_WhenIntegrationInactive_ReturnsIntegrationInactive()
    {
        var integration = new DomainIntegration(
            new IntegrationId(IntegrationKey),
            "Test",
            ModuleId,
            null,
            IntegrationState.Inactive,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var integrations = new Mock<IIntegrationManagementService>();
        integrations.Setup(s => s.GetIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readiness = new Mock<IIntegrationReadinessService>();
        readiness.Setup(s => s.GetReadinessAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationReadinessProjection(
                IntegrationKey,
                ModuleId,
                false,
                1,
                0,
                [new IntegrationReadinessIssue(
                    IntegrationReadinessIssueCodes.IntegrationNotActive,
                    "inactive",
                    null,
                    null,
                    null,
                    IntegrationReadinessIssueSeverity.Blocking.ToString())]));

        var sut = CreateSut(integrations.Object, readiness.Object);
        var outcome = await sut.LaunchAsync(IntegrationKey, ProcedureId, new IntegrationProcedureLaunchRequest(null, null, null));
        var failed = outcome.Should().BeOfType<IntegrationProcedureLaunchOutcome.Failed>().Subject;
        failed.Code.Should().Be(IntegrationProcedureLaunchErrorCode.IntegrationInactive);
    }

    [Fact]
    public async Task LaunchAsync_WhenBlockingReadinessIssuesExist_ReturnsIntegrationHardBlocked()
    {
        var integration = new DomainIntegration(
            new IntegrationId(IntegrationKey),
            "Test",
            ModuleId,
            null,
            IntegrationState.Active,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var integrations = new Mock<IIntegrationManagementService>();
        integrations.Setup(s => s.GetIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readiness = new Mock<IIntegrationReadinessService>();
        readiness.Setup(s => s.GetReadinessAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationReadinessProjection(
                IntegrationKey,
                ModuleId,
                false,
                1,
                1,
                [new IntegrationReadinessIssue(
                    IntegrationReadinessIssueCodes.MissingSlotBinding,
                    "missing binding",
                    "Db",
                    null,
                    null,
                    IntegrationReadinessIssueSeverity.Blocking.ToString())]));

        var sut = CreateSut(integrations.Object, readiness.Object);
        var outcome = await sut.LaunchAsync(IntegrationKey, ProcedureId, new IntegrationProcedureLaunchRequest(null, null, null));
        var failed = outcome.Should().BeOfType<IntegrationProcedureLaunchOutcome.Failed>().Subject;
        failed.Code.Should().Be(IntegrationProcedureLaunchErrorCode.IntegrationHardBlocked);
    }

    [Fact]
    public async Task LaunchAsync_WhenOnlyAdvisoryIssuesExist_DoesNotBlockOnReadiness()
    {
        var integration = new DomainIntegration(
            new IntegrationId(IntegrationKey),
            "Test",
            ModuleId,
            null,
            IntegrationState.Active,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var integrations = new Mock<IIntegrationManagementService>();
        integrations.Setup(s => s.GetIntegrationAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(integration);

        var readiness = new Mock<IIntegrationReadinessService>();
        readiness.Setup(s => s.GetReadinessAsync(It.IsAny<IntegrationId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationReadinessProjection(
                IntegrationKey,
                ModuleId,
                true,
                0,
                1,
                [new IntegrationReadinessIssue(
                    IntegrationReadinessIssueCodes.ConnectionVerificationFailed,
                    "advisory",
                    null,
                    null,
                    null,
                    IntegrationReadinessIssueSeverity.Advisory.ToString())]));

        var procedures = new Mock<IProcedureRegistryService>();
        procedures.Setup(s => s.GetProcedureAsync(ProcedureId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProcedureDefinition?)null);

        var sut = CreateSut(integrations.Object, readiness.Object, procedures.Object);
        var outcome = await sut.LaunchAsync(IntegrationKey, ProcedureId, new IntegrationProcedureLaunchRequest(null, null, null));
        var failed = outcome.Should().BeOfType<IntegrationProcedureLaunchOutcome.Failed>().Subject;
        failed.Code.Should().Be(IntegrationProcedureLaunchErrorCode.ProcedureNotFound);
    }

    private static IntegrationProcedureService CreateSut(
        IIntegrationManagementService integrations,
        IIntegrationReadinessService? readiness = null,
        IProcedureRegistryService? procedures = null,
        IWorkflowDefinitionResolver? workflowDefinitions = null,
        IPrincipalContext? principal = null,
        IExecutablePermissionEvaluator? permissionEvaluator = null,
        IWorkflowStartAuthorizationService? workflowStartAuth = null) =>
        new(
            integrations,
            readiness ?? Mock.Of<IIntegrationReadinessService>(),
            procedures ?? Mock.Of<IProcedureRegistryService>(),
            workflowDefinitions ?? Mock.Of<IWorkflowDefinitionResolver>(),
            Mock.Of<IOperationRegistryService>(),
            principal ?? Mock.Of<IPrincipalContext>(),
            permissionEvaluator ?? Mock.Of<IExecutablePermissionEvaluator>(),
            workflowStartAuth ?? Mock.Of<IWorkflowStartAuthorizationService>(),
            Mock.Of<IOperationStartAuthorizationService>(),
            Mock.Of<IWorkflowStartDispatchService>(),
            Mock.Of<IOperationStartDispatchService>());
}
