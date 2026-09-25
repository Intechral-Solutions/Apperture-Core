namespace Apperture.Shared.Kernel;

/// <summary>
/// Marks a Management UI API request as coming from the Apperture browser client.
/// The marker is not a credential: it does not authenticate, authorize, or identify the caller.
/// A plain HTML form cannot send it. The Management UI adds it in one HTTP handler.
/// </summary>
public static class BrowserRequest
{
    public const string HeaderName = "X-Apperture-Request";

    public const string HeaderValue = "1";
}

/// <summary>
/// Declares that one endpoint is exempt from the browser request-trust rules.
/// The reason is for operators and tests. It is not a bypass switch.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class BrowserRequestTrustExemptAttribute : Attribute
{
    public BrowserRequestTrustExemptAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    public string Reason { get; }
}
