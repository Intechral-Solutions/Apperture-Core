using Apperture.Core.Application.Services;
using Apperture.Shared.Kernel;
using Microsoft.AspNetCore.Http;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Resolves the current principal from the ASP.NET Core host (HttpContext.User) into an IActor.
/// Supports Human actors; fails safely when no authenticated principal is present.
/// </summary>
public sealed class HostPrincipalContext : IPrincipalContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HostPrincipalContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var actor = ClaimsPrincipalToActorMapper.ToActor(principal);
        return ValueTask.FromResult<IActor?>(actor);
    }
}
