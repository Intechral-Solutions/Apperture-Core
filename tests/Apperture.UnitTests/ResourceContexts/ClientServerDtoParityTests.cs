using System.Reflection;
using FluentAssertions;
using Xunit;
using ClientIntegrationDto = Apperture.Management.Client.IntegrationDto;
using ClientCreateIntegrationRequest = Apperture.Management.Client.CreateIntegrationRequest;
using ClientConnectionDto = Apperture.Management.Client.ConnectionDto;
using ClientCreateConnectionRequest = Apperture.Management.Client.CreateConnectionRequest;
using ClientRunSummaryDto = Apperture.Management.Client.RunSummaryDto;
using ClientRunQueryFilter = Apperture.Management.Client.RunQueryFilter;
using ClientRunDetailDto = Apperture.Management.Client.RunDetailDto;
using ClientIntegrationFleetRowDto = Apperture.Management.Client.IntegrationFleetRowDto;
using ServerIntegrationDto = Apperture.Management.Api.Controllers.IntegrationDto;
using ServerCreateIntegrationRequestBody = Apperture.Management.Api.Controllers.CreateIntegrationRequestBody;
using ServerConnectionDto = Apperture.Management.Api.Controllers.ConnectionDto;
using ServerCreateConnectionRequestBody = Apperture.Management.Api.Controllers.CreateConnectionRequestBody;
using ServerRunSummaryDto = Apperture.ControlPlane.Security.RunSummaryDto;
using ServerRunQueryFilter = Apperture.ControlPlane.Security.RunQueryFilter;
using ServerRunDetailDto = Apperture.ControlPlane.Security.RunDetailDto;
using ServerIntegrationFleetRowProjection = Apperture.Core.Application.Integration.IntegrationFleetRowProjection;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A (A2, R14): every server-side wire DTO must have a client twin exposing at least the
/// same set of public properties. Tasks 2-4 closed the ResourceContextId/ResourceContext drift
/// this test would have caught; this test makes that class of drift a CI failure going forward.
///
/// Note: <see cref="ServerIntegrationDto"/>, <see cref="ServerCreateIntegrationRequestBody"/>,
/// <see cref="ServerConnectionDto"/>, and <see cref="ServerCreateConnectionRequestBody"/> are
/// top-level public records declared in the controller source files (not nested inside the
/// controller classes), so no accessibility changes were needed to reflect over them.
/// </summary>
public class ClientServerDtoParityTests
{
    public static IEnumerable<object[]> DtoPairs()
    {
        yield return new object[] { typeof(ServerIntegrationDto), typeof(ClientIntegrationDto) };
        yield return new object[] { typeof(ServerCreateIntegrationRequestBody), typeof(ClientCreateIntegrationRequest) };
        yield return new object[] { typeof(ServerConnectionDto), typeof(ClientConnectionDto) };
        yield return new object[] { typeof(ServerCreateConnectionRequestBody), typeof(ClientCreateConnectionRequest) };
        yield return new object[] { typeof(ServerRunSummaryDto), typeof(ClientRunSummaryDto) };
        yield return new object[] { typeof(ServerRunQueryFilter), typeof(ClientRunQueryFilter) };
        yield return new object[] { typeof(ServerRunDetailDto), typeof(ClientRunDetailDto) };
        yield return new object[] { typeof(ServerIntegrationFleetRowProjection), typeof(ClientIntegrationFleetRowDto) };
    }

    [Theory]
    [MemberData(nameof(DtoPairs))]
    public void ClientDtoHasEveryServerProperty(Type serverType, Type clientType)
    {
        var serverProps = serverType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var clientProps = clientType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = serverProps.Except(clientProps).ToList();

        missing.Should().BeEmpty(
            $"the client type {clientType.FullName} is missing properties that {serverType.FullName} already exposes: {string.Join(", ", missing)}");
    }
}
