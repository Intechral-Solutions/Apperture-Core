using Apperture.Core.Application.ControlFacade;
using Apperture.Core.Security.Trust;
using Apperture.RuntimePlane.Abstractions;

namespace Apperture.ControlPlane.Security;

public interface IControlActionAvailabilityProjection
{
    ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectExecutionRunActionsAsync(
        RunSummaryDto run,
        bool hasQueuedPark,
        bool hasQueuedResume,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectOperationDefinitionActionsAsync(
        OperationDefinitionDto operation,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectWorkflowDefinitionActionsAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ControlActionAvailabilityDto>> ProjectRuntimeInstanceActionsAsync(
        RuntimeIdentity runtime,
        CancellationToken cancellationToken = default);
}

