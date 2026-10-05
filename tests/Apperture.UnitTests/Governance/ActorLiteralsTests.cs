using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The non-human actor vocabulary: exact literals plus the <c>system:</c> family. Historical actor normalization and
/// audit actor classification rely on it to leave these values alone and never map them to a person.
/// </summary>
public sealed class ActorLiteralsTests
{
    [Fact]
    public void The_exact_vocabulary_is_the_set_the_platform_writes()
    {
        ActorLiterals.ExactLiterals.Should().BeEquivalentTo(["anonymous", "system", "recovery", "emergency-cli", "system-worker"]);
        ActorLiterals.SystemPrefix.Should().Be("system:");
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("system")]
    [InlineData("recovery")]
    [InlineData("emergency-cli")]
    [InlineData("system-worker")]
    public void Exact_literals_are_non_human(string value) =>
        ActorLiterals.IsNonHumanLiteral(value).Should().BeTrue();

    [Theory]
    [InlineData("system:scheduler")]
    [InlineData("system:module-lifecycle")]
    [InlineData("system:unknown")]
    [InlineData("system:a-future-subsystem")]
    public void The_system_prefix_family_is_non_human_including_future_subtypes(string value) =>
        ActorLiterals.IsNonHumanLiteral(value).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("system:")]
    [InlineData(" system")]
    [InlineData("system ")]
    [InlineData("System")]
    [InlineData("SYSTEM:scheduler")]
    [InlineData("systems")]
    [InlineData("system-")]
    [InlineData("worker")]
    [InlineData("breakglass")]
    public void Near_misses_are_not_literals(string? value) =>
        ActorLiterals.IsNonHumanLiteral(value).Should().BeFalse();

    [Theory]
    [InlineData("3f9a1c0e7b2d4a6f8e1c5b7d")] // a user id (24 lowercase hex)
    [InlineData("pat.lee")] // a username
    [InlineData("jane@contoso.com")] // a grandfathered username
    [InlineData("3d1b7e42-6a55-4f6e-9c0d-1e2f3a4b5c6d")] // an Entra object id in a pre-upgrade actor column
    public void User_ids_usernames_and_object_ids_are_not_literals(string value) =>
        ActorLiterals.IsNonHumanLiteral(value).Should().BeFalse();

    [Fact]
    public void The_emergency_cli_actor_is_the_shared_literal() =>
        EmergencyAccessService.CliActorId.Should().Be(ActorLiterals.EmergencyCli);

    [Fact]
    public void Every_hard_coded_actor_id_in_production_code_is_a_known_literal()
    {
        var literal = new Regex("new ActorId\\(\"(?<value>[^\"]*)\"\\)", RegexOptions.CultureInvariant);
        var sep = Path.DirectorySeparatorChar;
        var found = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .SelectMany(f => literal.Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Value: m.Groups["value"].Value)))
            .ToList();

        found.Should().NotBeEmpty("the scan must see the platform's own actor literals");
        found.Where(x => !ActorLiterals.IsNonHumanLiteral(x.Value))
            .Should().BeEmpty("a new non-human actor value must be added to ActorLiterals so historical actor data never mistakes it for a person");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("repository root not found from test output directory");
        return dir!.FullName;
    }
}
