using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;

namespace Apperture.UnitTests.AdministratorViability;

/// <summary>A gate that counts how often it was taken and can run something while it is held, so a test sees the order of use.</summary>
internal sealed class RecordingGate : IPlatformAdministratorSetGate
{
    public int Acquired { get; private set; }

    public Exception? Fail { get; set; }

    public ValueTask AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (Fail is not null)
            throw Fail;
        Acquired++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Viability states a test scripts: each read returns the next, and the last one repeats.</summary>
internal sealed class ScriptedViabilityReads : IAdministratorViabilityService
{
    private readonly Queue<AdministratorViabilityState> _states;
    private AdministratorViabilityState _last;

    public ScriptedViabilityReads(params AdministratorViabilityState[] states)
    {
        _states = new Queue<AdministratorViabilityState>(states);
        _last = states.Length > 0 ? states[^1] : ViabilityTestData.Same([]);
    }

    public int Reads { get; private set; }

    public ValueTask<AdministratorViabilityState> ReadAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        if (_states.Count > 0)
            _last = _states.Dequeue();
        return ValueTask.FromResult(_last);
    }

    public ValueTask<AdministratorViabilityState> ReadForAttemptAsync(AttemptConfigurationReading attempt, CancellationToken cancellationToken = default) =>
        ReadAsync(cancellationToken);
}

/// <summary>A fixed sign-in configuration: Local sign-in on, Entra off (so only Local credentials make anyone viable).</summary>
internal sealed class FixedSignInConfiguration : IEffectiveSignInConfigurationProvider
{
    public ValueTask<EffectiveSignInConfiguration> ReadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EffectiveSignInConfiguration(
            ViabilityTestData.Availability(ViabilityView.Now, local: true, entra: false, tenant: null),
            ViabilityTestData.Availability(ViabilityView.Restart, local: true, entra: false, tenant: null),
            ConfigurationTrust.Full));

    public ValueTask<EffectiveSignInConfiguration> ReadForAttemptAsync(AttemptConfigurationReading attempt, CancellationToken cancellationToken = default) =>
        ReadAsync(cancellationToken);
}

internal static class FakeViability
{
    /// <summary>The real coordinator over a recording gate and scripted reads: the guard's logic runs; only the states are scripted.</summary>
    public static AdministratorViabilityCoordinator Scripted(RecordingGate gate, params AdministratorViabilityState[] states) =>
        new(gate, new ScriptedViabilityReads(states));

    /// <summary>The real coordinator and the real viability service over facts the test supplies.</summary>
    public static AdministratorViabilityCoordinator OverFacts(RecordingGate gate, IAdministratorFactsReader facts) =>
        new(gate, new AdministratorViabilityService(facts, new FixedSignInConfiguration()));

    /// <summary>A coordinator for a test that does not care: nobody is viable before or after, so nothing is ever refused.</summary>
    public static AdministratorViabilityCoordinator Inert() => Scripted(new RecordingGate());
}
