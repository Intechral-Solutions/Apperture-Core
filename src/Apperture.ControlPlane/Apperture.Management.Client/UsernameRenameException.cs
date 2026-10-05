using System.Net;

namespace Apperture.Management.Client;

/// <summary>
/// A username rename the server refused. <see cref="HttpRequestException.StatusCode"/> is the HTTP status,
/// <see cref="Code"/> the server's stable error code when it sent one (for example <c>username_taken</c>,
/// <c>username_invalid</c>, <c>username_reserved</c>, <c>UnsafeOperation</c>), and the message is safe to show.
/// </summary>
public sealed class UsernameRenameException : HttpRequestException
{
    public UsernameRenameException(string message, HttpStatusCode statusCode, string? code)
        : base(message, null, statusCode)
    {
        Code = code;
    }

    public string? Code { get; }
}
