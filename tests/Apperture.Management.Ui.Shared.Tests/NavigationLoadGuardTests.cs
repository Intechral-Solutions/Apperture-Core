using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

public sealed class NavigationLoadGuardTests
{
    [Fact]
    public void Begin_increments_generation()
    {
        var guard = new NavigationLoadGuard();
        var first = guard.Begin();
        var second = guard.Begin();
        second.Should().Be(first + 1);
    }

    [Fact]
    public void IsCurrent_returns_false_for_superseded_generation()
    {
        var guard = new NavigationLoadGuard();
        var stale = guard.Begin();
        guard.Begin();
        guard.IsCurrent(stale).Should().BeFalse();
        guard.IsCurrent(Volatile.Read(ref stale)).Should().BeFalse();
    }

    [Fact]
    public void IsCurrent_returns_true_for_latest_generation()
    {
        var guard = new NavigationLoadGuard();
        var latest = guard.Begin();
        guard.IsCurrent(latest).Should().BeTrue();
    }
}
