using System.Diagnostics;
using System.Text.Json;
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
            _ => new ExceptionMapping(
                StatusCodes.Status500InternalServerError,
                "An error occurred while processing your request.",
                Detail: null)
        };

    internal readonly record struct ExceptionMapping(int StatusCode, string Title, string? Detail);
}
