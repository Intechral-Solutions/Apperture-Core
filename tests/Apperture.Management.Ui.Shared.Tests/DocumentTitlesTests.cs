using System.Text.RegularExpressions;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// Browser and history titles. The formatter is a pure function of what the shell top bar already
/// shows, so the rules are pinned here. The contract tests walk every routable page.
/// </summary>
public sealed class DocumentTitlesTests
{
    private const string Pages = "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages";

    private readonly ITestOutputHelper _output;

    public DocumentTitlesTests(ITestOutputHelper output) => _output = output;

    // ---- Formatter ---------------------------------------------------------------------------

    [Theory]
    // List pages: the stable noun, nothing else.
    [InlineData("Connections", "Connections", null, "Connections")]
    [InlineData("Runs", "Runs", null, "Runs")]
    [InlineData("Platform settings", "Platform settings", null, "Platform settings")]
    // Create pages: the page already says what it creates.
    [InlineData("Add connection", "New", "Connections", "Add connection")]
    [InlineData("New user", "New", "Users", "New user")]
    [InlineData("Create notification definition", "New definition", "Definitions", "Create notification definition")]
    [InlineData("Add dashboard page", "Add page", "Control Center", "Add dashboard page")]
    // Detail pages: type, then the record name.
    [InlineData("Student Information System", "Connection", "Connections", "Connection — Student Information System")]
    [InlineData("Import Students", "Run", "Runs", "Run — Import Students")]
    [InlineData("alice", "User", "Users", "User — alice")]
    [InlineData("Failed integration run", "Instance", "Instances", "Instance — Failed integration run")]
    // Detail pages while the record is still loading: the generic page title.
    [InlineData("Connection", "Connection", "Connections", "Connection")]
    [InlineData("User", "User", "Users", "User")]
    // Edit pages read as a phrase.
    [InlineData("Failed integration run", "Edit", "Definition", "Edit Failed integration run")]
    // Integration workspace panels: the panel, then the integration.
    [InlineData("Student Information System", "Runs", "Integration", "Runs — Student Information System")]
    [InlineData("Student Information System", "Connections", "Integration", "Connections — Student Information System")]
    [InlineData("Student Information System", "Schedule diagnostics", "Integration", "Schedule diagnostics — Student Information System")]
    [InlineData("Student Information System", "Integration", "Integrations", "Integration — Student Information System")]
    // Static pages whose heading is longer than the route label.
    [InlineData("Notification diagnostics", "Diagnostics", "Notifications", "Notification diagnostics")]
    [InlineData("Delivery health", "Delivery", "Notifications", "Delivery health")]
    [InlineData("Permission catalog", "Permission catalog", "Governance", "Permission catalog")]
    [InlineData("Entra tenant setup", "Entra tenants", "Auth", "Entra tenant setup")]
    [InlineData("Sign-in chooser", "Sign-in experience", "Auth", "Sign-in chooser")]
    [InlineData("Edit dashboard layout", "Edit layout", "Control Center", "Edit dashboard layout")]
    // A heading that means nothing on its own gets its area, and is never used to qualify another heading.
    [InlineData("Overview", "Overview", "Auth", "Overview — Auth")]
    [InlineData("Configuration", "Overview", "Config", "Configuration")]
    public void Compose_formats_representative_screens(string page, string route, string? parent, string expected) =>
        DocumentTitles.Compose(page, route, parent).Should().Be(expected);

    [Fact]
    public void ComposeForRoute_never_uses_a_raw_url_key_as_the_qualifier()
    {
        // The customer detail route labels the page with the business key from the URL.
        const string path = "/business-data/customers/BK-1042";

        var loaded = DocumentTitles.ComposeForRoute("Alice Smith", "BK-1042", path, "Customers");

        loaded.Should().Be("Customers — Alice Smith");
        loaded.Should().NotContain("BK-1042");
    }

    /// <summary>
    /// The shell's real path through the resolver. A route label that merely equals the last URL segment
    /// ("Runs" at /runs) is a label, not a record key, so list pages must stay plain.
    /// </summary>
    [Theory]
    [InlineData("/runs", "Runs", "Runs")]
    [InlineData("/connections", "Connections", "Connections")]
    [InlineData("/config/governance/users", "Users", "Users")]
    [InlineData("/account/profile", "Profile", "Profile")]
    [InlineData("/config/notifications/instances", "Notification instances", "Notification instances")]
    [InlineData("/connections/conn-1", "Payroll DB", "Connection — Payroll DB")]
    [InlineData("/runs/run-1", "import-students", "Run — import-students")]
    [InlineData("/integrations/int-1", "Student Information System", "Integration — Student Information System")]
    [InlineData("/integrations/int-1/runs", "Student Information System", "Runs — Student Information System")]
    [InlineData("/integrations/int-1/connections", "Student Information System", "Connections — Student Information System")]
    [InlineData("/config/governance/users/u1", "alice", "User — alice")]
    [InlineData("/business-data/customers/BK-1042", "Alice Smith", "Customers — Alice Smith")]
    public void The_shell_composes_the_expected_title_for_real_routes(string path, string pageTitle, string expected)
    {
        var orientation = ManagementBreadcrumbResolver.ResolveOrientation(path);

        DocumentTitles.ComposeForRoute(
                pageTitle,
                orientation.Title,
                EntityShellTitles.NormalizePath(path),
                orientation.Parents.LastOrDefault()?.Label)
            .Should().Be(expected);
    }

    [Fact]
    public void ComposeForRoute_keeps_an_ordinary_route_label()
    {
        DocumentTitles.ComposeForRoute("Payroll DB", "Connection", "/connections/conn-1", "Connections")
            .Should().Be("Connection — Payroll DB");
    }

    [Fact]
    public void Compose_updates_when_the_record_name_arrives()
    {
        DocumentTitles.Compose("Connection", "Connection", "Connections").Should().Be("Connection");
        DocumentTitles.Compose("Payroll DB", "Connection", "Connections").Should().Be("Connection — Payroll DB");
    }

    [Fact]
    public void Compose_never_adds_the_product_name()
    {
        foreach (var (page, route) in new[] { ("Runs", "Runs"), ("Payroll DB", "Connection"), ("Overview", "Overview") })
            DocumentTitles.Compose(page, route, "Auth").Should().NotContainEquivalentOf("Apperture");
    }

    [Fact]
    public void Compose_shortens_a_long_record_name_and_keeps_the_type()
    {
        var name = new string('x', 120);

        var title = DocumentTitles.Compose(name, "Connection", "Connections");

        title.Should().StartWith("Connection — ");
        title.Should().EndWith("…");
        title!.Length.Should().BeLessThanOrEqualTo(DocumentTitles.MaxLength);
    }

    [Fact]
    public void Compose_shortens_a_long_static_title()
    {
        var title = DocumentTitles.Compose(new string('y', 90), "Page", null);

        title!.Length.Should().BeLessThanOrEqualTo(DocumentTitles.MaxLength);
        title.Should().EndWith("…");
    }

    [Theory]
    [InlineData("😀")]            // astral: a surrogate pair (2 UTF-16 units)
    [InlineData("a😀")]           // pair straddles a different boundary
    [InlineData("𝒜")]             // astral letter
    public void Compose_never_cuts_through_a_surrogate_pair(string unit)
    {
        // Repeat so that, at every plausible cut point, one of the units straddles it.
        foreach (var name in Enumerable.Range(40, 24).Select(n => string.Concat(Enumerable.Repeat(unit, n))))
        {
            foreach (var title in new[]
                     {
                         DocumentTitles.Compose(name, "Connection", "Connections"),
                         DocumentTitles.Compose(name, "Connection"),
                         DocumentTitles.Compose(name, null),
                     })
            {
                title.Should().NotBeNull();
                title!.Length.Should().BeLessThanOrEqualTo(DocumentTitles.MaxLength);
                AssertWellFormed(title);
            }
        }
    }

    [Theory]
    [InlineData("Payroll \"Q3\" — <north> & south's")]
    [InlineData("name\twith\ttabs\nand a newline")]
    [InlineData("עברית ‏RTL‏ name")]
    [InlineData("  ...  ")]
    [InlineData("——— — ———")]
    public void Compose_keeps_unusual_punctuation_and_scripts_intact(string name)
    {
        var title = DocumentTitles.Compose(name, "Connection", "Connections");

        title.Should().NotBeNull();
        title.Should().StartWith("Connection — ");
        title.Should().NotContainAny("\t", "\n", "\r");
        AssertWellFormed(title!);
    }

    [Fact]
    public void Compose_with_a_very_long_lead_still_returns_a_well_formed_short_title()
    {
        var title = DocumentTitles.Compose("Name", new string('L', 80), null);

        title!.Length.Should().BeLessThanOrEqualTo(DocumentTitles.MaxLength);
        title.Should().EndWith("…");
        AssertWellFormed(title);
    }

    /// <summary>Valid UTF-16: no lone surrogate halves.</summary>
    private static void AssertWellFormed(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])).Should().BeTrue($"high surrogate at {i} must be followed by a low surrogate in '{s}'");
                i++;
            }
            else
            {
                char.IsLowSurrogate(s[i]).Should().BeFalse($"unpaired low surrogate at {i} in '{s}'");
            }
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    public void Compose_returns_null_when_nothing_identifies_the_screen(string? page, string? route) =>
        DocumentTitles.Compose(page, route, null).Should().BeNull();

    [Fact]
    public void Compose_falls_back_to_the_route_label_when_there_is_no_page_title() =>
        DocumentTitles.Compose(null, "Runs", null).Should().Be("Runs");

    [Fact]
    public void Compose_collapses_whitespace() =>
        DocumentTitles.Compose("  Payroll   DB  ", "Connection", null).Should().Be("Connection — Payroll DB");

    // ---- Route-surface contract --------------------------------------------------------------

    /// <summary>
    /// Pages that never render their own screen: they redirect, or are a developer demo with its own title.
    /// Anything else must resolve a shell title or declare its own.
    /// </summary>
    private static readonly HashSet<string> NoTitleNeeded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account/AccountRedirect.razor",
        "Config/Security/SignInExperienceRedirect.razor",
        "Demo/DemoCustomersLegacyRedirect.razor",
        "Governance/GovernanceRedirects.razor",
        "Demo/NestedNavDemo.razor",
    };

    [Fact]
    public void Every_routable_page_has_a_shell_title_or_declares_its_own()
    {
        var failures = new List<string>();
        var table = new List<string>();

        foreach (var (file, route) in RoutablePages())
        {
            var relative = file[(RepoPath(Pages).Length + 1)..].Replace('\\', '/');
            if (NoTitleNeeded.Contains(relative))
                continue;

            var source = File.ReadAllText(file);
            if (source.Contains("@layout AuthLayout", StringComparison.Ordinal))
            {
                // No top bar on these screens, so the page must set its own title.
                if (!source.Contains("<PageTitle>", StringComparison.Ordinal))
                    failures.Add($"{route} ({relative}): auth-layout page without <PageTitle>");
                table.Add($"{route,-72} <PageTitle> in page");
                continue;
            }

            var path = Concrete(route);
            var orientation = ManagementBreadcrumbResolver.ResolveOrientation(path);

            // A route with no breadcrumb rule takes its title from the page's static heading, which the
            // heading publishes to the shell. The route label stands in for the heading elsewhere.
            var heading = orientation.Title ?? StaticHeading(source);
            var title = DocumentTitles.ComposeForRoute(
                heading,
                orientation.Title,
                EntityShellTitles.NormalizePath(path),
                orientation.Parents.LastOrDefault()?.Label);
            table.Add($"{route,-72} {title ?? "(none)"}");
            if (string.IsNullOrWhiteSpace(title))
                failures.Add($"{route} ({relative}): no shell title resolves for this route");
        }

        foreach (var line in table)
            _output.WriteLine(line);

        failures.Should().BeEmpty("every routable page needs an identifiable browser title:\n" + string.Join('\n', failures));
    }

    [Fact]
    public void The_shell_renders_one_document_title_from_the_formatter()
    {
        var topBar = File.ReadAllText(RepoPath(
            "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Molecules/TopBarOrientation.razor"));

        topBar.Should().Contain("<PageTitle>@_documentTitle</PageTitle>");
        topBar.Should().Contain("DocumentTitles.ComposeForRoute(");
        // The title must follow the page-published record name, not only the route.
        topBar.Should().Contain("ShellOrientation.PageTitle");
    }

    [Fact]
    public void Pages_inside_the_shell_do_not_set_a_competing_title()
    {
        var offenders = RoutablePages()
            .Where(p => !File.ReadAllText(p.File).Contains("@layout AuthLayout", StringComparison.Ordinal))
            .Where(p => File.ReadAllText(p.File).Contains("<PageTitle>", StringComparison.Ordinal))
            .Select(p => p.File[(RepoPath(Pages).Length + 1)..].Replace('\\', '/'))
            .Where(f => !NoTitleNeeded.Contains(f))
            .ToList();

        offenders.Should().BeEmpty("the shell title is the single source of the browser title");
    }

    [Fact]
    public void The_root_and_not_found_screens_do_not_use_the_product_name_as_a_title()
    {
        var routes = File.ReadAllText(RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Routes.razor"));
        var app = File.ReadAllText(RepoPath("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/App.razor"));

        routes.Should().NotContain("<PageTitle>");
        app.Should().Contain("<PageTitle>Page not found</PageTitle>");
    }

    private static IEnumerable<(string File, string Route)> RoutablePages()
    {
        var pageRegex = new Regex("^@page\\s+\"(?<route>[^\"]+)\"", RegexOptions.Multiline);
        foreach (var file in Directory.EnumerateFiles(RepoPath(Pages), "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (Match m in pageRegex.Matches(source))
                yield return (file, m.Groups["route"].Value);
        }
    }

    /// <summary>The first page heading written as a plain string literal, or null when it is computed.</summary>
    private static string? StaticHeading(string source)
    {
        var m = Regex.Match(source, "<PageHeader\\b[^>]*?\\bTitle=\"(?<t>[^\"@][^\"]*)\"", RegexOptions.Singleline);
        return m.Success ? m.Groups["t"].Value : null;
    }

    /// <summary>Turns a route template into a concrete path (<c>/connections/{ConnectionId}</c> to <c>/connections/x</c>).</summary>
    private static string Concrete(string route) => Regex.Replace(route, "\\{[^}]*\\}", "x");

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
