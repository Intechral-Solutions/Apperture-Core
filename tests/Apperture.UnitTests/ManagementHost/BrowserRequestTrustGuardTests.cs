using System.Reflection;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Host;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
        var developmentCors = program.IndexOf("if (app.Environment.IsDevelopment())", StringComparison.Ordinal);
        var cors = program.IndexOf("app.UseCors(UiDevCors.PolicyName);", StringComparison.Ordinal);
        var trust = program.IndexOf("app.UseBrowserRequestTrust();", StringComparison.Ordinal);
        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var authorization = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
        developmentCors.Should().BeGreaterThan(0);
        cors.Should().BeGreaterThan(developmentCors);
        trust.Should().BeGreaterThan(cors);
        program.Should().Contain("if (builder.Environment.IsDevelopment())");
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

    /// <summary>
    /// The endpoints Management.Host actually routes (controllers + host auth endpoints), not the attribute templates. Every
    /// unsafe endpoint must fall under a trust rule: Rule U (under <c>/api</c>) or Rule N (navigation metadata), with exactly one
    /// exempt endpoint. A route that escapes both — a <c>~/</c> template, a method-level route, a new minimal POST — fails here.
    /// </summary>
    [Fact]
    public void Every_routed_unsafe_endpoint_falls_under_rule_u_or_rule_n()
    {
        var endpoints = RoutedEndpoints();
        endpoints.Should().NotBeEmpty();

        var unsafeOutsideTrust = endpoints
            .Where(e => e.Methods.Any(m => m is not ("GET" or "HEAD" or "OPTIONS")))
            .Where(e => !e.Route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !e.Navigation)
            .Select(e => $"{string.Join(",", e.Methods)} {e.Route}")
            .ToList();
        unsafeOutsideTrust.Should().BeEmpty();

        endpoints.Where(e => !e.Route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"{string.Join(",", e.Methods)} {e.Route} navigation={e.Navigation}")
            .Should().BeEquivalentTo(
                "GET /signin navigation=False",
                "GET /signin/link navigation=True",
                "GET /signin/step-up navigation=True",
                "POST /signout navigation=True");

        endpoints.Where(e => e.Route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && e.Navigation)
            .Should().BeEmpty("an API endpoint never trades the platform header rule for Rule N");
        endpoints.Where(e => e.Exempt).Select(e => $"{string.Join(",", e.Methods)} {e.Route}")
            .Should().Equal("POST /api/Enroll");
    }

    [Fact]
    public void Delete_schedule_is_an_ordinary_unsafe_api_route_under_rule_u()
    {
        var match = RoutedEndpoints()
            .Where(e => e.Methods.Contains("DELETE") && e.Route.StartsWith("/api/schedules/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        match.Should().ContainSingle();
        match[0].Route.Should().Be("/api/schedules/{id}");
        match[0].Exempt.Should().BeFalse();
        match[0].Navigation.Should().BeFalse();
    }

    [Fact]
    public void Program_maps_only_the_endpoint_sources_the_coverage_guard_enumerates()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src/Apperture.ControlPlane/Apperture.Management.Host/Program.cs"));
        System.Text.RegularExpressions.Regex.Matches(program, @"app\.Map\w*\(").Select(m => m.Value)
            .Should().BeEquivalentTo("app.MapControllers(", "app.MapManagementAuthEndpoints(", "app.MapDefaultEndpoints(");
    }

    private static List<(string Route, IReadOnlyList<string> Methods, bool Navigation, bool Exempt)> RoutedEndpoints()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(EnrollController).Assembly);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<Apperture.Core.Application.Services.IPrincipalContext>(_ => null!);
        builder.Services.AddSingleton<Apperture.Core.Application.Services.IPrincipalToUserResolver>(_ => null!);
        var app = builder.Build();
        app.MapControllers();
        app.MapManagementAuthEndpoints();

        return ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Select(e => (
                Route: "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'),
                Methods: (IReadOnlyList<string>)(e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()?.HttpMethods.ToList() ?? []),
                Navigation: e.Metadata.GetMetadata<NativeNavigationEndpoint>() is not null,
                Exempt: e.Metadata.GetMetadata<BrowserRequestTrustExemptAttribute>() is not null))
            .ToList();
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
