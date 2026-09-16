using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;

namespace Apperture.Core.Application.ResourceContexts;

/// <summary>
/// Validates and performs explicit resource-ownership moves (design gate §8). Movement never happens
/// as a side effect of an ordinary update — only through this service, backed by
/// <c>GET .../ownership/move-preview</c> and <c>POST .../ownership/move</c> (§8.2).
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Preview*</c> method is advisory and mutates nothing. Every <c>Move*</c> method (CTX-3B Plan 2)
/// re-runs the identical validation inside a transaction, behind the ownership mutation gate, and returns
/// the resulting <see cref="ResourceMovePreview"/> whether or not the move succeeded — callers distinguish
/// success from a blocked attempt via <see cref="ResourceMovePreview.CanMove"/> plus which blocker codes are
/// present (see design gate §8.3 for the 400-vs-409 HTTP mapping the controller applies over this result).
/// </para>
/// <para>
/// This interface is grown one member at a time, each alongside its real implementation. It deliberately
/// never declares a member that throws <c>NotSupportedException</c>.
/// </para>
/// </remarks>
public interface IResourceOwnershipMoveService
{
    ValueTask<ResourceMovePreview> PreviewIntegrationMoveAsync(IntegrationId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default);

    ValueTask<ResourceMovePreview> PreviewConnectionMoveAsync(ConnectionId id, ResourceContextId targetResourceContextId, CancellationToken cancellationToken = default);
}
