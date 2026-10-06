using System.Net;

namespace Apperture.Management.Client;

/// <summary>
/// A username rename the server answered with an error. <see cref="HttpRequestException.StatusCode"/> is the HTTP status,
/// <see cref="Code"/> the server's stable error code when it sent one (for example <c>username_taken</c>,
/// <c>username_invalid</c>, <c>username_reserved</c>, <c>UnsafeOperation</c>), and the message is safe to show.
/// <para>
/// Not every error means the rename did not happen. A 4xx is the API refusing the request, so nothing changed
/// (<see cref="OutcomeUnknown"/> is false). A 5xx, or a 408, can arrive after the server committed the change (the audit
/// row is written after the rename commits, and a gateway can fail after the API answered), so the outcome is unknown
/// and the message says so instead of claiming a failure.
/// </para>
/// </summary>
public sealed class UsernameRenameException : HttpRequestException
{
    /// <summary>What to tell the operator whenever the result of a rename is not known: never "failed" or "not changed".</summary>
    public const string ConfirmByRefreshing =
        "The username may or may not have changed. Refresh this page to confirm before trying again.";

    public UsernameRenameException(string message, HttpStatusCode statusCode, string? code)
        : base(message, null, statusCode)
    {
        Code = code;
    }

    public string? Code { get; }

    /// <summary>True when the status does not prove the rename was refused: a server error or a request timeout.</summary>
    public bool OutcomeUnknown => IsOutcomeUnknown(StatusCode);

    public static bool IsOutcomeUnknown(HttpStatusCode? status) =>
        status is { } s && ((int)s >= 500 || s == HttpStatusCode.RequestTimeout);
}
