using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests;

/// <summary>
/// AUD-1.4 slice 4 remediation of review finding M1, client half: the schedule edit form sends only the fields the operator
/// changed, so a value loaded when the form opened (the enabled flag above all) is never sent back over another operator's
/// change. Source-text assertions, this suite's convention (there is no bUnit).
/// </summary>
public sealed class SchedulePatchUiSourceTests
{
    [Fact]
    public void The_schedule_edit_form_sends_null_for_every_field_the_operator_did_not_change()
    {
        var save = SaveEditBody();

        save.Should().Contain("name == _detail.Name ? null : name");
        save.Should().Contain("recurrence == _detail.RecurrenceExpression ? null : recurrence");
        save.Should().Contain("timeZone == _detail.TimeZone ? null : timeZone");
        save.Should().Contain("_editEnabled == _detail.Enabled ? null : _editEnabled",
            "the loaded enabled flag must not be sent back unless the operator changed it");
    }

    private static string SaveEditBody()
    {
        var content = ReadRepoFile("src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Schedules/ScheduleDetail.razor");
        var start = content.IndexOf("private async Task SaveEditAsync()", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var end = content.IndexOf("protected override async Task OnParametersSetAsync()", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return content[start..end];
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the tests run inside the repository");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
