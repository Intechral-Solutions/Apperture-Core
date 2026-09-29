using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// SEC-1A2a structural guards: the return-target policy (<c>LocalReturnUrl</c>, Ui.Shared) is the ONLY validator. The client
/// and Management.Host both consume it; nothing may grow a second copy of its rules (the drift that produced the original
/// open redirect: two hand-written validators that both accepted <c>/\evil.example</c>). Cheap source scans.
/// </summary>
public sealed class ReturnTargetSourceGuardTests
{
    private static readonly string[] Roots =
    [
        "src/Apperture.ControlPlane/Apperture.Management.Ui.Client",
        "src/Apperture.ControlPlane/Apperture.Management.Ui.Shared",
        "src/Apperture.ControlPlane/Apperture.Management.Host",
        "src/Apperture.ControlPlane/Apperture.Management.Api",
    ];

    private const string PolicyFile = "src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Auth/LocalReturnUrl.cs";
    private const string HelperFile = "src/Apperture.ControlPlane/Apperture.Management.Ui.Shared/Auth/LoginRouteHelper.cs";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("repository root not found from test output directory");
        return dir!.FullName;
    }

    private static IEnumerable<(string Relative, string Text)> Sources() =>
        Roots.SelectMany(root => Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), root.Replace('/', Path.DirectorySeparatorChar)), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(RepoRoot(), f).Replace('\\', '/'), File.ReadAllText(f))));

    [Fact]
    public void The_two_original_validators_no_longer_exist()
    {
        Sources()
            .Where(s => s.Text.Contains("NormalizeReturnUrl", StringComparison.Ordinal)
                        || s.Text.Contains("NormalizeRelativePath", StringComparison.Ordinal))
            .Select(s => s.Relative).Should().BeEmpty();
    }

    [Fact]
    public void No_source_that_handles_a_return_url_carries_its_own_scheme_or_authority_check()
    {
        // The classic hand-rolled validator: reject "://" and a leading "//". Only the shared policy decides this.
        Sources()
            .Where(s => s.Relative != PolicyFile)
            .Where(s => s.Text.Contains("returnUrl", StringComparison.OrdinalIgnoreCase))
            .Where(s => s.Text.Contains("StartsWith(\"//\"", StringComparison.Ordinal)
                        || s.Text.Contains("Contains(\"://\"", StringComparison.Ordinal)
                        || s.Text.Contains("StartsWith('/')", StringComparison.Ordinal))
            .Select(s => s.Relative).Should().BeEmpty();
    }

    [Fact]
    public void The_reserved_auth_surface_rule_lives_only_in_the_shared_policy()
    {
        Sources()
            .Where(s => Regex.IsMatch(s.Text, @"\bIsReservedAuthPath\s*\(") && s.Text.Contains("bool IsReservedAuthPath", StringComparison.Ordinal))
            .Select(s => s.Relative).Should().BeEquivalentTo([PolicyFile]);
    }

    [Fact]
    public void The_raw_unvalidated_return_value_is_only_read_by_the_shared_helper()
    {
        // Everything else uses ReadReturnTarget (validated) or hands the raw value to LocalReturnUrl.
        Sources()
            .Where(s => s.Text.Contains("GetReturnUrlRaw(", StringComparison.Ordinal))
            .Select(s => s.Relative).Should().BeEquivalentTo([HelperFile]);
    }

    [Fact]
    public void Every_host_and_client_file_that_takes_a_return_url_validates_it_with_the_shared_policy()
    {
        var files = Sources()
            .Where(s => s.Relative.Contains("Management.Host/", StringComparison.Ordinal) || s.Relative.Contains("Management.Ui.Client/", StringComparison.Ordinal))
            .Where(s => Regex.IsMatch(s.Text, @"\[FromQuery\]\s*string\?\s*returnUrl") || s.Text.Contains("ReturnPath", StringComparison.Ordinal) && s.Text.Contains("NavigateTo(", StringComparison.Ordinal))
            .ToArray();

        files.Should().NotBeEmpty("the scan must actually find the consumers");
        files
            .Where(s => !s.Text.Contains("LocalReturnUrl", StringComparison.Ordinal)
                        && !s.Text.Contains("LoginRouteHelper.ReadReturnTarget", StringComparison.Ordinal)
                        && !s.Text.Contains("EntraSignInChallenge.ResolveReturnPath", StringComparison.Ordinal)
                        && !s.Text.Contains("EntraStepUpFlowConstants.CreateChallengeProperties", StringComparison.Ordinal)
                        && !s.Text.Contains("EntraLinkFlowConstants.CreateChallengeProperties", StringComparison.Ordinal))
            .Select(s => s.Relative).Should().BeEmpty();
    }

    [Fact]
    public void Every_local_sign_in_success_path_ends_in_a_full_document_load_through_the_policy()
    {
        var form = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client", "Molecules", "LocalLoginFormSection.razor"));

        form.Should().Contain("LocalReturnUrl.ResolveOrFallback(ReturnPath), forceLoad: true");
    }

    [Fact]
    public void The_login_pages_only_carry_a_validated_return_target_between_each_other_and_to_the_host()
    {
        var pages = Path.Combine(RepoRoot(), "src", "Apperture.ControlPlane", "Apperture.Management.Ui.Client", "Pages");
        foreach (var name in new[] { "Login.razor", "LoginLocal.razor" })
        {
            var text = File.ReadAllText(Path.Combine(pages, name));
            text.Should().Contain("LoginRouteHelper.ReadReturnTarget(Nav.Uri)", name);
            text.Should().NotContain("Uri.UnescapeDataString", name).And.NotContain("GetReturnUrlRaw", name);
        }
    }
}
