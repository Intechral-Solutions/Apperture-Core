using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class ExpectedRunMissingCutoverReadinessTests
{
    [Fact]
    public void Not_evaluated_when_parity_not_run()
    {
        ExpectedRunMissingCutoverReadiness.ResolveState(false, null)
            .Should().Be(ExpectedRunMissingCutoverReadinessState.NotEvaluated);
        ExpectedRunMissingCutoverReadiness.GetLabel(ExpectedRunMissingCutoverReadinessState.NotEvaluated)
            .Should().Be("Not evaluated");
    }

    [Fact]
    public void No_recent_incidents_when_evaluated_without_acceptance()
    {
        ExpectedRunMissingCutoverReadiness.ResolveState(true, null)
            .Should().Be(ExpectedRunMissingCutoverReadinessState.NoRecentIncidents);
    }

    [Fact]
    public void Blocking_parity_shows_not_ready_for_cutover()
    {
        var acceptance = SampleAcceptance(blocking: ["kind missing"], acceptable: false);
        ExpectedRunMissingCutoverReadiness.ResolveState(true, acceptance)
            .Should().Be(ExpectedRunMissingCutoverReadinessState.NotReadyForCutover);
        ExpectedRunMissingCutoverReadiness.GetLabel(ExpectedRunMissingCutoverReadinessState.NotReadyForCutover)
            .Should().Be("Not ready for cutover");
    }

    [Fact]
    public void Acceptable_parity_without_nonblocking_shows_looks_acceptable()
    {
        var acceptance = SampleAcceptance(blocking: [], acceptable: true);
        ExpectedRunMissingCutoverReadiness.ResolveState(true, acceptance)
            .Should().Be(ExpectedRunMissingCutoverReadinessState.LooksAcceptableForCutover);
        ExpectedRunMissingCutoverReadiness.GetLabel(ExpectedRunMissingCutoverReadinessState.LooksAcceptableForCutover)
            .Should().Be("Looks acceptable for cutover");
    }

    [Fact]
    public void Acceptable_with_nonblocking_shows_needs_review()
    {
        var acceptance = SampleAcceptance(blocking: [], nonBlocking: ["extra in platform"], acceptable: true);
        ExpectedRunMissingCutoverReadiness.ResolveState(true, acceptance)
            .Should().Be(ExpectedRunMissingCutoverReadinessState.NeedsReview);
    }

    private static ExpectedRunMissingParityAcceptanceSummaryDto SampleAcceptance(
        IReadOnlyList<string> blocking,
        IReadOnlyList<string>? nonBlocking = null,
        bool acceptable = false) =>
        new(
            "int-1",
            1,
            1,
            1,
            0,
            nonBlocking is { Count: > 0 } ? 1 : 0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            blocking,
            nonBlocking ?? [],
            acceptable,
            DateTimeOffset.UtcNow);
}
