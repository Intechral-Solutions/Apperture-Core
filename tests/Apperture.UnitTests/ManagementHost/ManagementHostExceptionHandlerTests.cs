using System.Text.Json;
using Apperture.Core.Application.PlatformEvents;
using Apperture.Management.Host.ExceptionHandling;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Apperture.UnitTests.ManagementHost;

public sealed class ManagementHostExceptionHandlerTests
{
    private const string SecretMarker = "SECRET_INTERNAL_STACK_MARKER_9f3a2b1c";

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), 403, "Forbidden")]
    [InlineData(typeof(ArgumentException), 400, "Bad Request")]
    [InlineData(typeof(KeyNotFoundException), 404, "Not Found")]
    public async Task TryHandleAsync_maps_known_exceptions_to_expected_status(
        Type exceptionType,
        int expectedStatus,
        string expectedTitle)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "test message")!;
        var (context, body) = await InvokeHandlerAsync(exception, correlationId: "corr-known-001");

        context.Response.StatusCode.Should().Be(expectedStatus);
        context.Response.ContentType.Should().StartWith("application/problem+json");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(expectedStatus);
        root.GetProperty("title").GetString().Should().Be(expectedTitle);
        root.GetProperty("correlationId").GetString().Should().Be("corr-known-001");
    }

    [Fact]
    public async Task TryHandleAsync_validation_exception_returns_400_problem_details()
    {
        var exception = new PlatformEventValidationException("Event type is required.");
        var (context, body) = await InvokeHandlerAsync(exception);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(400);
        root.GetProperty("detail").GetString().Should().Be("Event type is required.");
        root.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task TryHandleAsync_unknown_exception_returns_500_with_correlation_id()
    {
        var exception = new InvalidOperationException(SecretMarker);
        var (context, body) = await InvokeHandlerAsync(exception, correlationId: "corr-unexpected-500");

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(500);
        root.GetProperty("title").GetString()
            .Should().Be("An error occurred while processing your request.");
        root.GetProperty("correlationId").GetString().Should().Be("corr-unexpected-500");
    }

    [Fact]
    public async Task TryHandleAsync_unknown_exception_does_not_expose_exception_details()
    {
        var exception = new InvalidOperationException(SecretMarker);
        var (_, body) = await InvokeHandlerAsync(exception);

        body.Should().NotContain(SecretMarker);
        body.Should().NotContain("InvalidOperationException");
        body.Should().NotContain("stackTrace", because: "raw exception details must not reach clients");
        body.Should().NotContain("StackTrace");
        body.Should().NotContain(" at ");
    }

    [Fact]
    public void MapException_does_not_map_invalid_operation_exception_to_client_error()
    {
        var mapping = ManagementHostExceptionHandler.MapException(
            new InvalidOperationException("Schedule sched-1 not found."));

        mapping.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        mapping.Detail.Should().BeNull();
    }

    [Fact]
    public void MapException_reports_a_committed_operation_whose_result_could_not_be_read_as_applied()
    {
        // AUD-1.3: verification confirmed the commit; the response says so and names the operation, so the client reloads
        // instead of repeating it. It is never a refusal or a "not found".
        var mapping = ManagementHostExceptionHandler.MapException(
            new Apperture.Core.Application.Audit.AuditCommittedResultUnavailableException("op-1", new InvalidOperationException("read failed")));

        mapping.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        mapping.Title.Should().Be("The operation was applied.");
        mapping.Extensions!["code"].Should().Be("committed_result_unavailable");
        mapping.Extensions["operationEventId"].Should().Be("op-1");
    }

    private static async Task<(HttpContext Context, string Body)> InvokeHandlerAsync(
        Exception exception,
        string? correlationId = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var handler = new ManagementHostExceptionHandler(
            provider.GetRequiredService<ILogger<ManagementHostExceptionHandler>>());

        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            Response =
            {
                Body = new MemoryStream()
            }
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
            context.Items[CorrelationIdMiddleware.ItemKey] = correlationId;

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context, body);
    }
}
