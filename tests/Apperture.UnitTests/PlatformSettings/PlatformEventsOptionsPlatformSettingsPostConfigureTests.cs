using Apperture.Core.Application.PlatformEvents;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.RuntimePlane.Engine.PlatformEvents;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class PlatformEventsOptionsPlatformSettingsPostConfigureTests
{
    [Fact]
    public void PostConfigure_applies_platform_events_enabled_override()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPlatformSettingsResolver>(_ => new StubResolver(false));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var sut = new PlatformEventsOptionsPlatformSettingsPostConfigure(scopeFactory, NullLogger<PlatformEventsOptionsPlatformSettingsPostConfigure>.Instance);
        var options = new PlatformEventsOptions { Enabled = true };

        sut.PostConfigure(null, options);

        options.Enabled.Should().BeFalse();
    }

    private sealed class StubResolver : IPlatformSettingsResolver
    {
        private readonly bool _enabled;
        public StubResolver(bool enabled) => _enabled = enabled;
        public Task<IReadOnlyList<PlatformSettingResolvedValue>> ListResolvedAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlatformSettingResolvedValue>>([]);
        public Task<PlatformSettingResolvedValue?> GetResolvedAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<PlatformSettingResolvedValue?>(null);
        public Task<object?> ResolveRawValueAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<object?>(_enabled);
    }
}
