using Apperture.Core.Application.AdministratorViability;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// Reads the complete state the administrator-viability invariants are stated over. It reads; it does not lock. A writer that
/// decides on it takes <see cref="IPlatformAdministratorSetGate"/> first and reads the state before and after its mutation, in its
/// own transaction. A display that only describes the state may read without the gate.
/// </summary>
public interface IAdministratorViabilityService
{
    /// <summary>Reads the state now: the effective sign-in configuration, VOA and EI in both views, and Recovery Access health.</summary>
    ValueTask<AdministratorViabilityState> ReadAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAdministratorViabilityService" />
public sealed class AdministratorViabilityService : IAdministratorViabilityService
{
    private readonly IAdministratorFactsReader _facts;
    private readonly IEffectiveSignInConfigurationProvider _configuration;

    public AdministratorViabilityService(IAdministratorFactsReader facts, IEffectiveSignInConfigurationProvider configuration)
    {
        _facts = facts;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async ValueTask<AdministratorViabilityState> ReadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await _configuration.ReadAsync(cancellationToken).ConfigureAwait(false);
        var administrators = await _facts.ReadAdministratorsAsync(cancellationToken).ConfigureAwait(false);
        var recovery = await _facts.ReadRecoveryAccessAsync(cancellationToken).ConfigureAwait(false);

        return new AdministratorViabilityState(
            AdministratorViabilityEvaluator.Evaluate(administrators, configuration.Now),
            AdministratorViabilityEvaluator.Evaluate(administrators, configuration.Restart),
            recovery.IsHealthy,
            configuration.Trust,
            configuration);
    }
}
