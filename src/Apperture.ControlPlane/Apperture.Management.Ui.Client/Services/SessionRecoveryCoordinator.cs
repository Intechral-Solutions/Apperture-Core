using Apperture.Management.Ui.Shared.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Apperture.Management.Ui.Client.Services;

/// <summary>Where the per-tab recovery state machine is.</summary>
public enum SessionRecoveryPhase
{
    /// <summary>Nothing in progress. A 401 from a protected call starts a check.</summary>
    Normal,

    /// <summary>One <c>auth/me</c> probe is in flight; every further 401 awaits the same decision.</summary>
    Confirming,

    /// <summary>Loss confirmed and the one full-page navigation to sign-in issued. Everything else is ignored.</summary>
    Recovering,

    /// <summary>Loss confirmed but the loop breaker is open: no automatic navigation, the operator chooses to sign in.</summary>
    ManualSignInRequired,
}

/// <summary>What the HTTP pipeline may tell the recovery coordinator.</summary>
public interface ISessionRecoveryCoordinator
{
    /// <summary>
    /// A protected API call was answered 401. Completes when the (single, shared) recovery decision is made.
    /// Never throws and never replays anything.
    /// </summary>
    Task ReportUnauthenticatedAsync();
}

/// <summary>
/// Per-tab session-recovery state machine (SEC-1 design gate §12). Scoped: one instance per browser document, so
/// a full-page navigation resets it, which is the point of recovering that way.
/// <para>
/// A 401 from a protected call is only a <em>suspicion</em>. This coordinator confirms it with one authoritative
/// <c>auth/me</c> probe, single-flight across every concurrent reporter, then branches on positive evidence:
/// 401 → mark the session expired and make ONE full-page navigation to sign-in; 403 → terminal account-unavailable
/// state; 200 → the 401 was endpoint-specific, nothing to do; network failure / 5xx / timeout → service
/// unavailable, never sign-in.
/// </para>
/// <para>
/// It never holds, retries or replays a request, never acts on a public route or in a tab that does not believe
/// it is signed in, and stops navigating automatically after <see cref="MaxAutomaticRecoveries"/> recoveries within
/// <see cref="RecoveryWindow"/> (the loop breaker), offering an explicit sign-in instead.
/// </para>
/// </summary>
public sealed class SessionRecoveryCoordinator : ISessionRecoveryCoordinator
{
    public const int MaxAutomaticRecoveries = 3;
    public static readonly TimeSpan RecoveryWindow = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private readonly SessionStateService _session;
    private readonly ISessionProbe _probe;
    private readonly IRecoveryHistoryStore _history;
    private readonly NavigationManager _nav;
    private readonly ILogger<SessionRecoveryCoordinator> _logger;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private SessionRecoveryPhase _phase = SessionRecoveryPhase.Normal;
    private Task? _inflight;
    private string? _loginUrl;

    public SessionRecoveryCoordinator(
        SessionStateService session,
        ISessionProbe probe,
        IRecoveryHistoryStore history,
        NavigationManager nav,
        ILogger<SessionRecoveryCoordinator> logger,
        TimeProvider? timeProvider = null)
    {
        _session = session;
        _probe = probe;
        _history = history;
        _nav = nav;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;

        // A soft session refresh in a signed-in tab is answered 401 by auth/me, which the HTTP pipeline excludes.
        // The session forwards that suspicion here so there is still exactly one recovery decision. (The coordinator
        // is scoped per document and lives as long as the session state does.)
        _session.UnauthorizedDuringRefresh += OnUnauthorizedDuringRefresh;
    }

    private void OnUnauthorizedDuringRefresh() => _ = ReportUnauthenticatedAsync();

    public SessionRecoveryPhase Phase
    {
        get
        {
            lock (_gate)
                return _phase;
        }
    }

    /// <summary>The sign-in URL captured when loss was confirmed (return target + <c>reason=expired</c>), or null before that.</summary>
    public string? LoginUrl
    {
        get
        {
            lock (_gate)
                return _loginUrl;
        }
    }

    public Task ReportUnauthenticatedAsync()
    {
        TaskCompletionSource tcs;
        lock (_gate)
        {
            if (_inflight is not null)
                return _inflight;

            if (_phase != SessionRecoveryPhase.Normal)
                return Task.CompletedTask;

            // Recovery only means something for a tab that believes it is signed in. This also keeps boot-time
            // noise (calls racing the initial auth check) and the sign-in surfaces from ever probing.
            if (_session.State != SessionLoadState.Authenticated || IsPublicRoute())
                return Task.CompletedTask;

            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _phase = SessionRecoveryPhase.Confirming;
            _inflight = tcs.Task;
        }

        _ = RunAsync(tcs);
        return tcs.Task;
    }

    /// <summary>
    /// The operator's explicit "Sign in" (shown when the loop breaker is open, or as the recovery surface's action).
    /// A user gesture, so it navigates regardless of the breaker.
    /// </summary>
    public void SignInNow()
    {
        var url = LoginUrl ?? LoginRouteHelper.BuildLoginUrl(CurrentPathAndQuery(), LoginRouteHelper.ExpiredReason);
        _nav.NavigateTo(url, forceLoad: true);
    }

    private async Task RunAsync(TaskCompletionSource tcs)
    {
        try
        {
            var result = await ProbeAsync();
            await ApplyAsync(result);
        }
        catch (Exception ex)
        {
            // Recovery bookkeeping must never fail a caller; an unknown failure is "unknown", not "signed out".
            _logger.LogWarning(ex, "Session recovery failed unexpectedly; leaving the session state unchanged.");
        }
        finally
        {
            lock (_gate)
            {
                _inflight = null;
                if (_phase == SessionRecoveryPhase.Confirming)
                    _phase = SessionRecoveryPhase.Normal;
            }

            tcs.TrySetResult();
        }
    }

    private async Task<SessionProbeResult> ProbeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(ProbeTimeout, _time);
            return await _probe.ProbeAsync(timeout.Token);
        }
        catch (Exception)
        {
            return SessionProbeResult.ServiceUnavailable;
        }
    }

    private async Task ApplyAsync(SessionProbeResult result)
    {
        switch (result)
        {
            case SessionProbeResult.Authenticated:
                return;

            case SessionProbeResult.AccountUnavailable:
                if (_session.State == SessionLoadState.Authenticated)
                    _session.MarkAccountUnavailable();
                return;

            case SessionProbeResult.ServiceUnavailable:
                if (_session.State == SessionLoadState.Authenticated)
                    _session.MarkServiceUnavailable();
                return;

            case SessionProbeResult.Unauthenticated:
                await ConfirmLossAsync();
                return;
        }
    }

    private async Task ConfirmLossAsync()
    {
        var now = _time.GetUtcNow();
        var recent = await LoadRecentAsync(now);
        var breakerOpen = recent.Count >= MaxAutomaticRecoveries;
        if (!breakerOpen)
        {
            recent.Add(now);
            await SaveAsync(recent);
        }

        // ---- The recovery edge: everything below is synchronous. ----
        // The world may have moved while awaiting (the operator reached /login, or another path already changed
        // the session); recovery must not act on stale premises.
        if (_session.State != SessionLoadState.Authenticated || IsPublicRoute())
            return;

        var loginUrl = LoginRouteHelper.BuildLoginUrl(CurrentPathAndQuery(), LoginRouteHelper.ExpiredReason);
        lock (_gate)
        {
            _loginUrl = loginUrl;
            _phase = breakerOpen ? SessionRecoveryPhase.ManualSignInRequired : SessionRecoveryPhase.Recovering;
        }

        // Stops the Toast watcher (it only polls while Authenticated), hides the protected shell and clears identity.
        // A throwing subscriber must not keep the operator from being taken to sign-in.
        try
        {
            _session.MarkSessionExpired();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A session-state subscriber failed while marking the session expired.");
        }

        if (breakerOpen)
        {
            _logger.LogWarning(
                "Session recovery loop breaker open: {Count} recoveries within {Window}s. Automatic sign-in navigation suppressed; manual sign-in required.",
                recent.Count,
                (int)RecoveryWindow.TotalSeconds);
            return;
        }

        _nav.NavigateTo(loginUrl, forceLoad: true);
    }

    private async Task<List<DateTimeOffset>> LoadRecentAsync(DateTimeOffset now)
    {
        try
        {
            var stamps = await _history.LoadAsync();
            // A stamp dated in the future means the clock moved backwards since it was written: it says nothing
            // reliable about "the last 60 seconds", so it does not count.
            return stamps.Where(s => s <= now && now - s < RecoveryWindow).ToList();
        }
        catch (Exception)
        {
            return new List<DateTimeOffset>();
        }
    }

    private async Task SaveAsync(List<DateTimeOffset> stamps)
    {
        try
        {
            await _history.SaveAsync(stamps);
        }
        catch (Exception)
        {
            // Best-effort: never block a recovery on storage.
        }
    }

    private bool IsPublicRoute() =>
        LoginRouteHelper.IsPublicPath(new Uri(_nav.Uri).AbsolutePath);

    private string CurrentPathAndQuery()
    {
        var uri = new Uri(_nav.Uri);
        return uri.AbsolutePath + uri.Query;
    }
}
