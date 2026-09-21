using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Client.Tests;

/// <summary>Screen-reader announcements happen once per toast, however often the host re-renders.</summary>
public sealed class ToastAnnouncerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ToastItem Toast(
        ToastSeverity severity = ToastSeverity.Success,
        string message = "Saved.",
        ToastPhase phase = ToastPhase.Visible,
        DateTimeOffset? createdAt = null) =>
        new(Guid.NewGuid(), severity, message, null, TimeSpan.FromSeconds(6), phase, ToastExit.Fade, createdAt ?? Now);

    [Fact]
    public void A_new_toast_is_announced_with_its_severity_and_text()
    {
        var announcer = new ToastAnnouncer();
        var toast = Toast(ToastSeverity.Warning, "Disk almost full.");

        var announced = announcer.Reconcile(new[] { toast }, Now);

        announced.Should().ContainSingle();
        announced[0].Id.Should().Be(toast.Id);
        announced[0].Text.Should().Be("Warning: Disk almost full.");
    }

    [Fact]
    public void Re_rendering_with_the_same_toasts_announces_nothing_more()
    {
        var announcer = new ToastAnnouncer();
        var toasts = new[] { Toast(), Toast(ToastSeverity.Info, "Two.") };

        announcer.Reconcile(toasts, Now).Should().HaveCount(2);
        announcer.Reconcile(toasts, Now).Should().BeEmpty();
        announcer.Reconcile(toasts, Now.AddSeconds(1)).Should().BeEmpty();
    }

    [Fact]
    public void Only_the_newly_arrived_toast_is_announced_when_the_list_grows()
    {
        var announcer = new ToastAnnouncer();
        var first = Toast(message: "First.");
        announcer.Reconcile(new[] { first }, Now);

        var second = Toast(message: "Second.");
        var announced = announcer.Reconcile(new[] { first, second }, Now);

        announced.Select(a => a.Id).Should().Equal(second.Id);
    }

    [Fact]
    public void Only_errors_are_assertive()
    {
        var announcer = new ToastAnnouncer();

        var announced = announcer.Reconcile(
            new[] { Toast(ToastSeverity.Error, "Failed."), Toast(ToastSeverity.Success), Toast(ToastSeverity.Info), Toast(ToastSeverity.Warning) },
            Now);

        announced.Select(a => a.Assertive).Should().Equal(true, false, false, false);
    }

    [Fact]
    public void A_toast_that_is_already_leaving_is_not_announced()
    {
        var announcer = new ToastAnnouncer();

        announcer.Reconcile(new[] { Toast(phase: ToastPhase.Exiting) }, Now).Should().BeEmpty();
    }

    [Fact]
    public void A_toast_older_than_the_announcement_window_is_treated_as_already_announced()
    {
        // e.g. the host was remounted by a layout swap; the previous host already announced it.
        var announcer = new ToastAnnouncer();
        var old = Toast(createdAt: Now - ToastPolicy.AnnouncementLifetime - TimeSpan.FromSeconds(1));

        announcer.Reconcile(new[] { old }, Now).Should().BeEmpty();
        announcer.Reconcile(new[] { old }, Now).Should().BeEmpty();
    }

    [Fact]
    public void A_toast_that_starts_leaving_is_not_announced_later_and_a_removed_toast_is_forgotten()
    {
        var announcer = new ToastAnnouncer();
        var toast = Toast();
        announcer.Reconcile(new[] { toast }, Now);

        announcer.Reconcile(new[] { toast with { Phase = ToastPhase.Exiting } }, Now).Should().BeEmpty();
        announcer.Reconcile(Array.Empty<ToastItem>(), Now).Should().BeEmpty();
        announcer.TrackedCount.Should().Be(0, "ids of removed toasts are not retained");
    }
}
