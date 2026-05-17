using Apperture.Core.Application.PlatformEvents;
using Microsoft.Extensions.Hosting;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Forces construction of <see cref="IPlatformEventCatalog"/> at host startup so manifest signal validation
/// fails fast (Epic 77 Phase 2B) instead of on first emit/query.
/// </summary>
internal sealed class PlatformEventCatalogWarmupHostedService : IHostedService
{
    private readonly IPlatformEventCatalog _catalog;

    public PlatformEventCatalogWarmupHostedService(IPlatformEventCatalog catalog) =>
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _catalog.GetAll();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
