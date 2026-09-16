using System.Collections;
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
/// CTX-3B Task 7 (Part D) strengthens this from property-name containment to type compatibility
/// (<see cref="PropertyTypesAreCompatible"/>) plus exact-mirror property-count checking
/// (<see cref="ExactMirrorPairsExposeNoExtraClientProperties"/>), because name containment alone lets a
/// same-named-but-wrong-typed client property, or a client type that has silently drifted extra
/// properties, slip through undetected.
///
/// Note: <see cref="ServerIntegrationDto"/>, <see cref="ServerCreateIntegrationRequestBody"/>,
/// <see cref="ServerConnectionDto"/>, and <see cref="ServerCreateConnectionRequestBody"/> are
/// top-level public records declared in the controller source files (not nested inside the
/// controller classes), so no accessibility changes were needed to reflect over them.
/// </summary>
public class ClientServerDtoParityTests
{
    /// <summary>
    /// Each row is (serverType, clientType, isExactMirror). <c>isExactMirror</c> was determined
    /// empirically (Task 7 Part D2): a pair started marked <see langword="true"/>, and was flipped to
    /// <see langword="false"/> only when <see cref="ExactMirrorPairsExposeNoExtraClientProperties"/>
    /// actually failed for it -- never guessed. Every <see langword="false"/> row below carries a comment
    /// naming the specific extra client-side properties that were observed to cause the failure.
    /// </summary>
    public static IEnumerable<object[]> DtoPairs()
    {
        yield return new object[] { typeof(ServerIntegrationDto), typeof(ClientIntegrationDto), true };
        yield return new object[] { typeof(ServerCreateIntegrationRequestBody), typeof(ClientCreateIntegrationRequest), true };
        yield return new object[] { typeof(ServerConnectionDto), typeof(ClientConnectionDto), true };
        yield return new object[] { typeof(ServerCreateConnectionRequestBody), typeof(ClientCreateConnectionRequest), true };
        yield return new object[] { typeof(ServerRunSummaryDto), typeof(ClientRunSummaryDto), true };
        yield return new object[] { typeof(ServerRunQueryFilter), typeof(ClientRunQueryFilter), true };
        yield return new object[] { typeof(ServerRunDetailDto), typeof(ClientRunDetailDto), true };
        yield return new object[] { typeof(ServerIntegrationFleetRowProjection), typeof(ClientIntegrationFleetRowDto), true };
    }

    [Theory]
    [MemberData(nameof(DtoPairs))]
    public void ClientDtoHasEveryServerProperty(Type serverType, Type clientType, bool _)
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

    /// <summary>
    /// Part D1: for every property name present on both sides, the two property types must be
    /// "compatible" -- see <see cref="AreCompatible"/> for the exact rule. This catches a client property
    /// that shares a server property's name but silently drifted to an incompatible type (e.g. a
    /// <see cref="string"/> where the server has an <see langword="int"/>, enum, or nested DTO), which
    /// <see cref="ClientDtoHasEveryServerProperty"/> cannot see because it compares names only.
    /// </summary>
    [Theory]
    [MemberData(nameof(DtoPairs))]
    public void PropertyTypesAreCompatible(Type serverType, Type clientType, bool _)
    {
        var serverProps = serverType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);
        var clientProps = clientType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

        var incompatible = new List<string>();
        foreach (var name in serverProps.Keys.Where(clientProps.ContainsKey))
        {
            var serverPropertyType = serverProps[name].PropertyType;
            var clientPropertyType = clientProps[name].PropertyType;
            if (!AreCompatible(serverPropertyType, clientPropertyType))
            {
                incompatible.Add(
                    $"{name} (server: {serverPropertyType.FullName}, client: {clientPropertyType.FullName})");
            }
        }

        incompatible.Should().BeEmpty(
            $"every property shared by name between {serverType.FullName} and {clientType.FullName} must have a compatible type, but: {string.Join("; ", incompatible)}");
    }

    /// <summary>
    /// Part D2: for pairs marked as exact mirrors in <see cref="DtoPairs"/>, the client must expose no
    /// properties the server lacks. Combined with <see cref="ClientDtoHasEveryServerProperty"/> (which
    /// already covers the other direction), this pins an exactly-equal property set -- and hence an equal
    /// count -- for those pairs. Non-exact pairs are intentionally not checked here; see the comment on
    /// their <see cref="DtoPairs"/> row for why they carry extra client-side properties.
    /// </summary>
    [Theory]
    [MemberData(nameof(DtoPairs))]
    public void ExactMirrorPairsExposeNoExtraClientProperties(Type serverType, Type clientType, bool isExactMirror)
    {
        if (!isExactMirror)
            return;

        var serverProps = serverType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var clientProps = clientType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var extra = clientProps.Except(serverProps).ToList();

        extra.Should().BeEmpty(
            $"{clientType.FullName} is declared an exact mirror of {serverType.FullName} in DtoPairs() but exposes properties the server does not: {string.Join(", ", extra)}");
    }

    /// <summary>
    /// Guards clause 4 of <see cref="AreCompatible(Type, Type)"/> against becoming a rubber stamp: a
    /// server/client pair whose names match the house Dto/Projection twin convention, but whose property
    /// <em>types</em> have genuinely drifted apart, must still be reported incompatible. A plausible name
    /// is not proof of a real twin.
    /// </summary>
    [Fact]
    public void HouseTwinNamingConvention_still_rejects_a_property_type_that_genuinely_disagrees()
    {
        // Both names strip to "FakeGadget", satisfying clause 4's naming half -- but Count is int on the
        // server and string on the client, so the recursive property check must still fail the pair.
        AreCompatible(typeof(FakeGadgetProjection), typeof(FakeGadgetDto)).Should().BeFalse(
            "FakeGadgetProjection/FakeGadgetDto match by the Dto/Projection naming convention, but Count disagrees in type (int vs string) between them -- a same-named-but-incompatible twin must not slip through");
    }

    /// <summary>
    /// Companion to the above: a server/client pair whose names match the house convention but where the
    /// client is simply missing a server property must also still fail.
    /// </summary>
    [Fact]
    public void HouseTwinNamingConvention_still_rejects_a_property_that_the_client_is_missing()
    {
        // Both names strip to "FakeGizmo" -- but the client twin never declares Count at all.
        AreCompatible(typeof(FakeGizmoProjection), typeof(FakeGizmoDto)).Should().BeFalse(
            "FakeGizmoProjection/FakeGizmoDto match by the Dto/Projection naming convention, but the client twin does not expose Count at all");
    }

    /// <summary>
    /// Positive counterpart: when the names match by convention <em>and</em> every property genuinely
    /// corresponds, clause 4 must accept the pair -- otherwise the recursive check would be pure theater.
    /// </summary>
    [Fact]
    public void HouseTwinNamingConvention_accepts_a_pair_whose_properties_genuinely_correspond()
    {
        // Both names strip to "FakeWidget", and every property matches by name and type.
        AreCompatible(typeof(FakeWidgetProjection), typeof(FakeWidgetDto)).Should().BeTrue();
    }

    private sealed record FakeWidgetProjection(int Count, string Name);
    private sealed record FakeWidgetDto(int Count, string Name);

    private sealed record FakeGadgetProjection(int Count, string Name);
    private sealed record FakeGadgetDto(string Count, string Name);

    private sealed record FakeGizmoProjection(int Count, string Name);
    private sealed record FakeGizmoDto(string Name);

    /// <summary>
    /// Two property types are compatible, after unwrapping <see cref="Nullable{T}"/> to <c>T</c> on both
    /// sides, when any of the following holds:
    /// <list type="bullet">
    /// <item><description>the two <see cref="Type"/>s are identical (the normal case for shared types such
    /// as <c>ResourceContextRef</c>, which both assemblies reference from
    /// <c>Apperture.Core.Domain</c>);</description></item>
    /// <item><description>both are collections (<see cref="IEnumerable"/> and not <see cref="string"/>)
    /// whose element types are compatible by this same rule;</description></item>
    /// <item><description>the two types have the same <see cref="MemberInfo.Name"/> (hand-maintained
    /// client twins of server types -- <c>Apperture.Management.Client</c> deliberately does not reference
    /// <c>Apperture.Core.Application</c>, so twins are expected and legitimate);</description></item>
    /// <item><description>the two names match after stripping a trailing house-convention twin suffix from
    /// either side (see <see cref="AreHouseTwins"/>), <em>and</em> their properties correspond
    /// recursively.</description></item>
    /// </list>
    /// Anything else is incompatible.
    /// </summary>
    private static bool AreCompatible(Type serverType, Type clientType) =>
        AreCompatible(serverType, clientType, new HashSet<(Type Server, Type Client)>());

    private static bool AreCompatible(Type serverType, Type clientType, HashSet<(Type Server, Type Client)> visited)
    {
        var server = Nullable.GetUnderlyingType(serverType) ?? serverType;
        var client = Nullable.GetUnderlyingType(clientType) ?? clientType;

        if (server == client)
            return true;

        if (TryGetElementType(server, out var serverElement) && TryGetElementType(client, out var clientElement))
            return AreCompatible(serverElement, clientElement, visited);

        if (string.Equals(server.Name, client.Name, StringComparison.Ordinal))
            return true;

        return AreHouseTwins(server, client, visited);
    }

    /// <summary>
    /// Clause 4. <c>Apperture.Management.Client</c> deliberately does not reference the server-side
    /// assemblies (<c>Apperture.Core.Application</c>, <c>Apperture.ControlPlane.Security</c>), so every
    /// client type that mirrors a server type is a hand-maintained twin, not a shared type. The codebase's
    /// naming convention for those twins is a trailing <c>Dto</c> on the client side and, for
    /// application-layer projections, a trailing <c>Projection</c> on the server side (e.g.
    /// <c>IntegrationReadinessProjection</c> / <c>IntegrationReadinessDto</c>). A bare name match after
    /// stripping those suffixes is a naming convention, not proof of compatibility on its own -- two types
    /// that merely happen to be named alike after stripping, but whose shapes have actually drifted apart,
    /// must still fail. So once the normalized names match, this recurses into the properties using the
    /// same server-properties-subset-of-client-properties containment rule
    /// <see cref="ClientDtoHasEveryServerProperty"/> applies at the top level, checking each shared
    /// property's type for compatibility in turn. <paramref name="visited"/> guards against infinite
    /// recursion on a self-referential or mutually-referential pair of DTOs: a pair already in progress
    /// higher up this same recursion is treated as compatible rather than walked again.
    /// </summary>
    private static bool AreHouseTwins(Type server, Type client, HashSet<(Type Server, Type Client)> visited)
    {
        if (!string.Equals(StripTrailingTwinSuffix(server.Name), StripTrailingTwinSuffix(client.Name), StringComparison.Ordinal))
            return false;

        if (!visited.Add((server, client)))
            return true; // already being checked higher up this recursion -- break the cycle, assume compatible

        var clientProps = client.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

        foreach (var serverProp in server.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!clientProps.TryGetValue(serverProp.Name, out var clientProp))
                return false;
            if (!AreCompatible(serverProp.PropertyType, clientProp.PropertyType, visited))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Strips at most one trailing house-convention twin suffix (<c>Dto</c> or <c>Projection</c>) from the
    /// end of <paramref name="name"/> -- only from the end, never mid-name, and never both from the same
    /// side.
    /// </summary>
    private static string StripTrailingTwinSuffix(string name)
    {
        foreach (var suffix in new[] { "Dto", "Projection" })
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return name[..^suffix.Length];
        }

        return name;
    }

    /// <summary>
    /// True when <paramref name="type"/> is a collection for this rule's purposes: implements
    /// <see cref="IEnumerable"/> and is not <see cref="string"/> (a <see cref="string"/> is technically
    /// <see cref="IEnumerable{T}"/> of <see langword="char"/>, which is never the intent here).
    /// </summary>
    private static bool TryGetElementType(Type type, out Type elementType)
    {
        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var enumerableInterface = (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ? type
                : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerableInterface is not null)
            {
                elementType = enumerableInterface.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = typeof(object);
        return false;
    }
}
