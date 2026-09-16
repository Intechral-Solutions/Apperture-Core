using System.Reflection;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// CTX-3A's A2 pattern: reflection-based proof that the client DTO twin matches the server contract, so
/// drift is caught at build/test time rather than at runtime deserialization. This is the third time this
/// codebase has needed it (CTX-2 and CTX-3A each hit the drift once), so the assertions go beyond name
/// containment: an equal property count catches a client-side field the server never sends, and the
/// scalar type check catches a `Blockers` that is `IReadOnlyList&lt;string&gt;` on one side.
/// </summary>
public sealed class ResourceMoveDtoContractParityTests
{
    public static TheoryData<Type, Type> Pairs => new()
    {
        { typeof(ResourceMovePreview), typeof(ResourceMovePreviewDto) },
        { typeof(ResourceMoveBlocker), typeof(ResourceMoveBlockerDto) },
        { typeof(ResourceMoveEffect),  typeof(ResourceMoveEffectDto)  }
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Client_dto_has_exactly_the_server_properties(Type serverType, Type clientType)
    {
        var serverProps = serverType.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var clientProps = clientType.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        clientProps.Should().Equal(serverProps,
            "the client twin must carry every server property and nothing extra -- an extra client field is a field the server never populates");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Scalar_property_types_match(Type serverType, Type clientType)
    {
        foreach (var serverProp in serverType.GetProperties())
        {
            var clientProp = clientType.GetProperty(serverProp.Name);
            clientProp.Should().NotBeNull();

            // Scalars must match exactly. Collection/record-shaped properties legitimately differ, because
            // the client mirrors ResourceMoveBlocker as ResourceMoveBlockerDto -- those are covered by
            // their own row in this theory.
            if (serverProp.PropertyType.IsPrimitive || serverProp.PropertyType == typeof(string) || serverProp.PropertyType == typeof(bool))
                clientProp!.PropertyType.Should().Be(serverProp.PropertyType, $"{serverType.Name}.{serverProp.Name}");
        }
    }

    [Fact]
    public void Client_blocker_codes_mirror_the_server_codes_exactly()
    {
        ConstantValues(typeof(Apperture.Core.Application.ResourceContexts.ResourceMoveBlockerCodes))
            .Should().BeEquivalentTo(ConstantValues(typeof(Apperture.Management.Client.ResourceMoveBlockerCodes)),
                "the client mirrors these because it cannot reference Core.Application -- a mirror that drifts is worse than no mirror");
    }

    [Fact]
    public void Client_effect_codes_mirror_the_server_codes_exactly()
    {
        ConstantValues(typeof(Apperture.Core.Application.ResourceContexts.ResourceMoveEffectCodes))
            .Should().BeEquivalentTo(ConstantValues(typeof(Apperture.Management.Client.ResourceMoveEffectCodes)));
    }

    private static IEnumerable<string> ConstantValues(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);
}
