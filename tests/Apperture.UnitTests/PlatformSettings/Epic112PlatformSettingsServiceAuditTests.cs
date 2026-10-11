using Apperture.ControlPlane.Security;
using Apperture.UnitTests.Audit;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class Epic112PlatformSettingsServiceAuditTests
{
    [Fact]
    public async Task SetAsync_records_readable_array_value_on_the_setting()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;
        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);
        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());
        resolver.Setup(x => x.GetResolvedAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resolved(def, ["alpha", "beta"]));
        var recorded = new AuditRecording();

        var coordinator = PlatformSettingOptionProviderTestHarness.CreateCoordinator(
            new Mock<IPlatformSettingOptionProvider>().Object);

        var sut = new PlatformSettingsService(
            new SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            coordinator,
            new PlatformSettingSecretMaterialProvisioner(Mock.Of<ISecretStore>()),
            recorded);

        await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ArrayValues = ["alpha", "beta"] },
            null,
            CancellationToken.None);

        var set = recorded.Single("platform.settings.set");
        set.Fields["newValue"].Should().Be("[alpha, beta]");
        set.Fields["operation"].Should().Be("set");
        set.Fields["sensitive"].Should().Be(false);
    }

    [Fact]
    public void ParsePayload_rejects_scalar_payload_for_string_array()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample;

        var act = () => PlatformSettingsUpdateParser.Parse(
            def,
            new PlatformSettingUpdatePayload { ScalarValue = "alpha" });

        act.Should().Throw<ArgumentException>().WithMessage("*values array*");
    }

    [Fact]
    public void ParsePayload_rejects_values_payload_for_scalar_setting()
    {
        var def = new PlatformSettingDefinition
        {
            Key = "Test.Boolean",
            DisplayName = "Test",
            Description = "Test",
            Category = PlatformSettingCategory.PlatformEvents,
            ValueType = PlatformSettingValueType.Boolean,
            InputKind = PlatformSettingInputKind.Toggle,
            ConfigurationPath = "Test:Boolean",
            DefaultValue = false
        };

        var act = () => PlatformSettingsUpdateParser.Parse(
            def,
            new PlatformSettingUpdatePayload { ArrayValues = ["alpha"] });

        act.Should().Throw<ArgumentException>().WithMessage("*only supported for StringArray*");
    }

    private static PlatformSettingResolvedValue Resolved(PlatformSettingDefinition def, string[] current) => new()
    {
        Key = def.Key,
        DisplayName = def.DisplayName,
        Description = def.Description,
        Category = def.Category,
        ValueType = def.ValueType,
        InputKind = def.InputKind,
        AllowedValues = def.AllowedValues ?? PlatformSettingResolvedValue.EmptyAllowedValues,
        CurrentValue = current,
        Source = PlatformSettingValueSource.Database
    };

    private sealed class SingleDefinitionRegistry(PlatformSettingDefinition definition) : IPlatformSettingDefinitionRegistry
    {
        public IReadOnlyList<PlatformSettingDefinition> GetAll() => [definition];
        public PlatformSettingDefinition? TryGet(string key) =>
            string.Equals(key, definition.Key, StringComparison.Ordinal) ? definition : null;
    }
}
