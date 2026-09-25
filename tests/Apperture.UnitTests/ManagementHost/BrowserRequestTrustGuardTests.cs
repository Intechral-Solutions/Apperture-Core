using System.Reflection;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class BrowserRequestTrustGuardTests
{
    [Fact]
    public void The_platform_request_header_literal_lives_only_in_the_shared_constant()
    {
        var hits = RepoFiles("src")
            .Where(file => File.ReadAllText(file).Contains("X-Apperture-Request", StringComparison.Ordinal))
            .Select(file => Relative(file))
            .OrderBy(path => path)
            .ToArray();

        hits.Should().Equal("src/Apperture.Shared/Apperture.Shared.Kernel/BrowserRequest.cs");
    }

    [Fact]
    public void The_client_adds_both_headers_only_in_the_one_handler()
    {
        var client = File.ReadAllText(Path.Combine(RepoRoot(), "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/BrowserRequestHeadersHandler.cs"));
        client.Should().Contain("BrowserRequest.HeaderName").And.Contain("BackgroundActivity.HeaderName");

        var writers = RepoFiles("src/Apperture.ControlPlane/Apperture.Management.Ui.Client")
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("BrowserRequest.HeaderName", StringComparison.Ordinal)
                    || text.Contains("BackgroundActivity.HeaderName", StringComparison.Ordinal);
            })
            .Select(Relative)
            .OrderBy(path => path)
            .ToArray();

        writers.Should().Equal("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Services/BrowserRequestHeadersHandler.cs");
    }

    [Fact]
    public void Middleware_runs_after_cors_and_before_authentication()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs"));
        var cors = program.IndexOf("app.UseCors(\"UiDev\");", StringComparison.Ordinal);
        var trust = program.IndexOf("app.UseBrowserRequestTrust();", StringComparison.Ordinal);
        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var authorization = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
        cors.Should().BeGreaterThan(0);
        trust.Should().BeGreaterThan(cors);
        authentication.Should().BeGreaterThan(trust);
        authorization.Should().BeGreaterThan(authentication);
        program.Should().Contain("AddBrowserRequestTrust(builder.Configuration, builder.Environment)");
    }

    [Fact]
    public void The_only_exemption_is_the_runtime_enroll_action()
    {
        var assembly = typeof(EnrollController).Assembly;
        var methods = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<BrowserRequestTrustExemptAttribute>() is not null)
            .Select(method => method.DeclaringType!.Name + "." + method.Name)
            .OrderBy(name => name)
            .ToArray();
        var types = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<BrowserRequestTrustExemptAttribute>() is not null)
            .Select(type => type.Name)
            .ToArray();

        methods.Should().Equal("EnrollController.Enroll");
        types.Should().BeEmpty("the exemption is one action, not a controller");
        typeof(EnrollController).GetMethod(nameof(EnrollController.Enroll))!
            .GetCustomAttribute<BrowserRequestTrustExemptAttribute>()!.Reason
            .Should().Be("runtime enrollment: machine client, token-authenticated");
    }

    [Fact]
    public void Every_management_controller_route_is_under_api()
    {
        var offenders = new List<string>();
        foreach (var type in typeof(EnrollController).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(ControllerBase))))
        {
            foreach (var route in type.GetCustomAttributes<RouteAttribute>())
            {
                if (!IsApiTemplate(route.Template))
                    offenders.Add(type.Name + " " + route.Template);
            }

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                foreach (var template in method.GetCustomAttributes().OfType<HttpMethodAttribute>().Select(attribute => attribute.Template))
                {
                    if (string.IsNullOrEmpty(template) || template.StartsWith('{'))
                        continue;
                    if (template.Contains("/api/", StringComparison.OrdinalIgnoreCase) || template.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (template.StartsWith('/') && !template.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                        offenders.Add(type.Name + "." + method.Name + " " + template);
                }
            }
        }

        offenders.Should().BeEmpty();
    }

    private static bool IsApiTemplate(string? template) =>
        template is not null
        && (template.Equals("api/[controller]", StringComparison.OrdinalIgnoreCase)
            || template.StartsWith("api/", StringComparison.OrdinalIgnoreCase));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    private static IEnumerable<string> RepoFiles(string relative)
    {
        var root = Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
}
