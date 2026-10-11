using Apperture.Core.Application.Notifications;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Notifications;

/// <summary>
/// AUD-1.4 slice 3: creating an integration-scoped definition runs the integration-manage check once before the work and again
/// under the integration row lock, so an ownership move that committed in between is seen. The lock itself, and the commit that
/// releases it, are proven against SQL Server in <c>IntegrationRowLockSqlServerTests</c>.
/// </summary>
public sealed class NotificationDefinitionCreateLockTests
{
    private static NotificationDefinitionCreateRequest Request(string integrationId) =>
        new("Disk space alert", null, integrationId, "{}", "{}");

    [Fact]
    public async Task The_integration_manage_check_runs_before_the_work_and_again_under_the_lock()
    {
        using var h = new NotificationDefinitionTestHarness();
        var integration = await h.CreateIntegrationAsync();

        var created = await h.Definitions.CreateAsync(Request(integration.Id.Value));

        created.IntegrationId.Should().Be(integration.Id.Value);
        h.IntegrationUpdateChecks.Should().Be(2, "once at the top and once after the store took the integration row lock");
    }

    [Fact]
    public async Task A_definition_for_an_integration_that_does_not_exist_is_refused_before_anything_is_stored()
    {
        using var h = new NotificationDefinitionTestHarness();

        var act = () => h.Definitions.CreateAsync(Request("int_gone")).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*int_gone*");
        (await h.Definitions.ListAsync(new NotificationDefinitionListQuery())).Should().BeEmpty();
    }
}
