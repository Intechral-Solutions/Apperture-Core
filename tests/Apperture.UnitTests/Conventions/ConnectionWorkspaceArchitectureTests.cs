using System.Reflection;
using System.Text.RegularExpressions;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;
using ClientConnectionDto = Apperture.Management.Client.ConnectionDto;
using ServerConnectionDto = Apperture.Management.Api.Controllers.ConnectionDto;

namespace Apperture.UnitTests.Conventions;

/// <summary>
/// Guards for the Connection-workspace rulings: the workspace is the existing Connection page, not a new object. There is
/// no Service entity, table, id, route or permission; ownership is only ever <c>Connection.ResourceContextId</c>; and
/// ResourceContext is not an authorization boundary.
/// </summary>
public sealed class ConnectionWorkspaceArchitectureTests
{
    private static readonly Assembly[] ProductAssemblies =
    [
        typeof(Connection).Assembly,
        typeof(IConnectionManagementService).Assembly,
        typeof(ConnectionManagementService).Assembly,
        typeof(AppertureDbContext).Assembly,
        typeof(ServerConnectionDto).Assembly,
        typeof(ClientConnectionDto).Assembly,
        typeof(Apperture.Management.Ui.Shared.ConnectionWorkspacePresentation).Assembly,
    ];

    private static readonly string[] ForbiddenServiceTypeNames =
    [
        "Service", "ServiceId", "ServiceDefinition", "ServiceEntity", "ServiceRecord", "ServiceWorkspace", "IService",
    ];

    // DV2 / DV5 -------------------------------------------------------------------------------------

    [Fact]
    public void DV5_no_type_named_service_or_service_definition_or_service_experience_exists()
    {
        var offenders = ProductAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => ForbiddenServiceTypeNames.Contains(t.Name, StringComparer.Ordinal)
                        || t.Name.StartsWith("IServiceExperience", StringComparison.Ordinal)
                        || t.Name.StartsWith("ServiceExperience", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty("'Service' is architectural language for the Connection workspace, never a type");
    }

    [Fact]
    public void DV2_there_is_no_service_table_entity_or_dbset()
    {
        var dbSets = typeof(AppertureDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("DbSet", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();

        dbSets.Should().NotBeEmpty("precondition: the reflection finds the context's sets");
        dbSets.Should().NotContain(n => Regex.IsMatch(n, "^Services?(Definitions?)?$"));
        typeof(AppertureDbContext).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Entity", StringComparison.Ordinal))
            .Should().NotContain(t => t.Name.StartsWith("Service", StringComparison.Ordinal) && !t.Name.StartsWith("ServiceBus", StringComparison.Ordinal));
    }

    // RC2 -------------------------------------------------------------------------------------------

    [Fact]
    public void RC2_ownership_is_a_single_column_on_the_connection_and_the_read_contract_has_no_independent_context_field()
    {
        var entityContextColumns = typeof(AppertureDbContext).Assembly.GetTypes()
            .Where(t => t.Name == "ConnectionEntity")
            .SelectMany(t => t.GetProperties())
            .Where(p => p.Name.Contains("ResourceContext", StringComparison.Ordinal))
            .Select(p => p.Name);
        entityContextColumns.Should().Equal("ResourceContextId");

        foreach (var dto in new[] { typeof(ServerConnectionDto), typeof(ClientConnectionDto) })
        {
            dto.GetProperties().Where(p => p.Name.Contains("Context", StringComparison.Ordinal)).Select(p => p.Name)
                .Should().BeEquivalentTo(["ResourceContextId", "ResourceContext"], $"{dto.Name} exposes the Connection's own ownership only");
        }
    }

    [Fact]
    public void Future_organisational_grouping_is_not_predesigned_on_the_connection_surface()
    {
        var forbidden = new Regex("^(Department|Domain|Group|OrganizationalArea|OrgUnit|Grouping|Area)(Id)?$");
        foreach (var type in new[] { typeof(Connection), typeof(ServerConnectionDto), typeof(ClientConnectionDto) })
            type.GetProperties().Select(p => p.Name).Should().NotContain(n => forbidden.IsMatch(n), type.Name);
    }

    // RC4 -------------------------------------------------------------------------------------------

    [Fact]
    public async Task RC4_reading_a_connection_is_authorized_the_same_way_in_every_context()
    {
        var evaluated = new List<AuthorizationEvaluationContext>();
        var auth = new Mock<IAuthorizationEvaluationService>();
        auth.Setup(a => a.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .Callback<AuthorizationEvaluationContext, CancellationToken>((c, _) => evaluated.Add(c))
            .ReturnsAsync(true);
        var actor = new Mock<IActor>();
        actor.SetupGet(a => a.Id).Returns(new ActorId("user:reader"));
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actor.Object);

        var shared = NewConnection("conn-s", ResourceContextIds.Shared);
        var owned = NewConnection("conn-o", new ResourceContextId("app-zeta"));
        var repository = new Mock<IConnectionRepository>();
        repository.Setup(r => r.GetByIdAsync(shared.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shared);
        repository.Setup(r => r.GetByIdAsync(owned.Id, It.IsAny<CancellationToken>())).ReturnsAsync(owned);
        var sut = new ConnectionManagementService(
            principal.Object, auth.Object, repository.Object, null!, null!, null!, null!, null!, null!, null!);

        (await sut.GetConnectionAsync(shared.Id)).Should().BeSameAs(shared);
        (await sut.GetConnectionAsync(owned.Id)).Should().BeSameAs(owned);

        evaluated.Should().HaveCount(2);
        evaluated.Should().OnlyContain(c => c.Permission.Action == ActionConstants.ConnectionRead);
        evaluated.Select(c => c.Permission.Resource!.ScopeKey).Distinct().Should().Equal(
            [ScopeKeyConstants.Instance], "authorization scope is instance for both; ResourceContext plays no part");
        evaluated.Select(c => System.Text.Json.JsonSerializer.Serialize(c.Permission)).Should().NotContain(s => s.Contains("app-zeta") || s.Contains("shared"));
    }

    // DV1 / AR --------------------------------------------------------------------------------------

    [Fact]
    public void No_service_permission_action_exists()
    {
        var actions = typeof(ActionConstants).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        actions.Should().NotContain(a => a.StartsWith("service.", StringComparison.OrdinalIgnoreCase),
            "no service.* permission exists in the derived Connection-workspace architecture");
    }

    private static Connection NewConnection(string id, ResourceContextId context) =>
        new(new ConnectionId(id), new ConnectorDefinitionId("Apperture.Platform.Sql"), id,
            new Dictionary<string, string>(), new Dictionary<string, string>(), null, null)
        {
            ResourceContextId = context
        };
}
