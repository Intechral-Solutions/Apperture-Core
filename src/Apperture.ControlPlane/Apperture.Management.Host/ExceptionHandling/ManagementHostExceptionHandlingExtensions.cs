namespace Apperture.Management.Host.ExceptionHandling;

internal static class ManagementHostExceptionHandlingExtensions
{
    public static IServiceCollection AddManagementHostExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ManagementHostExceptionHandler>();
        return services;
    }

    public static WebApplication UseManagementHostExceptionHandling(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        return app;
    }
}
