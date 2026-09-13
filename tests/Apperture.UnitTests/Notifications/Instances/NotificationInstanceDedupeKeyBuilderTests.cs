using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Instances;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications.Instances;

public sealed class NotificationInstanceDedupeKeyBuilderTests
{
    [Fact]
    public void Activation_uses_incident_id()
    {
        var key = NotificationInstanceDedupeKeyBuilder.TryBuild(
            "def-1",
            NotificationKinds.Activation,
            "inc-1",
            "pevt-1",
            DateTimeOffset.UtcNow,
            null);
        key.Should().Be("def-1|inc-1|activation");
    }

    [Fact]
    public void Reminder_uses_bucket_ticks()
    {
        var interval = TimeSpan.FromHours(1);
        var observed = new DateTimeOffset(2026, 5, 17, 10, 30, 0, TimeSpan.Zero);
        var key = NotificationInstanceDedupeKeyBuilder.TryBuild(
            "def-1",
            NotificationKinds.Reminder,
            "inc-1",
            null,
            observed,
            interval);
        key.Should().Be($"def-1|inc-1|reminder|{observed.Ticks / interval.Ticks}");
    }

    [Fact]
    public void Escalation_is_once_per_incident()
    {
        var key = NotificationInstanceDedupeKeyBuilder.TryBuild(
            "def-1",
            NotificationKinds.Escalation,
            "inc-1",
            "pevt-1",
            DateTimeOffset.UtcNow,
            reminderInterval: TimeSpan.FromHours(1));
        key.Should().Be("def-1|inc-1|escalation");
    }

    [Fact]
    public void Fallback_uses_platform_event_when_no_incident()
    {
        var key = NotificationInstanceDedupeKeyBuilder.TryBuild(
            "def-1",
            NotificationKinds.Activation,
            null,
            "pevt-99",
            DateTimeOffset.UtcNow,
            null);
        key.Should().Be("def-1|pevt-99|activation");
    }
}
