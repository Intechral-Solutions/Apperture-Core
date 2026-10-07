using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The startup canonicalization never claims the account was left unchanged unless that was established (audit hardening
/// design gate §13.2): an unknown outcome is reported as unknown.
/// </summary>
public sealed class EmergencyAccessCanonicalizationHostedServiceTests
{
    [Fact]
    public async Task An_unknown_outcome_is_logged_as_unknown_never_as_left_unchanged()
    {
        var (service, logger) = Create(new AuditOutcomeUnknownException("op-123"));

        await service.StartAsync(CancellationToken.None);

        var error = logger.Errors.Should().ContainSingle().Subject;
        error.Should().Contain("may or may not have been applied").And.Contain("op-123");
        error.Should().NotContain("left unchanged");
    }

    [Fact]
    public async Task A_failure_before_any_commit_still_reports_the_account_unchanged()
    {
        var (service, logger) = Create(new AuditOperationNotCompletedException("op-456"));

        await service.StartAsync(CancellationToken.None);

        logger.Errors.Should().ContainSingle().Which.Should().Contain("left unchanged");
    }

    [Fact]
    public async Task The_host_applies_the_identity_as_the_system_not_the_command_line()
    {
        var emergency = new ThrowingEmergency(null);
        var (service, _) = Create(emergency);

        await service.StartAsync(CancellationToken.None);

        emergency.Actor.Should().Be(AuditActor.System());
    }

    private static (EmergencyAccessCanonicalizationHostedService, CapturingLogger) Create(Exception thrown) =>
        Create(new ThrowingEmergency(thrown));

    private static (EmergencyAccessCanonicalizationHostedService, CapturingLogger) Create(ThrowingEmergency emergency)
    {
        var provider = new ServiceCollection()
            .AddScoped<IEmergencyAccessService>(_ => emergency)
            .BuildServiceProvider();
        var logger = new CapturingLogger();
        return (new EmergencyAccessCanonicalizationHostedService(provider.GetRequiredService<IServiceScopeFactory>(), logger), logger);
    }

    private sealed class CapturingLogger : ILogger<EmergencyAccessCanonicalizationHostedService>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }

    private sealed class ThrowingEmergency(Exception? thrown) : IEmergencyAccessService
    {
        public AuditActor? Actor { get; private set; }

        public ValueTask EnsureCanonicalIdentityAsync(AuditActor actor, CancellationToken cancellationToken = default)
        {
            Actor = actor;
            return thrown is null ? ValueTask.CompletedTask : ValueTask.FromException(thrown);
        }

        public ValueTask<EmergencyAccessPosture> GetPostureAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DesignateAsync(UserId userId, ActorId? actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<UserId> ProvisionAsync(string password, ActorId? actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RevokeAsync(bool confirmFallback, ActorId? actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask SetLocalPasswordAsync(UserId userId, string password, ActorId? actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> IsDesignatedEmergencyUserAsync(UserId userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask EnsureDestructiveChangeAllowedAsync(UserId userId, string operation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
