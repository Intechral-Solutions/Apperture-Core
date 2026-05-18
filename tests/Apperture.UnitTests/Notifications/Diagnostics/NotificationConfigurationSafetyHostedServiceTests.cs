using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Application.PlatformEvents;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Apperture.UnitTests.Notifications.Diagnostics;

public sealed class NotificationConfigurationSafetyHostedServiceTests
{
    [Fact]
    public async Task StartAsync_completes_for_safe_configuration()
    {
        var sut = new NotificationConfigurationSafetyHostedService(
            Options.Create(new PlatformEventsOptions { Enabled = true }),
            Options.Create(new NotificationEvaluationOptions { Enabled = true }),
            Options.Create(new NotificationInstanceOptions { Enabled = true }),
            Options.Create(new NotificationDeliveryOptions { Enabled = false }),
            Options.Create(new NotificationPipelineOptions { Enabled = false }),
            NullLogger<NotificationConfigurationSafetyHostedService>.Instance);

        await sut.Invoking(s => s.StartAsync(CancellationToken.None)).Should().NotThrowAsync();
    }
}
