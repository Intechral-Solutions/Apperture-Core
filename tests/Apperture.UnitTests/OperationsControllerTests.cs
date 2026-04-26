using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Shared.Kernel;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests;

/// <summary>
/// Operation start API boundary: request is built from route + body; OrchestrationSafeValidator is applied to parameters.
/// </summary>
public sealed class OperationsControllerTests
{
    private static IOperationStartAuthorizationService CreateAllowAllAuthService()
    {
        var mock = new MockOperationStartAuthorizationService();
        return mock;
    }

    private static IOperationStartDispatchService CreateDispatchService(Func<StartOperationRequest, OperationStartDispatchResult?> handler) =>
        new MockOperationStartDispatchService(handler);

    private static IPrincipalContext StubPrincipal() =>
        new StubPrincipalContext(new HumanActor(new ActorId("test"), "test"));

    [Fact]
    public async Task Start_WhenBodyContainsSecretBearingParameter_Returns400BadRequest()
    {
        var controller = new OperationsController(
            new StubOperationDefinitionQueryService(),
            CreateAllowAllAuthService(),
            new StubOperationInputValidationService(),
            CreateDispatchService(_ => throw new InvalidOperationException("dispatch should not be called")),
            new StubRunQueryService(),
            StubPrincipal());

        var resolvedSecret = new ResolvedSecret(
            new SecretRef("x", null),
            new SecretMaterial(SecretMaterialType.OpaqueString, OpaqueString: "secret"),
            Version: null);

        var body = new StartOperationRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["apiKey"] = resolvedSecret }
        };

        var result = await controller.Start("operation-1", body, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Start_WhenBodyHasSafeParameters_Returns202Accepted()
    {
        var controller = new OperationsController(
            new StubOperationDefinitionQueryService(),
            CreateAllowAllAuthService(),
            new StubOperationInputValidationService(),
            CreateDispatchService(req =>
                new OperationStartDispatchResult(
                    new OperationRunId("run-1"),
                    new OperationInstanceId("inst-1"),
                    new Apperture.Core.Domain.Operations.OperationId(req.OperationDefinitionId),
                    "requested")),
            new StubRunQueryService(),
            StubPrincipal());

        var body = new StartOperationRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["id"] = "safe-value" },
            IdempotencyKey = "key-1",
            IntegrationId = "integration-1"
        };

        var result = await controller.Start("operation-1", body, CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task Start_WhenBodyIsNull_Returns400BadRequest()
    {
        var controller = new OperationsController(
            new StubOperationDefinitionQueryService(),
            CreateAllowAllAuthService(),
            new StubOperationInputValidationService(),
            CreateDispatchService(req =>
                new OperationStartDispatchResult(
                    new OperationRunId("run-1"),
                    new OperationInstanceId("inst-1"),
                    new Apperture.Core.Domain.Operations.OperationId(req.OperationDefinitionId),
                    "requested")),
            new StubRunQueryService(),
            StubPrincipal());

        var result = await controller.Start("operation-1", null, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Start_WhenIntegrationIdMissing_Returns400BadRequest()
    {
        var controller = new OperationsController(
            new StubOperationDefinitionQueryService(),
            CreateAllowAllAuthService(),
            new StubOperationInputValidationService(),
            CreateDispatchService(_ => throw new InvalidOperationException("dispatch should not be called")),
            new StubRunQueryService(),
            StubPrincipal());

        var body = new StartOperationRequestBody
        {
            Parameters = new Dictionary<string, object?> { ["id"] = "safe-value" }
        };

        var result = await controller.Start("operation-1", body, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        private readonly IActor? _actor;
        public StubPrincipalContext(IActor? actor) => _actor = actor;
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_actor);
    }

    private sealed class MockOperationStartAuthorizationService : IOperationStartAuthorizationService
    {
        public ValueTask EnsureCanStartOperationAsync(Apperture.Core.Domain.Operations.OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class MockOperationStartDispatchService : IOperationStartDispatchService
    {
        private readonly Func<StartOperationRequest, OperationStartDispatchResult?> _handler;

        public MockOperationStartDispatchService(Func<StartOperationRequest, OperationStartDispatchResult?> handler) =>
            _handler = handler;

        public ValueTask<OperationStartDispatchResult?> StartAsync(StartOperationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(request));
    }

    private sealed class StubOperationDefinitionQueryService : IOperationDefinitionQueryService
    {
        public ValueTask<IReadOnlyList<OperationDefinitionDto>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<OperationDefinitionDto>>(Array.Empty<OperationDefinitionDto>());

        public ValueTask<OperationDefinitionDto?> GetAsync(Apperture.Core.Domain.Operations.OperationId operationDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<OperationDefinitionDto?>(null);
    }

    private sealed class StubOperationInputValidationService : IOperationInputValidationService
    {
        public ValueTask ValidateAsync(Apperture.Core.Domain.Operations.OperationId operationId, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    [Fact]
    public async Task GetOperations_WhenUnauthorized_Returns403()
    {
        var controller = new OperationsController(
            new ThrowingOperationDefinitionQueryService(),
            CreateAllowAllAuthService(),
            new StubOperationInputValidationService(),
            CreateDispatchService(_ => null),
            new StubRunQueryService(),
            StubPrincipal());

        var result = await controller.GetOperations(CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetOperationRun_WhenRunIsWorkflow_Returns404()
    {
        var controller = new OperationsController(
            new StubOperationDefinitionQueryService(),
            CreateAllowAllAuthService(),
            new StubOperationInputValidationService(),
            CreateDispatchService(_ => null),
            new StubRunQueryService(detail: new RunDetailDto(
                "r1", "i1", "wf1", "running", DateTimeOffset.UtcNow, null, null, null, null,
                new RunResultDto("in_progress", "Execution is still in progress.", null, null),
                null,
                Array.Empty<RunLifecycleEventDto>(),
                Array.Empty<RunStepInstanceDto>(),
                Array.Empty<RunControlRequestDto>(),
                null, null, null, null, null, null,
                "workflow",
                null,
                null,
                null)),
            StubPrincipal());

        var result = await controller.GetOperationRun("r1", CancellationToken.None);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetOperationRun_WhenRunIsOperation_Returns200()
    {
        var controller = new OperationsController(
            new StubOperationDefinitionQueryService(),
            CreateAllowAllAuthService(),
            new StubOperationInputValidationService(),
            CreateDispatchService(_ => null),
            new StubRunQueryService(detail: new RunDetailDto(
                "r1", "oi1", "op1", "completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null,
                new RunResultDto("succeeded", "ok", null, null),
                null,
                Array.Empty<RunLifecycleEventDto>(),
                Array.Empty<RunStepInstanceDto>(),
                Array.Empty<RunControlRequestDto>(),
                null, null, null, null, null, null,
                "operation",
                null,
                null,
                null)),
            StubPrincipal());

        var result = await controller.GetOperationRun("r1", CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    private sealed class ThrowingOperationDefinitionQueryService : IOperationDefinitionQueryService
    {
        public ValueTask<IReadOnlyList<OperationDefinitionDto>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException();
        public ValueTask<OperationDefinitionDto?> GetAsync(Apperture.Core.Domain.Operations.OperationId operationDefinitionId, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException();
    }

    private sealed class StubRunQueryService : IRunQueryService
    {
        private readonly RunDetailDto? _detail;
        public StubRunQueryService(RunDetailDto? detail = null) => _detail = detail;
        public ValueTask<IReadOnlyList<RunSummaryDto>> ListRunsAsync(RunQueryFilter? filter = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<RunSummaryDto>>(Array.Empty<RunSummaryDto>());

        public ValueTask<IReadOnlyDictionary<string, FleetOperationalRunSignals>> GetFleetOperationalRunSignalsAsync(
            IReadOnlyList<string> integrationIds,
            DateTimeOffset sinceUtc,
            int maxRows,
            CancellationToken cancellationToken = default)
        {
            var d = integrationIds.Distinct(StringComparer.Ordinal).ToDictionary(
                id => id,
                _ => new FleetOperationalRunSignals(0, null, null, null, null, null),
                StringComparer.Ordinal);
            return ValueTask.FromResult<IReadOnlyDictionary<string, FleetOperationalRunSignals>>(d);
        }

        public ValueTask<IReadOnlyDictionary<string, FleetScheduleOriginRunEvidence>> GetLatestAuthorizedScheduleOriginRunsAsync(
            IReadOnlyList<string> integrationIds,
            DateTimeOffset sinceUtc,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, FleetScheduleOriginRunEvidence>>(
                new Dictionary<string, FleetScheduleOriginRunEvidence>(StringComparer.Ordinal));

        public ValueTask<IReadOnlyDictionary<string, IReadOnlyList<FleetScheduleOriginRunEvidence>>> GetAuthorizedScheduleOriginRunsAsync(
            IReadOnlyList<string> integrationIds,
            DateTimeOffset sinceUtc,
            int maxRunsPerIntegration,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, IReadOnlyList<FleetScheduleOriginRunEvidence>>>(
                new Dictionary<string, IReadOnlyList<FleetScheduleOriginRunEvidence>>(StringComparer.Ordinal));

        public ValueTask<RunDetailDto?> GetRunAsync(OperationRunId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_detail);
    }
}

