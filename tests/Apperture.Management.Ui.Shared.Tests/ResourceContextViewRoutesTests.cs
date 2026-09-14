using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public class ResourceContextViewRoutesTests
{
    [Theory]
    [InlineData("/integrations")]
    [InlineData("/connections")]
    [InlineData("/schedules")]
    [InlineData("/runs")]
    public void ScopedListRoutes_ResolveToScoped(string path)
    {
        ResourceContextViewRoutes.Resolve(path).Should().Be(ResourceContextViewMode.Scoped);
    }

    [Theory]
    [InlineData("/control/center")]
    public void ControlCenter_ResolvesToPartial(string path)
    {
        ResourceContextViewRoutes.Resolve(path).Should().Be(ResourceContextViewMode.Partial);
    }

    [Theory]
    [InlineData("/integrations/abc123")]
    [InlineData("/integrations/abc123/connections")]
    [InlineData("/integrations/abc123/schedules")]
    [InlineData("/integrations/abc123/runs")]
    [InlineData("/integrations/abc123/settings")]
    [InlineData("/integrations/abc123/notifications")]
    [InlineData("/integrations/abc123/schedule-diagnostics")]
    [InlineData("/connections/conn1")]
    [InlineData("/schedules/sched1")]
    [InlineData("/runs/run1")]
    public void DetailAndHubRoutes_ResolveToDetail(string path)
    {
        ResourceContextViewRoutes.Resolve(path).Should().Be(ResourceContextViewMode.Detail);
    }

    [Theory]
    [InlineData("/business-data")]
    [InlineData("/business-data/customers")]
    [InlineData("/config/inventory")]
    [InlineData("/config/applications")]
    [InlineData("/config/applications/new")]
    [InlineData("/config/applications/abc123")]
    [InlineData("/config/governance")]
    [InlineData("/control/audit")]
    [InlineData("/execute")]
    [InlineData("/me/notifications")]
    [InlineData("/account")]
    [InlineData("/schedules/new")]
    [InlineData("/integrations/new")]
    [InlineData("/connections/new")]
    [InlineData("/")]
    public void EverythingElse_ResolvesToUnscoped(string path)
    {
        ResourceContextViewRoutes.Resolve(path).Should().Be(ResourceContextViewMode.Unscoped);
    }

    [Fact]
    public void TrailingSlashAndQueryString_AreIgnored()
    {
        ResourceContextViewRoutes.Resolve("/integrations/").Should().Be(ResourceContextViewMode.Scoped);
        ResourceContextViewRoutes.Resolve("/integrations?context=all").Should().Be(ResourceContextViewMode.Scoped);
    }
}
