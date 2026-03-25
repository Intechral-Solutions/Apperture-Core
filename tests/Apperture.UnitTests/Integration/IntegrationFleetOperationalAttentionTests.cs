using Apperture.Core.Application.Integration;
using Apperture.Core.Domain.Workflow;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Integration;

public sealed class IntegrationFleetOperationalAttentionTests
{
    private static readonly DateTimeOffset Since = DateTimeOffset.Parse("2025-01-01T00:00:00Z");

    [Fact]
    public void BuildOperationalRunAttention_inactive_clears_evidence_and_flags()
    {
        var a = IntegrationFleetAttention.BuildOperationalRunAttention(
            "Inactive",
            Since,
            recentFailedRunCount: 99,
            mostRecentFailedRunId: "x",
            mostRecentFailedRunAt: Since,
            lastAuthorizedRunId: "x",
            lastAuthorizedRunStatus: WorkflowLifecycleStatusNames.Failed,
            lastAuthorizedRunAt: Since);

        a.RecentFailedRunCount.Should().Be(0);
        a.EvidenceWindowStartUtc.Should().BeNull();
        a.HasRecentOperationalFailure.Should().BeFalse();
        a.HasRepeatedRecentFailures.Should().BeFalse();
        a.MostRecentAuthorizedRunFailed.Should().BeFalse();
    }

    [Fact]
    public void AppendOperationalAttention_tokens_only_when_active()
    {
        var tokens = new List<string> { IntegrationFleetAttention.Tokens.Inactive };
        var ops = IntegrationFleetAttention.BuildOperationalRunAttention(
            "Active",
            Since,
            2,
            "r1",
            Since,
            "r1",
            WorkflowLifecycleStatusNames.Failed,
            Since);
        IntegrationFleetAttention.AppendOperationalAttentionTokens(tokens, "Inactive", ops);
        tokens.Should().NotContain(IntegrationFleetAttention.Tokens.OperationalFailure);

        tokens.Clear();
        tokens.Add("blocking");
        IntegrationFleetAttention.AppendOperationalAttentionTokens(tokens, "Active", ops);
        tokens.Should().Contain(IntegrationFleetAttention.Tokens.OperationalFailure);
        tokens.Should().Contain(IntegrationFleetAttention.Tokens.RepeatedOperationalFailure);
    }
}
