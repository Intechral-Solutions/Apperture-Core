using Apperture.Core.Application.Integration;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class IntegrationFleetAttentionTests
{
    [Fact]
    public void Compute_active_runnable_with_only_advisory_has_advisory_only_not_config_blocking()
    {
        var readiness = new IntegrationReadinessProjection(
            "i1",
            "Mod",
            true,
            0,
            1,
            [new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.ConnectionVerificationUnverified,
                "msg",
                null,
                null,
                null,
                IntegrationReadinessIssueSeverity.Advisory.ToString())]);

        var f = IntegrationFleetAttention.Compute("Active", readiness);
        f.HasConfigBlockingReadiness.Should().BeFalse();
        f.HasAdvisoryOnly.Should().BeTrue();
        f.HasVerificationUnverified.Should().BeTrue();
        f.IsInactiveOperational.Should().BeFalse();
    }

    [Fact]
    public void Compute_inactive_has_integration_not_active_blocking_but_config_blocking_false()
    {
        var readiness = new IntegrationReadinessProjection(
            "i1",
            "Mod",
            false,
            1,
            0,
            [new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.IntegrationNotActive,
                "inactive",
                null,
                null,
                null,
                IntegrationReadinessIssueSeverity.Blocking.ToString())]);

        var f = IntegrationFleetAttention.Compute("Inactive", readiness);
        f.IsInactiveOperational.Should().BeTrue();
        f.HasConfigBlockingReadiness.Should().BeFalse();
        readiness.BlockingIssueCount.Should().Be(1);
    }

    [Fact]
    public void Compute_active_missing_binding_is_config_blocking()
    {
        var readiness = new IntegrationReadinessProjection(
            "i1",
            "Mod",
            false,
            1,
            0,
            [new IntegrationReadinessIssue(
                IntegrationReadinessIssueCodes.MissingSlotBinding,
                "msg",
                "Db",
                null,
                null,
                IntegrationReadinessIssueSeverity.Blocking.ToString())]);

        var f = IntegrationFleetAttention.Compute("Active", readiness);
        f.HasConfigBlockingReadiness.Should().BeTrue();
        f.HasAdvisoryOnly.Should().BeFalse();
    }

    [Fact]
    public void VerificationSummary_prefers_failed_over_unverified()
    {
        var f = new IntegrationFleetAttentionFlags(
            false,
            false,
            false,
            false,
            true,
            true,
            false);
        IntegrationFleetAttention.VerificationSummary(f).Should().Be("Failed");
    }

    [Fact]
    public void VerificationSummary_ok_when_no_verification_flags()
    {
        var f = new IntegrationFleetAttentionFlags(false, false, false, false, false, false, false);
        IntegrationFleetAttention.VerificationSummary(f).Should().Be("OK");
    }

    [Fact]
    public void ToAttentionTokens_includes_expected_stable_tokens()
    {
        var f = new IntegrationFleetAttentionFlags(
            IsInactiveOperational: true,
            IsRetired: false,
            HasConfigBlockingReadiness: true,
            HasAdvisoryOnly: true,
            HasVerificationFailed: false,
            HasVerificationUnverified: true,
            HasVerificationUnavailable: false);
        var tokens = IntegrationFleetAttention.ToAttentionTokens(f);
        tokens.Should().Contain(IntegrationFleetAttention.Tokens.Inactive);
        tokens.Should().Contain(IntegrationFleetAttention.Tokens.Blocking);
        tokens.Should().Contain(IntegrationFleetAttention.Tokens.AdvisoryOnly);
        tokens.Should().Contain(IntegrationFleetAttention.Tokens.VerificationUnverified);
    }
}
