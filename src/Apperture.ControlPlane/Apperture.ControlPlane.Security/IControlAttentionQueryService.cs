using Apperture.Core.Application.ControlFacade;

namespace Apperture.ControlPlane.Security;

public interface IControlAttentionQueryService
{
    /// <summary>
    /// Cross-domain attention/issue items (minimal, permission-safe, and actionable).
    /// </summary>
    ValueTask<IReadOnlyList<ControlAttentionItemDto>> ListAttentionAsync(
        DateTimeOffset createdFromUtc,
        int take,
        CancellationToken cancellationToken = default);
}

