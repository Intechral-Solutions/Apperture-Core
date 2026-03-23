using System.Text.Json;
using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class AuthMeResponseJsonTests
{
    [Fact]
    public void MeResponse_deserializes_from_api_json_shape()
    {
        const string json = """{"userId":"usr_1","displayName":"Ada","roles":["platform-admin","operator"]}""";
        var me = JsonSerializer.Deserialize<MeResponse>(json, ClientJsonOptions());
        me.Should().NotBeNull();
        me!.UserId.Should().Be("usr_1");
        me.DisplayName.Should().Be("Ada");
        me.Roles.Should().Equal("platform-admin", "operator");
    }

    private static JsonSerializerOptions ClientJsonOptions() =>
        new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
