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
        const string json = """
            {
              "userId":"usr_1",
              "displayName":"Ada",
              "roles":["platform-admin"],
              "userPrincipal":"ada@contoso.com",
              "signInSource":"Local",
              "identityLinks":[{"source":"Local","displayLabel":"ada","lastAuthenticatedAtUtc":null}],
              "localCredentialStatus":"active",
              "roleNames":["Platform administrator"],
              "canChangeLocalPassword":true,
              "deferred":{"avatarManagementDeferred":true,"profileEditDeferred":true,"notificationSubscriptionsDeferred":true,"message":"Deferred."}
            }
            """;
        var me = JsonSerializer.Deserialize<MeResponse>(json, ClientJsonOptions());
        me.Should().NotBeNull();
        me!.UserId.Should().Be("usr_1");
        me.DisplayName.Should().Be("Ada");
        me.Roles.Should().Equal("platform-admin");
        me.SignInSource.Should().Be("Local");
        me.LocalCredentialStatus.Should().Be("active");
        me.CanChangeLocalPassword.Should().BeTrue();
        me.RoleNames.Should().Equal("Platform administrator");
        me.IdentityLinks.Should().ContainSingle(l => l.DisplayLabel == "ada");
        me.Deferred.Message.Should().Be("Deferred.");
        me.DisplayTimeZoneId.Should().BeNull();
    }

    [Fact]
    public void MeResponse_deserializes_displayTimeZoneId_when_present()
    {
        const string json = """
            {
              "userId":"usr_1",
              "displayName":"Ada",
              "roles":["platform-admin"],
              "userPrincipal":"ada@contoso.com",
              "signInSource":"Local",
              "identityLinks":[],
              "localCredentialStatus":"active",
              "roleNames":["Platform administrator"],
              "canChangeLocalPassword":true,
              "deferred":{"avatarManagementDeferred":true,"profileEditDeferred":true,"notificationSubscriptionsDeferred":true,"message":"Deferred."},
              "displayTimeZoneId":"America/Chicago"
            }
            """;
        var me = JsonSerializer.Deserialize<MeResponse>(json, ClientJsonOptions());
        me.Should().NotBeNull();
        me!.DisplayTimeZoneId.Should().Be("America/Chicago");
    }

    [Fact]
    public void MeResponse_deserializes_recent_account_verification()
    {
        const string json = """
            {
              "userId":"usr_1",
              "displayName":"Ada",
              "roles":["platform-admin"],
              "userPrincipal":"ada@contoso.com",
              "signInSource":"Local",
              "identityLinks":[],
              "localCredentialStatus":"active",
              "roleNames":["Platform administrator"],
              "canChangeLocalPassword":true,
              "hasRecentAccountVerification":true,
              "recentAccountVerificationExpiresAtUtc":"2026-09-04T18:45:00Z"
            }
            """;
        var me = JsonSerializer.Deserialize<MeResponse>(json, ClientJsonOptions());
        me.Should().NotBeNull();
        me!.HasRecentAccountVerification.Should().BeTrue();
        me.RecentAccountVerificationExpiresAtUtc.Should().Be(new DateTimeOffset(2026, 9, 4, 18, 45, 0, TimeSpan.Zero));
    }

    private static JsonSerializerOptions ClientJsonOptions() =>
        new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
