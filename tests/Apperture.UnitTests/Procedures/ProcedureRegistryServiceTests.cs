using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Procedures;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace Apperture.UnitTests.Procedures;

public sealed class ProcedureRegistryServiceTests
{
    [Fact]
    public async Task ListAsync_WhenActivationFilterEnablesModule_RebuildsCatalogFromEmpty()
    {
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        var filter = new Mock<IModuleActivationFilter>();
        filter
            .Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string moduleId, CancellationToken _) => enabled.Contains(moduleId));

        var modules = new IModule[]
        {
            CreateModule(
                "Mod.A",
                new ModuleProcedureDeclaration(
                    "mod.a.p1",
                    "P1",
                    null,
                    null,
                    ProcedureLaunchKind.Workflow,
                    "wf.a",
                    null)),
            CreateModule(
                "Mod.B",
                new ModuleProcedureDeclaration(
                    "mod.b.p1",
                    "P2",
                    null,
                    null,
                    ProcedureLaunchKind.Operation,
                    "op.b",
                    null))
        };

        var sut = new ProcedureRegistryService(modules, filter.Object);

        (await sut.ListAsync()).Should().BeEmpty();

        enabled.Add("Mod.A");
        var afterA = await sut.ListAsync();
        afterA.Should().ContainSingle();
        afterA[0].Id.Should().Be("mod.a.p1");

        enabled.Add("Mod.B");
        var afterB = await sut.ListAsync();
        afterB.Should().HaveCount(2);
        afterB.Select(x => x.Id).Should().BeEquivalentTo(["mod.a.p1", "mod.b.p1"]);
    }

    [Fact]
    public async Task GetByModuleAsync_WithoutFilter_IncludesAllManifestProcedures()
    {
        var modules = new IModule[]
        {
            CreateModule(
                "Mod.A",
                new ModuleProcedureDeclaration(
                    "mod.a.p1",
                    "P1",
                    null,
                    null,
                    ProcedureLaunchKind.Workflow,
                    "wf.a",
                    null))
        };

        var sut = new ProcedureRegistryService(modules, activationFilter: null);
        var list = await sut.GetByModuleAsync("Mod.A");
        list.Should().ContainSingle();
        list[0].Id.Should().Be("mod.a.p1");
    }

    private static IModule CreateModule(string moduleId, params ModuleProcedureDeclaration[] procedures)
    {
        var manifest = new ModuleManifest(moduleId, "N", "1", "D")
        {
            Procedures = procedures
        };

        var m = new Mock<IModule>();
        m.Setup(x => x.GetManifest()).Returns(manifest);
        m.Setup(x => x.GetDefinitionsAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyDefinitions());
        return m.Object;
    }

    private static async IAsyncEnumerable<WorkflowDefinition> EmptyDefinitions()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
