using Apperture.Bootstrap.Models;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Bootstrap;

/// <summary>
/// Unit tests for bootstrap recovery models: BootstrapRecoveryRequest, BootstrapRecoveryAuditRecord, BootstrapRecoveryResult.
/// </summary>
public sealed class BootstrapRecoveryModelsTests
{
    [Fact]
    public void BootstrapRecoveryAction_has_expected_values()
    {
        Enum.GetValues<BootstrapRecoveryAction>().Should().Contain(BootstrapRecoveryAction.RebindCredentialSource);
        Enum.GetValues<BootstrapRecoveryAction>().Should().Contain(BootstrapRecoveryAction.SwitchToRecoveryProfile);
        Enum.GetValues<BootstrapRecoveryAction>().Should().Contain(BootstrapRecoveryAction.ReplaceLocalCredential);
        Enum.GetValues<BootstrapRecoveryAction>().Should().Contain(BootstrapRecoveryAction.RestorePlatformAccess);
    }

    [Fact]
    public void BootstrapRecoveryRequest_with_ActionType_constructs()
    {
        var request = new BootstrapRecoveryRequest
        {
            ActionType = BootstrapRecoveryAction.RebindCredentialSource,
            Reason = "Key Vault unreachable",
            NewCredentialReference = new BootstrapCredentialReference
            {
                SourceType = BootstrapCredentialSourceType.KeyVaultReference,
                Reference = "https://vault.azure.net/secrets/new-ref"
            }
        };

        request.ActionType.Should().Be(BootstrapRecoveryAction.RebindCredentialSource);
        request.Reason.Should().Be("Key Vault unreachable");
        request.NewCredentialReference.Should().NotBeNull();
        request.NewCredentialReference!.Reference.Should().Contain("vault.azure.net");
    }

    [Fact]
    public void BootstrapRecoveryRequest_with_RecoveryProfile_constructs()
    {
        var request = new BootstrapRecoveryRequest
        {
            ActionType = BootstrapRecoveryAction.SwitchToRecoveryProfile,
            Reason = "Primary DB down",
            RecoveryProfile = new SqlConnectionBootstrapProfile
            {
                Server = "recovery-server",
                Database = "AppertureRecovery",
                AuthMode = BootstrapAuthMode.SqlPassword
            }
        };

        request.ActionType.Should().Be(BootstrapRecoveryAction.SwitchToRecoveryProfile);
        request.RecoveryProfile.Should().NotBeNull();
        request.RecoveryProfile!.Server.Should().Be("recovery-server");
        request.RecoveryProfile.Database.Should().Be("AppertureRecovery");
    }

    [Fact]
    public void BootstrapRecoveryAuditRecord_constructs()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var record = new BootstrapRecoveryAuditRecord
        {
            Timestamp = timestamp,
            RecoveryAction = BootstrapRecoveryAction.RestorePlatformAccess,
            Actor = "operator@example.com",
            Reason = "Recovery requested",
            Outcome = "Authorized",
            Details = "Stub execution"
        };

        record.Timestamp.Should().Be(timestamp);
        record.RecoveryAction.Should().Be(BootstrapRecoveryAction.RestorePlatformAccess);
        record.Actor.Should().Be("operator@example.com");
        record.Outcome.Should().Be("Authorized");
        record.Details.Should().NotContain("password").And.NotContain("secret");
    }

    [Fact]
    public void BootstrapRecoveryResult_constructs()
    {
        var success = new BootstrapRecoveryResult { Success = true, Message = "Done." };
        var failure = new BootstrapRecoveryResult { Success = false, Message = "Denied." };

        success.Success.Should().BeTrue();
        success.Message.Should().Be("Done.");
        failure.Success.Should().BeFalse();
        failure.Message.Should().Be("Denied.");
    }
}
