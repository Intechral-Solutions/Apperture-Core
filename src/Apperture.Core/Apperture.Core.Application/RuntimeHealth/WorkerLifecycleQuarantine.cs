namespace Apperture.Core.Application.RuntimeHealth;

/// <summary>
/// SWM-9a: Epic72B script-backed WorkerHost lifecycle is quarantined for v1.
/// Process lifecycle belongs to Aspire/AppHost, Manage.ps1, or future host adapters — not Management.
/// </summary>
public static class WorkerLifecycleQuarantine
{
    public const string Status = "Quarantined";

    public const string ErrorCode = "runtime_worker_lifecycle_quarantined";

    public const string StartDeniedMessage =
        "WorkerHost start/restart via Management is quarantined (SWM-9a). "
        + "Scripts referenced by Epic72B are missing or Aspire-unsafe. "
        + "Start or restart the worker through AppHost/Aspire (preferred) or Manage.ps1 native start. "
        + "Observe health via GET /api/control/scheduler-status (Control Center → Worker runtime). "
        + "Use POST /api/control/scheduler/run-cycle or recover-stale for schedule recovery — not process lifecycle.";

    public const string StatusMessage =
        "Epic72B file/PID worker launch status is quarantined (SWM-9a). "
        + "Use Control Center Worker runtime (scheduler heartbeat) as the supported observability surface. "
        + "WorkerHost process lifecycle is managed outside Management in v1.";

    public const string GuidancePrimaryUi =
        "WorkerHost process lifecycle is externally managed in v1. Management observes the scheduler heartbeat and offers schedule recovery — it does not start, stop, or restart WorkerHost.";

    public const string GuidanceAspire =
        "Aspire/dev: start or restart the worker resource via AppHost (dotnet run --project src/Apperture.AppHost) or the Aspire dashboard. Do not use Management runtime-worker/start.";

    public const string GuidanceScriptDev =
        "Scripted local: use ./scripts/Manage.ps1 -Area dev -Command start (or stop/restart). Prefer one stack only — do not mix with Aspire.";

    public const string GuidanceFuture =
        "Production future: Windows Service, Docker, Azure, or host adapters may own lifecycle. Direct Management process control remains deferred (SWM-11).";
}
