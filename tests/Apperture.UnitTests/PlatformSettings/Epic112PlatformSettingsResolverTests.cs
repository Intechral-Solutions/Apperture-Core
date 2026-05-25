using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class Epic112PlatformSettingsResolverTests
{
    [Fact]
    public async Task Resolve_prefers_database_string_array_over_configuration_and_default()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;
        var resolver = CreateResolver(
            def,
            new Dictionary<string, string?> { ["Test:MultiSelect:Sample:0"] = "beta" },
            new PlatformSettingValue
            {
                Key = def.Key,
                ScopeKind = PlatformSettingScopeConstants.InstanceKind,
                ScopeId = PlatformSettingScopeConstants.InstanceId,
                ValueJson = "[\"alpha\",\"gamma\"]"
            });

        var value = await resolver.GetResolvedAsync(def.Key);

        value!.Source.Should().Be(PlatformSettingValueSource.Database);
        PlatformSettingsValueCodec.CoerceStringArray(value.CurrentValue).Should().Equal("alpha", "gamma");
        value.InputKind.Should().Be(PlatformSettingInputKind.MultiSelect);
        value.AllowedValues.Should().HaveCount(3);
    }

    [Fact]
    public async Task Resolve_uses_configuration_array_when_database_missing()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;
        var resolver = CreateResolver(
            def,
            new Dictionary<string, string?> { ["Test:MultiSelect:Sample:0"] = "beta", ["Test:MultiSelect:Sample:1"] = "gamma" });

        var value = await resolver.GetResolvedAsync(def.Key);

        value!.Source.Should().Be(PlatformSettingValueSource.ConfigurationDefault);
        PlatformSettingsValueCodec.CoerceStringArray(value.CurrentValue).Should().Equal("beta", "gamma");
    }

    [Fact]
    public async Task Resolve_uses_definition_default_array_when_no_override()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;
        var resolver = CreateResolver(def, new Dictionary<string, string?>());

        var value = await resolver.GetResolvedAsync(def.Key);

        value!.Source.Should().Be(PlatformSettingValueSource.DefinitionDefault);
        PlatformSettingsValueCodec.CoerceStringArray(value.CurrentValue).Should().Equal("alpha");
    }

    private static PlatformSettingsResolver CreateResolver(
        PlatformSettingDefinition definition,
        IReadOnlyDictionary<string, string?> configurationValues,
        PlatformSettingValue? dbValue = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configurationValues!).Build();
        return new PlatformSettingsResolver(
            new SingleDefinitionRegistry(definition),
            new FakeRepo(dbValue),
            config);
    }

    private sealed class SingleDefinitionRegistry(PlatformSettingDefinition definition) : IPlatformSettingDefinitionRegistry
    {
        public IReadOnlyList<PlatformSettingDefinition> GetAll() => [definition];
        public PlatformSettingDefinition? TryGet(string key) =>
            string.Equals(key, definition.Key, StringComparison.Ordinal) ? definition : null;
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
