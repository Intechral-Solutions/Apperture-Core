namespace Apperture.Core.Application.Services;

/// <summary>
/// Another module change holds the module lifecycle lock and did not release it within the bounded wait. Transient and safe to
/// retry: nothing was changed and nothing was recorded, because the request was never decided (it is not a refusal of the
/// transition). Carries a stable machine code so the API can answer with a retryable conflict rather than a generic failure.
/// </summary>
public sealed class ModuleLifecycleBusyException : Exception
{
    /// <summary>Stable machine code for the API and the UI.</summary>
    public const string BusyCode = "module_lifecycle_busy";

    /// <summary>The operator-facing message.</summary>
    public const string BusyMessage = "Another module change is in progress. Wait a moment and try again.";

    public ModuleLifecycleBusyException() : base(BusyMessage) { }

    public string Code => BusyCode;
}
