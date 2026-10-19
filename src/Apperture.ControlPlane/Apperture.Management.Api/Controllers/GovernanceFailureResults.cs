using Apperture.ControlPlane.Security;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Api.Controllers;

/// <summary>
/// The HTTP form of the typed governance outcomes (IAM-10 gate, P-37). Each type has one status and a body that carries a stable
/// <c>code</c> and a <c>message</c>. Only these types are mapped: an <see cref="InvalidOperationException"/> is never read as a
/// business outcome, so an audit programming error or an unknown failure reaches the host's exception handler (500).
/// </summary>
public static class GovernanceFailureResults
{
    /// <summary>True for the exceptions <see cref="Map"/> turns into a response.</summary>
    public static bool IsGovernanceOutcome(Exception exception) =>
        exception is AuthorizationRefusalException
            or GovernanceConflictException
            or GovernanceBusyException
            or GovernanceNotFoundException
            or GovernanceValidationException;

    /// <summary>The stable code of a governance outcome.</summary>
    public static string CodeOf(Exception exception) => exception switch
    {
        AuthorizationRefusalException refusal => refusal.Code,
        GovernanceConflictException conflict => conflict.Code,
        GovernanceBusyException => GovernanceBusyException.Code,
        GovernanceNotFoundException notFound => notFound.Code,
        GovernanceValidationException invalid => invalid.Code,
        _ => throw new ArgumentException($"{exception.GetType().Name} is not a governance outcome.", nameof(exception)),
    };

    /// <summary>
    /// The response for a governance outcome. <paramref name="legacyErrorProperty"/> also writes the code as <c>error</c>, the
    /// property the roles routes have always carried and the client still reads.
    /// </summary>
    public static ActionResult Map(Exception exception, bool legacyErrorProperty = false)
    {
        var code = CodeOf(exception);
        var message = exception.Message;
        var legacy = legacyErrorProperty;

        return exception switch
        {
            AuthorizationRefusalException refusal => new ObjectResult(
                legacy
                    ? new { error = code, code, message, missing = refusal.Missing, missingCount = refusal.MissingTotal }
                    : (object)new { code, message, missing = refusal.Missing, missingCount = refusal.MissingTotal })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            },
            RoleLifecycleException lifecycle => new ConflictObjectResult(
                legacy
                    ? new { error = code, code, message, assignmentCount = lifecycle.AssignmentCount }
                    : (object)new { code, message, assignmentCount = lifecycle.AssignmentCount }),
            GovernanceConflictException => new ConflictObjectResult(legacy ? new { error = code, code, message } : (object)new { code, message }),
            GovernanceBusyException => new ConflictObjectResult(legacy ? new { error = code, code, message, retry = true } : (object)new { code, message, retry = true }),
            GovernanceNotFoundException => new NotFoundObjectResult(legacy ? new { error = code, code, message } : (object)new { code, message }),
            GovernanceValidationException => new BadRequestObjectResult(legacy ? new { error = code, code, message } : (object)new { code, message }),
            _ => throw new ArgumentException($"{exception.GetType().Name} is not a governance outcome.", nameof(exception)),
        };
    }
}
