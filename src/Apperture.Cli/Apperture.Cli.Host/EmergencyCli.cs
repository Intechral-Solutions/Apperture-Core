using Apperture.Cli.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Cli.Host;

/// <summary>
/// Operator commands for the Entra-independent emergency Platform Administrator.
/// Passwords, TOTP secrets, and recovery codes are never taken from argv.
/// </summary>
/// <remarks>
/// The exit status is one of <see cref="EmergencyExitCodes"/>; see <see cref="EmergencyHelp"/> for what the operator is shown.
/// </remarks>
public static class EmergencyCli
{
    public static Task<int> RunAsync(string[] args, IConfiguration config, string environmentName) =>
        RunAsync(args, config, environmentName, console: null, configureServices: null);

    /// <param name="console">The console to use; the process console when null.</param>
    /// <param name="configureServices">
    /// Replaces registrations after the production composition (a test points the container at a throwaway catalog and a test protector).
    /// Null in the process.
    /// </param>
    public static async Task<int> RunAsync(
        string[] args,
        IConfiguration config,
        string environmentName,
        IEmergencyConsole? console,
        Action<IServiceCollection>? configureServices)
    {
        console ??= new SystemEmergencyConsole();

        // Usage is settled before anything is opened: a typed-wrong command never reaches a service.
        if (!EmergencyCommandLine.TryParse(args, out var command, out var usageError))
        {
            console.Error.WriteLine(usageError);
            EmergencyHelp.Write(console.Error, command: null);
            return EmergencyExitCodes.InvalidUsage;
        }

        if (command!.Help)
        {
            EmergencyHelp.Write(console.Out, command.Name == "help" ? null : command.Name);
            return EmergencyExitCodes.Success;
        }

        // Cancelling (Ctrl+C) stops the wait for operator input only; a change that has started is never torn.
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            ServiceProvider provider;
            try
            {
                provider = BuildProvider(config, environmentName, configureServices);
            }
            catch (Exception ex)
            {
                var outcome = EmergencyOutcomeMapper.Map(ex);
                console.Error.WriteLine($"Error: {outcome.Message}");
                if (!string.IsNullOrEmpty(outcome.NextStep))
                    console.Error.WriteLine($"Next: {outcome.NextStep}");
                return outcome.ExitCode;
            }

            using (provider)
            using (var scope = provider.CreateScope())
            {
                var runner = CreateRunner(scope.ServiceProvider, console);
                return await runner.RunAsync(command, cancellation.Token);
            }
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>
    /// The runner over the services the container provides. This is the one place the command line takes services from its container; the
    /// change-making services are the application services, and nothing here resolves a repository of the authenticator or the candidates.
    /// </summary>
    public static EmergencyCommandRunner CreateRunner(IServiceProvider services, IEmergencyConsole console) =>
        new(
            services.GetRequiredService<INativeMfaService>(),
            services.GetRequiredService<IEmergencyAccessService>(),
            services.GetRequiredService<IUserRepository>(),
            console);

    /// <summary>The production composition of the emergency command line, then <paramref name="configureServices"/> (a test seam; null in the process).</summary>
    public static ServiceProvider BuildProvider(IConfiguration config, string environmentName, Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddBootstrapCli(config, environmentName);
        services.AddApperturePersistence(sp =>
            sp.GetRequiredService<Apperture.Bootstrap.IBootstrapConnectionProfile>().ConnectionString);
        if (OperatingSystem.IsWindows())
            services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddAppertureEmergencyOperatorServices();
        configureServices?.Invoke(services);

        // Authenticator secrets are protected with Windows DPAPI: without it (and without a protector provided for the host) there is none.
        if (!services.Any(d => d.ServiceType == typeof(ISecretProtector)))
        {
            throw new PlatformNotSupportedException(
                "Emergency CLI protects authenticator secrets with Windows DPAPI. Run it on Windows, or provide a host-appropriate protector.");
        }

        return services.BuildServiceProvider();
    }
}
