using Apperture.Core.Application.ResourceContexts;
using Microsoft.AspNetCore.Mvc;
using Client = Apperture.Management.Client;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// The one place the design gate §8.3 status mapping lives. All three movement-capable controllers use
/// it, rather than each carrying its own copy of the same three members.
/// </summary>
internal static class ResourceMoveHttpMapper
{
    /// <summary>
    /// §8.3: the common, resource-less blockers are bad input and get a plain 400. Any family-specific
    /// blocker is a legitimate state the operator must see, so it gets 409 with the full preview body.
    /// </summary>
    private static readonly HashSet<string> CommonBlockerCodes = new(StringComparer.Ordinal)
    {
        ResourceMoveBlockerCodes.TargetNotFound,
        ResourceMoveBlockerCodes.TargetArchived,
        ResourceMoveBlockerCodes.AlreadyInTarget
    };

    public static IActionResult MapMoveResult(ResourceMovePreview preview)
    {
        if (preview.CanMove)
            return new OkObjectResult(ToDto(preview));

        var commonBlocker = preview.Blockers.FirstOrDefault(b => CommonBlockerCodes.Contains(b.Code));
        if (commonBlocker is not null)
            return new BadRequestObjectResult(new { error = commonBlocker.Message });

        return new ConflictObjectResult(ToDto(preview));
    }

    /// <summary>
    /// Plan 2's ownership mutation gate timed out: another ownership change is in progress. Transient and
    /// retryable, so it is a 409 with a stable machine code -- never a 500, and never a raw app-lock error.
    /// </summary>
    public static IActionResult MapBusy(ResourceOwnershipBusyException ex) =>
        new ConflictObjectResult(new { error = ex.Message, code = ex.Code });

    public static Client.ResourceMovePreviewDto ToDto(ResourceMovePreview p) => new(
        p.ResourceType, p.ResourceId, p.Source, p.Target, p.CanMove,
        p.Blockers.Select(b => new Client.ResourceMoveBlockerDto(
            b.Code, b.Message, b.RelatedResourceType, b.RelatedResourceId, b.RelatedDisplayName, b.RelatedResourceContextId)).ToList(),
        p.Effects.Select(e => new Client.ResourceMoveEffectDto(
            e.Code, e.Message, e.RelatedResourceType, e.RelatedResourceIds)).ToList());
}
