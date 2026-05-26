using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
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
    public async Task SetAsync_writes_readable_array_audit_details()
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
        var actor = new Mock<IActor>();
        actor.Setup(x => x.Id).Returns(new ActorId("actor-1"));
        var principalContext = new Mock<IPrincipalContext>();
        principalContext.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor.Object);
        string? auditDetails = null;
        var audit = new Mock<IAuditService>();
        audit.Setup(x => x.AppendAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuditRecord, CancellationToken>((record, _) => auditDetails = record.Details)
            .Returns(ValueTask.CompletedTask);

        var sut = new PlatformSettingsService(
            new SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            new Mock<IPlatformSettingOptionProviderRegistry>().Object,
            principalContext.Object,
            audit.Object);

        await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ArrayValues = ["alpha", "beta"] },
            null,
            CancellationToken.None);

        auditDetails.Should().Contain("newValue=[alpha, beta]");
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
