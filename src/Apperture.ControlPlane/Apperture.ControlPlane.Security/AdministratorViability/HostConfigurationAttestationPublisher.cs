using Apperture.Core.Application.AdministratorViability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Apperture.ControlPlane.Security.AdministratorViability;

/// <summary>
/// Writes the Management Host's attestation: its row at start, a refresh about every minute, and a stopped mark on a graceful
/// stop. The emergency command line, which cannot observe the host, reads the row.
/// </summary>
/// <remarks>
/// <para><b>It evidences and decides nothing.</b> It writes no configuration anywhere but its own row, takes no gate and no
/// administrator lock, and changes nothing the host does. A failure to write is logged and retried on the next tick; while the
/// row is not fresh the evidence is simply unavailable, which fails closed in the reader.</para>
/// <para><b>It never builds the Entra or OpenID Connect options</b> (see <see cref="HostAttestationPayloadSource"/>), and it starts
/// after the migrations, the secret-store validation and the governance startup steps, with no ordering of its own to rely on.</para>
/// <para><b>Ownership.</b> A newer process of the same host replaces the row at its start. An older process that is still running
/// then finds itself superseded: it writes nothing to the row, and its stop never marks the newer process's row stopped. A
/// superseded process is not retired for good: on each tick it asks the store to give the row back, and the store (the database,
/// not this process) agrees only when the holder has stopped or has gone silent for longer than the freshness window, or the row is
/// gone. While the holder is live the answer is no and nothing changes. The same rule governs every later attempt to take the row,
/// including the retry of a start whose first write failed; only that first write at start is unconditional.</para>
/// </remarks>
public sealed class HostConfigurationAttestationPublisher : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly HostAttestationPayloadSource _source;
    private readonly HostAttestationIdentity _identity;
    private readonly TimeProvider _time;
    private readonly ILogger<HostConfigurationAttestationPublisher> _logger;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _publishing = new(1, 1);
    private CancellationTokenSource? _stop;
    private Task _loop = Task.CompletedTask;
    private bool _claimAttempted;
    private bool _claimed;
    private volatile bool _superseded;
    private volatile bool _stopped;

    public HostConfigurationAttestationPublisher(
        IServiceScopeFactory scopes,
        HostAttestationPayloadSource source,
        HostAttestationIdentity identity,
        TimeProvider time,
        ILogger<HostConfigurationAttestationPublisher> logger,
        TimeSpan? refreshInterval = null)
    {
        _scopes = scopes;
        _source = source;
        _identity = identity;
        _time = time;
        _logger = logger;
        _interval = refreshInterval ?? HostConfigurationAttestationTiming.RefreshInterval;
    }

    /// <summary>
    /// True while another process of this host holds the row: this process writes nothing to it, and asks for it back on each tick
    /// until the store gives it back. False again once it owns the row.
    /// </summary>
    public bool Superseded => _superseded;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stop = new CancellationTokenSource();
        await PublishOnceAsync(cancellationToken).ConfigureAwait(false);

        // The timer starts counting here, not when the loop's task first runs, so the first refresh is one interval after the claim.
        var timer = new PeriodicTimer(_interval, _time);
        _loop = Task.Run(() => RunAsync(timer, _stop.Token), CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopped = true;
        if (_stop is not null)
            await _stop.CancelAsync().ConfigureAwait(false);

        try
        {
            await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host is no longer waiting for this service.
        }

        if (_superseded || !_claimed)
            return;

        // The stopped mark closes the window at once: a graceful stop is not a crash. It is bounded, and a failure to write it only
        // leaves the window to run out.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var scope = _scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IHostConfigurationAttestationStore>();
            await store.MarkStoppedAsync(_identity.HostId, _identity.ProcessInstanceId, bounded.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The Management Host could not record that it stopped; its sign-in record will age out on its own.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("The Management Host did not record that it stopped in time; its sign-in record will age out on its own.");
        }
    }

    /// <summary>
    /// Writes the host's row now. The first time it claims the row, replacing the process it succeeds. After that it refreshes the row
    /// it owns; when it does not own the row (it was replaced, the first write failed, or the row is gone) it asks the store to give
    /// the row back, which the store does only when no live holder stands in the way. A failure is logged and never thrown, so the
    /// host starts and runs whatever the state of the evidence.
    /// </summary>
    /// <returns>True when the row was written.</returns>
    public async Task<bool> PublishOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped)
            return false;

        await _publishing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopped)
                return false;

            var payload = _source.Build();
            var write = new HostConfigurationAttestationWrite(
                _identity.HostId,
                _identity.ProcessInstanceId,
                _identity.ProcessStartedUtc,
                AttestedAuthenticationPayload.SchemaVersion,
                payload.Serialize());

            using var scope = _scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IHostConfigurationAttestationStore>();

            if (_superseded)
                return await ReclaimAsync(store, write, cancellationToken).ConfigureAwait(false);

            if (_claimed)
            {
                switch (await store.RefreshAsync(write, cancellationToken).ConfigureAwait(false))
                {
                    case HostAttestationRefreshOutcome.Refreshed:
                        return true;
                    case HostAttestationRefreshOutcome.Superseded:
                        _superseded = true;
                        _logger.LogWarning("Another Management Host process has taken over this host's sign-in record; this process stops recording it until that process stops or goes quiet.");
                        // The holder may already have stopped: ask now rather than a tick later. The store decides.
                        return await ReclaimAsync(store, write, cancellationToken).ConfigureAwait(false);
                }
            }

            if (_claimAttempted)
                return await ReclaimAsync(store, write, cancellationToken).ConfigureAwait(false);

            // Only the first write of a process is unconditional: a process that starts replaces the one it succeeds. It is marked
            // attempted before the write, so a start whose write failed (or committed and was not acknowledged) never claims
            // unconditionally again and cannot take the row back from a newer process that has since claimed it.
            _claimAttempted = true;
            await store.ClaimAsync(write, cancellationToken).ConfigureAwait(false);
            _claimed = true;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The Management Host could not record its sign-in configuration; it will try again.");
            return false;
        }
        finally
        {
            _publishing.Release();
        }
    }

    private async Task<bool> ReclaimAsync(IHostConfigurationAttestationStore store, HostConfigurationAttestationWrite write, CancellationToken cancellationToken)
    {
        if (await store.ReclaimAsync(write, cancellationToken).ConfigureAwait(false) == HostAttestationReclaimOutcome.Reclaimed)
        {
            var wasSuperseded = _superseded;
            _claimed = true;
            _superseded = false;
            if (wasSuperseded)
                _logger.LogInformation("The Management Host's sign-in record is this process's again: the process that held it has stopped or gone quiet.");
            return true;
        }

        if (!_superseded)
        {
            _superseded = true;
            _logger.LogWarning("Another Management Host process holds this host's sign-in record; this process does not record it until that process stops or goes quiet.");
        }

        return false;
    }

    private async Task RunAsync(PeriodicTimer periodic, CancellationToken cancellationToken)
    {
        using var timer = periodic;
        try
        {
            // The loop outlives being superseded: while another process holds the row each tick is one conditional statement that
            // changes nothing until the holder stops or goes quiet. There is no other polling.
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await PublishOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    public void Dispose()
    {
        _stop?.Dispose();
        _publishing.Dispose();
    }
}

/// <summary>Registration of the Management Host's attestation.</summary>
public static class HostConfigurationAttestationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the attestation publisher as a hosted service. Call it after the registrations of every governance startup step and of
    /// authentication: hosted services start in registration order, and the attestation must start after the migrations, the
    /// secret-store validation and the governance startup steps. Only the Management Host calls it.
    /// </summary>
    public static IServiceCollection AddHostConfigurationAttestation(this IServiceCollection services)
    {
        services.AddSingleton(sp => HostAttestationIdentity.Create(
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddSingleton<HostAttestationPayloadSource>();
        services.TryAddSingleton(TimeProvider.System);
        // Registered by type like every other startup step, so the host's start order can be observed and pinned.
        services.AddHostedService<HostConfigurationAttestationPublisher>();
        return services;
    }
}
