using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class GatedPlatformSettingDefinitionRegistryTests
{
    [Fact]
    public void GetAll_includes_demo_when_development()
    {
        var sut = CreateSut(isDevelopment: true, demoFlag: false);
        var keys = sut.GetAll().Select(d => d.Key).ToList();

        keys.Should().Contain(DemoPlatformSettingKeys.Boolean);
        keys.Should().HaveCount(new PlatformSettingDefinitionRegistry().GetAll().Count + PlatformSettingDemoDefinitionRegistry.GetAll().Count);
    }

    [Fact]
    public void GetAll_includes_demo_when_flag_enabled_in_production()
    {
        var sut = CreateSut(isDevelopment: false, demoFlag: true);
        sut.GetAll().Select(d => d.Key).Should().Contain(DemoPlatformSettingKeys.Text);
    }

    [Fact]
    public void GetAll_excludes_demo_when_not_development_and_flag_disabled()
    {
        var sut = CreateSut(isDevelopment: false, demoFlag: false);
        var keys = sut.GetAll().Select(d => d.Key).ToList();

        keys.Should().NotContain(k => k.StartsWith("demo.settings.", StringComparison.Ordinal));
        keys.Should().HaveCount(17);
    }

    [Fact]
    public void TryGet_returns_demo_definition_when_gallery_enabled()
    {
        var sut = CreateSut(isDevelopment: true, demoFlag: false);
        var def = sut.TryGet(DemoPlatformSettingKeys.StaticMultiSelect);

        def.Should().NotBeNull();
        def!.InputKind.Should().Be(PlatformSettingInputKind.MultiSelect);
        def.Category.Should().Be(PlatformSettingCategory.Demo);
    }

    [Fact]
    public void TryGet_returns_null_for_demo_key_when_gallery_disabled()
    {
        var sut = CreateSut(isDevelopment: false, demoFlag: false);
        sut.TryGet(DemoPlatformSettingKeys.Boolean).Should().BeNull();
    }

    private static GatedPlatformSettingDefinitionRegistry CreateSut(bool isDevelopment, bool demoFlag)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformSettings:DemoGallery:Enabled"] = demoFlag ? "true" : "false"
            })
            .Build();

        return new GatedPlatformSettingDefinitionRegistry(new StubHostEnvironment(isDevelopment), config);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(bool isDevelopment) =>
            EnvironmentName = isDevelopment ? Environments.Development : Environments.Production;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
