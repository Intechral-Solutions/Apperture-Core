using System.Reflection;
using System.Text.Json;
using Apperture.ConnectorResolution;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ConnectionTools;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Management.Api.Controllers;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Conventions;

/// <summary>
/// Boundaries for the connector tool seam (Services v1). The seam is one contract plus a typed capability interface: no
/// registry, no contributor, no generic command or capability list, nothing executable on a connector definition, and no
/// compile-time reference from the control plane or the management API to a concrete connector assembly.
/// </summary>
public sealed class ConnectionToolsArchitectureTests
{
    private static IEnumerable<string> ReferencedConnectorAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(n => n.StartsWith("Apperture.Connectors.", StringComparison.Ordinal));

    // AR1 / AR2 -------------------------------------------------------------------------------------

    [Fact]
    public void AR1_connector_resolution_still_references_no_concrete_connector_assembly()
    {
        ReferencedConnectorAssemblies(typeof(ConnectorClientFactoryRegistry).Assembly).Should().BeEmpty();
    }

    // ControlPlane.Security already referenced Apperture.Connectors.CampusNexus before Services began (for an unrelated option
    // provider). AR2 is a no-NEW-reference guard: that baseline is pinned exactly, so any further connector reference -- and
    // in particular one for a tool provider -- fails here.
    [Fact]
    public void AR2_the_control_plane_the_management_api_and_the_application_layer_acquire_no_new_connector_assembly_reference()
    {
        ReferencedConnectorAssemblies(typeof(ConnectionToolAccess).Assembly).Should().BeEquivalentTo(
            ["Apperture.Connectors.CampusNexus"], "the pre-existing baseline, unrelated to tools");
        ReferencedConnectorAssemblies(typeof(ConnectionToolsController).Assembly).Should().BeEmpty();
        ReferencedConnectorAssemblies(typeof(IConnectionToolProvider).Assembly).Should().BeEmpty();
    }

    // The Security assembly's baseline reference cannot prove the tool seam itself is clean, so its own types are checked too:
    // no field, constructor, method or property type of the seam may come from a concrete connector assembly.
    [Fact]
    public void AR2_the_tool_seam_types_themselves_use_no_type_from_a_concrete_connector_assembly()
    {
        var seam = typeof(IConnectionToolProvider).Assembly.GetTypes().Where(t => t.Namespace == typeof(IConnectionToolProvider).Namespace)
            .Concat([typeof(ConnectionToolAccess), typeof(ConnectionToolsController)])
            .ToList();

        var usedTypes = seam
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(m => m switch
            {
                FieldInfo f => new[] { f.FieldType },
                PropertyInfo p => new[] { p.PropertyType },
                MethodBase mb => mb.GetParameters().Select(p => p.ParameterType).Concat(mb is MethodInfo mi ? [mi.ReturnType] : []),
                _ => []
            })
            .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : [t])
            .Distinct()
            .ToList();

        usedTypes.Should().NotBeEmpty();
        usedTypes.Where(t => t.Assembly.GetName().Name!.StartsWith("Apperture.Connectors.", StringComparison.Ordinal))
            .Should().BeEmpty("the seam depends on abstractions only; concrete connectors register themselves");
    }

    // AR3 -------------------------------------------------------------------------------------------

    [Fact]
    public void AR3_the_connector_definition_carries_no_executable_member()
    {
        var executable = typeof(ConnectorDefinition)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m is MethodInfo mi && !mi.IsSpecialName && !IsRecordPlumbing(mi.Name))
            .Cast<MethodInfo>()
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType)
                        || (m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                        || m.ReturnType == typeof(ValueTask)
                        || m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
            .Select(m => m.Name)
            .ToList();
        executable.Should().BeEmpty("a definition is metadata; it has no operation to invoke");

        typeof(ConnectorDefinition).GetProperties().Select(p => p.PropertyType)
            .Should().NotContain(t => typeof(Delegate).IsAssignableFrom(t) || typeof(IConnectionToolProvider).IsAssignableFrom(t),
                "tool availability is host composition, never part of the definition");
    }

    [Fact]
    public void AR3_the_connector_definition_contributor_stays_metadata_only()
    {
        var methods = typeof(IConnectorDefinitionContributor).GetMethods();

        methods.Should().ContainSingle();
        methods[0].Name.Should().Be(nameof(IConnectorDefinitionContributor.Contribute));
        methods[0].ReturnType.Should().Be(typeof(void));
        methods[0].GetParameters().Select(p => p.ParameterType).Should().Equal(typeof(IConnectorDefinitionRegistry));
    }

    // C1: the smallest seam -------------------------------------------------------------------------

    [Fact]
    public void The_provider_contract_is_a_marker_and_nothing_else()
    {
        typeof(IConnectionToolProvider).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(nameof(IConnectionToolProvider.ConnectorDefinitionId), nameof(IConnectionToolProvider.Availability));
        typeof(IConnectionToolProvider).GetMethods().Where(m => !m.IsSpecialName).Should().BeEmpty("properties only: no operation");
    }

    [Fact]
    public void No_registry_contributor_sanity_service_or_generic_command_type_exists_for_tools()
    {
        var toolTypes = typeof(IConnectionToolProvider).Assembly.GetTypes()
            .Concat(typeof(ConnectionToolAccess).Assembly.GetTypes())
            .Where(t => t.Namespace == typeof(IConnectionToolProvider).Namespace || t.Name.Contains("ConnectionTool", StringComparison.Ordinal))
            .ToList();

        toolTypes.Should().NotBeEmpty("precondition: the seam's types are found");
        toolTypes.Select(t => t.Name).Should().NotContain(n =>
            n.Contains("Registry", StringComparison.Ordinal)
            || n.Contains("Contributor", StringComparison.Ordinal)
            || n.Contains("Sanity", StringComparison.Ordinal)
            || n.Contains("Command", StringComparison.Ordinal)
            || n.Contains("Capabilities", StringComparison.Ordinal));

        var genericExecution = toolTypes.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name is "Execute" or "ExecuteAsync" or "Invoke" or "InvokeAsync" or "Run" or "RunAsync")
            .ToList();
        genericExecution.Should().BeEmpty("there is no Execute(string) or Invoke(tool, payload) surface");

        typeof(ConnectionToolsDto).GetProperties().Should().OnlyContain(p => p.PropertyType == typeof(bool),
            "one typed flag per real tool, never a list of capability names");
    }

    [Fact]
    public void The_seam_carries_no_json_or_string_command_parameter()
    {
        var offenders = typeof(IConnectionToolProvider).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(IConnectionToolProvider).Namespace && t.IsInterface)
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(JsonDocument) || p.ParameterType == typeof(JsonElement)
                                                   || p.Name is "sql" or "statement" or "command" or "query" or "payload"))
            .Select(m => m.DeclaringType!.Name + "." + m.Name)
            .ToList();

        offenders.Should().BeEmpty("the SQL contract takes schema and table VALUES, never a statement");
    }

    private static bool IsRecordPlumbing(string name) =>
        name is "Equals" or "GetHashCode" or "ToString" or "Deconstruct" or "PrintMembers" or "<Clone>$" or "GetIdentity";

    // ---- Reviewer-required hardening of the guards ------------------------------------------------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    // GetReferencedAssemblies only lists assemblies whose types are actually used, so a fresh ProjectReference to a connector
    // project would slip through until code used it. The project files are pinned directly as well.
    [Theory]
    [InlineData("src/Apperture.ControlPlane/Apperture.ControlPlane.Security/Apperture.ControlPlane.Security.csproj", "Apperture.Connectors.CampusNexus")]
    [InlineData("src/Apperture.ControlPlane/Apperture.Management.Api/Apperture.Management.Api.csproj")]
    [InlineData("src/Apperture.Core/Apperture.Core.Application/Apperture.Core.Application.csproj")]
    [InlineData("src/Apperture.ConnectorResolution/Apperture.ConnectorResolution.csproj")]
    public void AR2_project_files_reference_no_connector_project_beyond_the_pinned_baseline(string project, params string[] baseline)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), project.Replace('/', Path.DirectorySeparatorChar)));

        var referenced = System.Text.RegularExpressions.Regex.Matches(text, @"Include=""[^""]*?(Apperture\.Connectors\.[A-Za-z]+)\.csproj""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        referenced.Should().BeEquivalentTo(baseline);
    }

    [Fact]
    public void No_registry_or_catalog_of_tool_providers_exists_anywhere_in_the_product()
    {
        var offenders = new[]
            {
                typeof(ConnectionToolAccess).Assembly, typeof(ConnectionToolsController).Assembly,
                typeof(IConnectionToolProvider).Assembly, typeof(ConnectorClientFactoryRegistry).Assembly
            }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericArguments().Contains(typeof(IConnectionToolProvider)))
                        || t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType.IsGenericType
                                                                                   && p.ParameterType.GetGenericArguments().Contains(typeof(IConnectionToolProvider)))
                                                        && t != typeof(ConnectionToolAccess)))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty("the DI set of providers is consumed in exactly one place, ConnectionToolAccess");
    }

    [Fact]
    public void Only_the_tool_access_component_consumes_providers_so_no_other_path_can_skip_authorization()
    {
        var providerTypes = new[] { typeof(IConnectionToolProvider), typeof(ISqlStructureBrowser) };
        var consumers = new[]
            {
                typeof(ConnectionToolAccess).Assembly, typeof(ConnectionToolsController).Assembly,
                typeof(Apperture.Management.Client.ConnectionToolsDto).Assembly, typeof(Apperture.Management.Ui.Shared.ConnectionWorkspacePresentation).Assembly
            }
            .SelectMany(a => a.GetTypes())
            .Where(t => t != typeof(ConnectionToolAccess) && t.DeclaringType != typeof(ConnectionToolAccess))
            .Where(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(m => m switch
                {
                    FieldInfo f => new[] { f.FieldType },
                    PropertyInfo p => new[] { p.PropertyType },
                    MethodBase mb => mb.GetParameters().Select(p => p.ParameterType).Concat(mb is MethodInfo mi ? [mi.ReturnType] : []),
                    _ => []
                })
                .SelectMany(ty => ty.IsGenericType ? ty.GetGenericArguments().Append(ty) : [ty])
                .Any(ty => providerTypes.Contains(ty)))
            .Select(t => t.FullName)
            .ToList();

        consumers.Should().BeEmpty("controllers and services reach tools only through IConnectionToolAccess, which authorizes first");
    }

}
