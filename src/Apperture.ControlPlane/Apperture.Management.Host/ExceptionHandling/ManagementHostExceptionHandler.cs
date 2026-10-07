using System.Diagnostics;
using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.PlatformEvents;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Apperture.Management.Host.ExceptionHandling;

internal sealed class ManagementHostExceptionHandler(ILogger<ManagementHostExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId.Get(httpContext);
        var mapping = MapException(exception);

        if (mapping.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception. CorrelationId={CorrelationId}",
                correlationId);
        }
        else
        {
            logger.LogWarning(
                exception,
                "Request failed with {StatusCode}. CorrelationId={CorrelationId}",
                mapping.StatusCode,
                correlationId);
        }

        var problemDetails = new ProblemDetails
        {
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Detail = mapping.Detail,
            Type = $"https://httpstatuses.com/{mapping.StatusCode}",
            Extensions =
            {
                ["correlationId"] = correlationId,
                ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier
            }
        };
        if (mapping.Extensions is not null)
        {
            foreach (var (name, value) in mapping.Extensions)
                problemDetails.Extensions[name] = value;
        }

        httpContext.Response.StatusCode = mapping.StatusCode;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problemDetails,
            cancellationToken: cancellationToken);

        return true;
    }

    internal static ExceptionMapping MapException(Exception exception) =>
        exception switch
        {
            UnauthorizedAccessException => new ExceptionMapping(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                Detail: null),
            ArgumentException argument => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                argument.Message),
            PlatformEventValidationException validation => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                validation.Message),
            KeyNotFoundException notFound => new ExceptionMapping(
                StatusCodes.Status404NotFound,
                "Not Found",
                notFound.Message),
            // An audited operation whose commit could be neither confirmed nor ruled out (audit hardening design gate §13.2):
            // never "failed" or "unchanged", and never a prompt to retry blindly. The client reloads its state.
            AuditOutcomeUnknownException unknown => new ExceptionMapping(
                StatusCodes.Status500InternalServerError,
                "The outcome could not be confirmed.",
                "The operation may or may not have been applied. Refresh to confirm before trying again.",
                new Dictionary<string, object?>
                {
                    ["code"] = AuditOutcomeUnknownException.Code,
                    ["operationEventId"] = unknown.OperationEventId,
                }),
            // Verification confirmed a rollback, but the operation could not be completed within its bounded reruns.
            AuditOperationNotCompletedException notCompleted => new ExceptionMapping(
                StatusCodes.Status500InternalServerError,
                "The operation could not be completed.",
                "Nothing was changed.",
                new Dictionary<string, object?>
                {
                    ["code"] = "operation_not_completed",
                    ["operationEventId"] = notCompleted.OperationEventId,
                }),
            _ => new ExceptionMapping(
                StatusCodes.Status500InternalServerError,
                "An error occurred while processing your request.",
                Detail: null)
        };

    internal readonly record struct ExceptionMapping(
        int StatusCode,
        string Title,
        string? Detail,
        IReadOnlyDictionary<string, object?>? Extensions = null);
}
