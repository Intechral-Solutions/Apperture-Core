using Apperture.Core.Application.Execution;
using Apperture.Core.Application.PlatformSettings;
using Apperture.ModuleSdk.Integration.Configuration;
using Apperture.RuntimePlane.Engine.Integration;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

/// <summary>
/// SEC-2.3 (A3.4, A3.5): the runtime accessor reads a sensitive setting's secret only through the setting-secret boundary, which
/// proves the reference is the setting's own. A module whose setting names another holder's secret sees the approved
/// "not configured" behaviour, never the value, and the failure names no reference.
/// </summary>
public sealed class IntegrationSettingsAccessorSecretTests
{
    private const string IntegrationId = "int-a";
    private const string Key = "Sample.Api.Token";
    private static readonly string Reference = Guid.NewGuid().ToString("D");

    private static PlatformSettingDefinition SensitiveDefinition(string key = Key) => new()
    {
        Key = key,
        DisplayName = "Token (test)",
        Description = "Sensitive integration setting.",
        Category = PlatformSettingCategory.PlatformEvents,
        ValueType = PlatformSettingValueType.String,
        InputKind = PlatformSettingInputKind.Secret,
        IsSensitive = true,
        ConfigurationPath = "Test:Token",
        DefaultValue = string.Empty,
    };

    private static PlatformSettingDefinition OrdinaryDefinition(string key) => SensitiveDefinition(key) with
    {
        InputKind = PlatformSettingInputKind.Text,
        IsSensitive = false,
    };

    private sealed class Rig
    {
        public Mock<ISettingSecretResolver> Secrets { get; } = new(MockBehavior.Strict);
        public IntegrationSettingsAccessor Sut { get; }

        public Rig(PlatformSettingResolvedValue? resolved)
        {
            var scope = new Mock<IIntegrationExecutionScopeAccessor>();
            scope.SetupGet(s => s.IntegrationId).Returns(IntegrationId);
            var settings = new Mock<IIntegrationSettingsResolver>();
            settings.Setup(s => s.GetResolvedAsync(IntegrationId, Key, It.IsAny<CancellationToken>())).ReturnsAsync(resolved);
            Sut = new IntegrationSettingsAccessor(scope.Object, settings.Object, Secrets.Object);
        }

        public static Rig WithReference(string? reference) =>
            new(PlatformSettingResolvedValueFactory.Create(
                SensitiveDefinition(), reference ?? "", PlatformSettingValueSource.Database, valueScopeKind: PlatformSettingScopeConstants.IntegrationKind, valueScopeId: IntegrationId));
    }

    [Fact]
    public async Task An_owned_secret_is_returned_and_the_boundary_is_asked_for_this_integrations_setting_on_the_run_path()
    {
        var rig = Rig.WithReference(Reference);
        rig.Secrets.Setup(s => s.ResolveIntegrationSettingAsync(IntegrationId, Key, Reference, SettingSecretResolutionPath.Run, It.IsAny<CancellationToken>()))
            .ReturnsAsync("the-token");

        (await rig.Sut.GetSecretAsync(Key)).Should().Be("the-token");
        rig.Secrets.VerifyAll();
    }

    [Fact]
    public async Task A_refused_reference_reads_as_not_configured_and_never_returns_a_value()
    {
        var rig = Rig.WithReference(Reference);
        rig.Secrets.Setup(s => s.ResolveIntegrationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SettingSecretResolutionPath>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SettingSecretUnavailableException(Key));

        var act = () => rig.Sut.GetSecretAsync(Key).AsTask();

        var thrown = (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which;
        thrown.Code.Should().Be("missing_secret");
        thrown.SettingKey.Should().Be(Key);
        thrown.ToString().Should().NotContain(Reference);
    }

    [Fact]
    public async Task A_failure_that_is_not_a_refusal_is_the_existing_invalid_reference_failure_and_leaks_nothing()
    {
        var rig = Rig.WithReference(Reference);
        rig.Secrets.Setup(s => s.ResolveIntegrationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SettingSecretResolutionPath>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException($"unprotect failed for {Reference}"));

        var act = () => rig.Sut.GetSecretAsync(Key).AsTask();

        var thrown = (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which;
        thrown.Code.Should().Be("invalid_secret_reference");
        thrown.ToString().Should().NotContain(Reference);
    }

    [Fact]
    public async Task A_secret_that_resolves_to_blank_text_is_an_invalid_reference_not_an_empty_secret()
    {
        var rig = Rig.WithReference(Reference);
        rig.Secrets.Setup(s => s.ResolveIntegrationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SettingSecretResolutionPath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("   ");

        var act = () => rig.Sut.GetSecretAsync(Key).AsTask();

        (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which.Code.Should().Be("invalid_secret_reference");
    }

    [Fact]
    public async Task A_setting_with_nothing_stored_is_missing_and_the_boundary_is_not_asked()
    {
        var rig = Rig.WithReference(null);

        var act = () => rig.Sut.GetSecretAsync(Key).AsTask();

        (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which.Code.Should().Be("missing_secret");
        rig.Secrets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Text_that_is_not_a_reference_is_handed_to_the_boundary_and_refused_there_like_any_other_reference()
    {
        // The reclassification hazard: an ordinary value that a module later declares sensitive. The accessor does not decide what
        // a usable reference looks like; the boundary refuses it, with the evidence row, and the module sees "not configured".
        var rig = Rig.WithReference("plain text a module once stored");
        rig.Secrets.Setup(s => s.ResolveIntegrationSettingAsync(IntegrationId, Key, "plain text a module once stored", SettingSecretResolutionPath.Run, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SettingSecretUnavailableException(Key));

        var act = () => rig.Sut.GetSecretAsync(Key).AsTask();

        (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which.Code.Should().Be("missing_secret");
        rig.Secrets.VerifyAll();
    }

    [Fact]
    public async Task An_ordinary_setting_is_not_a_secret_and_the_boundary_is_not_asked()
    {
        var rig = new Rig(PlatformSettingResolvedValueFactory.Create(
            OrdinaryDefinition(Key), Reference, PlatformSettingValueSource.Database, valueScopeKind: PlatformSettingScopeConstants.IntegrationKind, valueScopeId: IntegrationId));

        var act = () => rig.Sut.GetSecretAsync(Key).AsTask();

        (await act.Should().ThrowAsync<IntegrationConfigurationException>()).Which.Code.Should().Be("invalid_setting");
        rig.Secrets.VerifyNoOtherCalls();
    }
}
