using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using FluentAssertions;
using Xunit;
using Client = Apperture.Management.Client;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// IAM-9.3 wire contract: the server DTOs expose a <c>username</c>, and the Management client's copies read the
/// same JSON the server writes. Property names are the contract, so these tests go through real serialization.
/// </summary>
public sealed class UserReferenceContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, Web);

    [Fact]
    public void User_reference_is_exactly_user_id_username_and_display_name()
    {
        var json = Json(new UserReferenceDto("aaaaaaaaaaaaaaaaaaaaaaa1", "ada", "Ada Lovelace"));

        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("userId", "username", "displayName");
        json.GetProperty("username").GetString().Should().Be("ada");
    }

    [Fact]
    public void Server_user_reference_round_trips_into_the_client_copy()
    {
        var json = Json(new UserReferenceDto("aaaaaaaaaaaaaaaaaaaaaaa1", "ada", "Ada Lovelace")).GetRawText();

        var client = JsonSerializer.Deserialize<Client.UserReferenceDto>(json, Web)!;

        client.Should().Be(new Client.UserReferenceDto("aaaaaaaaaaaaaaaaaaaaaaa1", "ada", "Ada Lovelace"));
    }

    [Fact]
    public void User_list_item_carries_username_and_the_client_reads_it()
    {
        var json = Json(new UserListDto("u1", "ada", "Ada Lovelace", true, false, ["r1"])).GetRawText();

        JsonDocument.Parse(json).RootElement.GetProperty("username").GetString().Should().Be("ada");
        var client = JsonSerializer.Deserialize<Client.UserListDto>(json, Web)!;
        client.Username.Should().Be("ada");
        client.UserId.Should().Be("u1");
    }

    [Fact]
    public void User_detail_carries_username_and_the_client_reads_it()
    {
        var detail = new UserDetailDto("u1", "ada", "Ada Lovelace", [], [], null, null);
        var json = Json(detail).GetRawText();

        JsonDocument.Parse(json).RootElement.GetProperty("username").GetString().Should().Be("ada");
        JsonSerializer.Deserialize<Client.UserDetailDto>(json, Web)!.Username.Should().Be("ada");
    }

    [Fact]
    public void Role_holder_carries_username_and_the_client_reads_it()
    {
        var json = Json(new RoleAssignmentHolderDto("u1", "ada", "Ada Lovelace")).GetRawText();

        JsonDocument.Parse(json).RootElement.GetProperty("username").GetString().Should().Be("ada");
        JsonSerializer.Deserialize<Client.RoleAssignmentHolderDto>(json, Web)!.Username.Should().Be("ada");
    }

    [Fact]
    public void Me_response_carries_username_and_the_client_reads_it()
    {
        var me = new MeResponse("u1", "ada", "Ada Lovelace", [], null, "Local", [], "Set", [], false, null);
        var json = Json(me).GetRawText();

        JsonDocument.Parse(json).RootElement.GetProperty("username").GetString().Should().Be("ada");
        JsonSerializer.Deserialize<Client.MeResponse>(json, Web)!.Username.Should().Be("ada");
    }

    [Fact]
    public void Create_user_request_accepts_username_and_keeps_external_id_as_an_optional_alias()
    {
        var username = JsonSerializer.Deserialize<CreateUserRequest>(
            """{"displayName":"Ada","identitySource":"Local","username":"ada","password":"x"}""", Web)!;
        username.Username.Should().Be("ada");
        username.ExternalId.Should().BeNull();

        var alias = JsonSerializer.Deserialize<CreateUserRequest>(
            """{"displayName":"Ada","identitySource":"Local","externalId":"ada","password":"x"}""", Web)!;
        alias.ExternalId.Should().Be("ada");
        alias.Username.Should().BeNull();
    }

    [Fact]
    public void Add_identity_link_request_allows_an_omitted_external_id()
    {
        var request = JsonSerializer.Deserialize<AddIdentityLinkRequest>("""{"identitySource":"Local"}""", Web)!;

        request.ExternalId.Should().BeNull();
    }
}
