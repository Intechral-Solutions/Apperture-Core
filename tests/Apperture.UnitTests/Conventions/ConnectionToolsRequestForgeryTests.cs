using System.Reflection;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Apperture.UnitTests.Conventions;

/// <summary>
/// The SQL structure browse endpoints each log in to an external system, consume browse capacity and write an audit record, so
/// they must not be safe-method (GET) endpoints that a cross-site navigation can trigger. The platform has no antiforgery-token
/// middleware: its request-forgery posture is a SameSite=Lax session cookie (not sent on cross-site POSTs) plus typed
/// <c>[ApiController]</c> JSON bodies. These tests pin the wiring that posture depends on: POST only, a JSON body that a plain
/// cross-site form cannot produce, no alternate GET path, and unchanged authorization.
/// </summary>
public sealed class ConnectionToolsRequestForgeryTests
{
    private static readonly Type Controller = typeof(ConnectionToolsController);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static IReadOnlyList<string> Verbs(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList();

    private static string? Template(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.Template).FirstOrDefault(t => t is not null);

    private static IEnumerable<MethodInfo> RemoteBrowseActions() =>
        Actions(Controller).Where(a => Template(a)?.StartsWith("sql-structure/", StringComparison.Ordinal) == true);

    [Fact]
    public void The_three_remote_browse_operations_exist_and_are_post_only()
    {
        var browse = RemoteBrowseActions().ToList();

        browse.Select(Template).Should().BeEquivalentTo("sql-structure/schemas", "sql-structure/tables", "sql-structure/columns");
        browse.Should().OnlyContain(a => Verbs(a).SequenceEqual(new[] { "POST" }), "no browse action may also accept GET or any other verb");
    }

    [Fact]
    public void No_GET_route_can_perform_a_remote_browse()
    {
        // Across every controller in the API assembly, not just this one: an alternate GET path would defeat the change.
        var controllers = typeof(ConnectionToolsController).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t));
        var getActions = controllers.SelectMany(c => Actions(c).Select(a => (Controller: c, Action: a)))
            .Where(x => Verbs(x.Action).Contains("GET"))
            .ToList();

        getActions.Where(x => Template(x.Action)?.Contains("sql-structure", StringComparison.OrdinalIgnoreCase) == true)
            .Should().BeEmpty();
        getActions.Where(x => x.Controller == Controller).Select(x => Template(x.Action))
            .Should().ContainSingle().Which.Should().BeNull("the only GET on the tools controller is the local availability lookup");
    }

    [Fact]
    public void Only_the_tools_controller_can_reach_the_tool_access_so_no_other_route_can_start_a_remote_browse()
    {
        var consumers = typeof(ConnectionToolsController).Assembly.GetTypes()
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p =>
                p.ParameterType == typeof(Apperture.Core.Application.ConnectionTools.IConnectionToolAccess))));

        consumers.Should().BeEquivalentTo(new[] { Controller });
    }

    [Fact]
    public void Only_the_local_availability_lookup_stays_a_GET_and_no_other_verb_exists_on_the_controller()
    {
        var all = Actions(Controller).Select(a => (Template: Template(a), Verbs: string.Join(",", Verbs(a)))).ToList();

        all.Should().BeEquivalentTo(new[]
        {
            (Template: (string?)null, Verbs: "GET"),
            (Template: "sql-structure/schemas", Verbs: "POST"),
            (Template: "sql-structure/tables", Verbs: "POST"),
            (Template: "sql-structure/columns", Verbs: "POST"),
        });
        Controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<AcceptVerbsAttribute>().Any()).Should().BeEmpty();
    }

    [Fact]
    public void Each_browse_operation_requires_a_JSON_body_that_a_cross_site_form_cannot_send()
    {
        foreach (var action in RemoteBrowseActions())
        {
            var consumes = action.GetCustomAttribute<ConsumesAttribute>();
            consumes.Should().NotBeNull($"{action.Name} must reject non-JSON content types (415) before the action runs");
            consumes!.ContentTypes.Should().Equal("application/json");

            var body = action.GetParameters().Where(p => p.GetCustomAttribute<FromBodyAttribute>() is not null).ToList();
            body.Should().ContainSingle($"{action.Name} takes its operator input from one typed JSON body");
            action.GetParameters().Should().NotContain(p => p.GetCustomAttribute<FromQueryAttribute>() != null,
                "operator input must not travel in the query string of a state-consuming request");
        }
    }

    [Fact]
    public void Each_browse_body_is_a_strongly_typed_request_record_not_a_dictionary_or_json_element()
    {
        var bodyTypes = RemoteBrowseActions()
            .Select(a => a.GetParameters().Single(p => p.GetCustomAttribute<FromBodyAttribute>() is not null).ParameterType)
            .ToList();

        bodyTypes.Should().BeEquivalentTo(new[] { typeof(SqlSchemasRequestDto), typeof(SqlTablesRequestDto), typeof(SqlColumnsRequestDto) });
    }

    [Fact]
    public void Authorization_is_unchanged_the_controller_is_authorized_and_no_action_opts_out()
    {
        Controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        Controller.GetCustomAttribute<ApiControllerAttribute>().Should().NotBeNull("[ApiController] is what makes an absent or unparsable body a 400");
        Controller.GetMethods().Should().NotContain(m => m.GetCustomAttributes<AllowAnonymousAttribute>().Any());
        Controller.GetCustomAttributes<AllowAnonymousAttribute>().Should().BeEmpty();
    }

    [Fact]
    public void The_tools_route_prefix_is_unchanged()
    {
        Controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/connections/{id}/tools");
    }
}
