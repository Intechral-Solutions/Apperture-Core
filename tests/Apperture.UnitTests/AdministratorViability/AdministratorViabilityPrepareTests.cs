using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using FluentAssertions;
using Xunit;
using static Apperture.UnitTests.AdministratorViability.ViabilityTestData;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>
/// IAM-10.2b WP3: what a registration writer relies on from the coordinator. It builds what the viability reads are made from before
/// its unit opens (R-1), it can see the state it captured and the state its change left, and a registration refusal names what to do.
/// </summary>
public sealed class AdministratorViabilityPrepareTests
{
    private sealed class CountingDeployment : IDeploymentAuthenticationConfiguration
    {
        public int Initializations { get; private set; }

        public int Reads { get; private set; }

        public DeploymentAuthenticationReading Read()
        {
            Reads++;
            throw new InvalidOperationException("The reading is not part of preparing.");
        }

        public void EnsureInitialized() => Initializations++;
    }

    [Fact]
    public async Task Preparing_builds_the_deployment_once_and_takes_neither_the_gate_nor_a_reading()
    {
        var gate = new RecordingGate();
        var deployment = new CountingDeployment();
        var coordinator = new AdministratorViabilityCoordinator(gate, new ScriptedViabilityReads(), deployment);

        await coordinator.PrepareAsync();

        deployment.Initializations.Should().Be(1);
        deployment.Reads.Should().Be(0);
        gate.Acquired.Should().Be(0, "preparing happens before the unit, where no transaction exists to own the gate");
    }

    [Fact]
    public async Task Preparing_with_no_deployment_to_build_is_a_no_op()
    {
        var coordinator = new AdministratorViabilityCoordinator(new RecordingGate(), new ScriptedViabilityReads());

        var prepare = async () => await coordinator.PrepareAsync();

        await prepare.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Preparing_honours_cancellation_and_a_failure_to_build_is_never_turned_into_a_reading()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var coordinator = new AdministratorViabilityCoordinator(new RecordingGate(), new ScriptedViabilityReads(), new CountingDeployment());
        var failing = new AdministratorViabilityCoordinator(new RecordingGate(), new ScriptedViabilityReads(), new FailingDeployment());

        var cancel = async () => await coordinator.PrepareAsync(cancelled.Token);
        var fail = async () => await failing.PrepareAsync();

        await cancel.Should().ThrowAsync<OperationCanceledException>();
        await fail.Should().ThrowAsync<TimeoutException>("a build that failed is not swallowed into an optimistic reading");
    }

    private sealed class FailingDeployment : IDeploymentAuthenticationConfiguration
    {
        public DeploymentAuthenticationReading Read() => throw new TimeoutException();

        public void EnsureInitialized() => throw new TimeoutException();
    }

    [Fact]
    public async Task The_scope_exposes_the_state_it_captured_and_the_state_the_change_left()
    {
        var before = Same([ "a" ]);
        var after = Same([]);
        var coordinator = FakeViability.Scripted(new RecordingGate(), before, after);
        var scope = await coordinator.EnterAsync();

        scope.Captured.Should().BeNull();
        scope.After.Should().BeNull();
        await scope.CaptureBeforeAsync();
        var verdict = await scope.VerifyAfterAsync();

        scope.Captured.Should().BeSameAs(before);
        scope.After.Should().BeSameAs(after);
        verdict.Kind.Should().Be(ViabilityVerdictKind.LastViableAdministrator);
    }

    [Fact]
    public void The_management_host_registers_one_record_of_how_the_running_options_were_built()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddAppertureGovernance();

        services.Where(d => d.ServiceType == typeof(IEntraOptionsBuildHealth)).Should().ContainSingle()
            .Which.Should().Match<Microsoft.Extensions.DependencyInjection.ServiceDescriptor>(
                d => d.ImplementationType == typeof(EntraOptionsBuildHealth) && d.Lifetime == Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);
    }
}
