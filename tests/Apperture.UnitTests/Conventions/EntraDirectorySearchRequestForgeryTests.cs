using System.Reflection;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Apperture.UnitTests.Conventions;

/// <summary>
/// The two Entra directory searches (design gate §18.1 G1/G2) call Microsoft Graph with the registration's stored secret and
/// write the registration's directory-discovery outcome, so they are unsafe requests: POST with a typed JSON body. As POSTs
/// under <c>/api</c> they are covered by Rule U (exact Origin + platform header) like every other mutation. A GET cannot reach
/// them; a same-site sibling's navigation would pass Rule G.
/// </summary>
public sealed class EntraDirectorySearchRequestForgeryTests
{
    private static readonly Type Controller = typeof(ConfigEntraTenantsController);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static IReadOnlyList<string> Verbs(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList();

    private static string? Template(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.Template).FirstOrDefault(t => t is not null);

    private static List<MethodInfo> DirectoryActions() =>
        Actions(Controller).Where(a => Template(a)?.Contains("/directory/", StringComparison.Ordinal) == true).ToList();

    [Fact]
    public void Both_directory_searches_exist_and_are_post_only()
    {
        var directory = DirectoryActions();

        directory.Select(Template).Should().BeEquivalentTo("{id}/directory/users", "{id}/directory/groups");
        directory.Should().OnlyContain(a => Verbs(a).SequenceEqual(new[] { "POST" }), "a directory search writes the discovery outcome");
    }

    [Fact]
    public void No_get_route_anywhere_in_the_api_reaches_a_tenant_directory_search()
    {
        var getTemplates = Controller.Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(c => Actions(c).Select(a => (Controller: c, Action: a)))
            .Where(x => Verbs(x.Action).Contains("GET"))
            .Select(x => (x.Controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "") + "/" + Template(x.Action))
            .ToList();

        getTemplates.Should().NotContain(t => t.Contains("entra-tenants", StringComparison.Ordinal) && t.Contains("directory", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_search_takes_one_typed_json_body_and_nothing_from_the_query_string()
    {
        foreach (var action in DirectoryActions())
        {
            action.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes.Should().Equal("application/json");
            var body = action.GetParameters().Where(p => p.GetCustomAttribute<FromBodyAttribute>() is not null).ToList();
            body.Should().ContainSingle().Which.ParameterType.Should().Be(typeof(EntraDirectorySearchRequestBody));
            action.GetParameters().Should().NotContain(p => p.GetCustomAttribute<FromQueryAttribute>() != null);
        }
    }

    [Fact]
    public void Authorization_is_unchanged()
    {
        Controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        Controller.GetCustomAttribute<ApiControllerAttribute>().Should().NotBeNull();
        DirectoryActions().Should().NotContain(m => m.GetCustomAttributes<AllowAnonymousAttribute>().Any());
    }
}
