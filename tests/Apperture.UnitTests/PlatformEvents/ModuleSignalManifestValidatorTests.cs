using Apperture.Core.Application.PlatformEvents;
using Apperture.ModuleSdk.Abstractions;
using Apperture.ModuleSdk.Abstractions.Signals;
using Apperture.RuntimePlane.Abstractions;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.PlatformEvents;

public sealed class ModuleSignalManifestValidatorTests
{
    private sealed class Vm : IModule
    {
        public Vm(ModuleManifest m) => Manifest = m;
        public ModuleManifest Manifest { get; }
        public ModuleManifest GetManifest() => Manifest;
        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }

    private static ModuleManifest M(string id, params ModuleSignalDeclaration[] s) =>
        new(Id: id, Name: "n", Version: "1", Description: "d") { Signals = s };

    private static ModuleSignalDeclaration D(
        string name,
        string? display = "Display",
        string? description = "Description",
        string severity = "info",
        string[]? req = null) =>
        new(name, display!, description!, severity, RequiredDataProperties: req);

    private static void Act(params IModule[] modules) =>
        ModuleSignalManifestValidator.ThrowIfInvalid(modules);

    [Fact]
    public void Accepts_well_formed_declaration()
    {
        var act = () => Act(new Vm(M("My.Mod", D("demo.ok", req: ["message"]))));
        act.Should().NotThrow();
    }

    [Fact]
    public void Rejects_duplicate_name_within_module()
    {
        var act = () => Act(new Vm(M("My.Mod", D("demo.x"), D("demo.x"))));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_duplicate_full_type_across_manifest_casing_aliases()
    {
        var m1 = new Vm(M("Sample.Mod", D("signal.a")));
        var m2 = new Vm(M("sample.mod", D("signal.a")));
        var act = () => Act(m1, m2);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_bad_signal_name_segments()
    {
        var act = () => Act(new Vm(M("My.Mod", D("NOPE", severity: "info"))));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_bad_severity()
    {
        var act = () => Act(new Vm(M("My.Mod", D("demo.x", severity: "nope"))));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_empty_display_description_or_invalid_required_props()
    {
        FluentActions.Invoking(() => Act(new Vm(M("My.Mod", D("demo.z", display: "", description: "Desc")))))
            .Should().Throw<InvalidOperationException>();

        FluentActions.Invoking(() => Act(new Vm(M("My.Mod", D("demo.z", display: "D", description: "")))))
            .Should().Throw<InvalidOperationException>();

        FluentActions.Invoking(() => Act(new Vm(M("My.Mod", D("demo.z", severity: "info", req: ["InvalidCase"])))))
            .Should().Throw<InvalidOperationException>();

        FluentActions.Invoking(() => Act(new Vm(M("My.Mod", D("demo.a", severity: "info", req: ["a", "a"])))))
            .Should().Throw<InvalidOperationException>();
    }
}
