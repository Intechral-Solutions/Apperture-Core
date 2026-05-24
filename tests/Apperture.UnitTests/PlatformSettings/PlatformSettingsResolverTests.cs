using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class PlatformSettingsResolverTests
{
    [Fact]
    public async Task Resolve_prefers_database_over_configuration_and_default()
    {
        var resolver = CreateResolver(
            new Dictionary<string, string?> { ["PlatformEvents:Enabled"] = "false" },
            new PlatformSettingValue
            {
                Key = PlatformSettingKeys.PlatformEventsEnabled,
                ScopeKind = PlatformSettingScopeConstants.InstanceKind,
                ScopeId = PlatformSettingScopeConstants.InstanceId,
                ValueJson = "true"
            });

        var value = await resolver.GetResolvedAsync(PlatformSettingKeys.PlatformEventsEnabled);

        value!.Source.Should().Be(PlatformSettingValueSource.Database);
        value.CurrentValue.Should().Be(true);
    }

    [Fact]
    public async Task Resolve_uses_configuration_when_database_missing()
    {
        var resolver = CreateResolver(
            new Dictionary<string, string?> { ["PlatformEvents:Enabled"] = "false" });

        var value = await resolver.GetResolvedAsync(PlatformSettingKeys.PlatformEventsEnabled);

        value!.Source.Should().Be(PlatformSettingValueSource.ConfigurationDefault);
        value.CurrentValue.Should().Be(false);
    }

    private static PlatformSettingsResolver CreateResolver(
        IReadOnlyDictionary<string, string?> configurationValues,
        PlatformSettingValue? dbValue = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configurationValues!).Build();
        return new PlatformSettingsResolver(
            new PlatformSettingDefinitionRegistry(),
            new FakeRepo(dbValue),
            config);
    }

    private sealed class FakeRepo : IPlatformSettingValueRepository
    {
        private readonly PlatformSettingValue? _value;
        public FakeRepo(PlatformSettingValue? value) => _value = value;
        public Task<PlatformSettingValue?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_value is not null && _value.Key == key ? _value : null);
        public Task<IReadOnlyList<PlatformSettingValue>> ListInstanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlatformSettingValue>>(_value is null ? [] : [_value]);
        public Task UpsertAsync(PlatformSettingValue value, byte[]? expectedRowVersion, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
