using Apperture.Cli.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Apperture.Cli.Host;

/// <summary>
/// Operator commands for the Entra-independent emergency Platform Administrator.
/// Passwords, TOTP secrets, and recovery codes are never taken from argv.
/// </summary>
internal static class EmergencyCli
{
    private static readonly ActorId CliActor = new(EmergencyAccessService.CliActorId);

    public static async Task<int> RunAsync(string[] args, IConfiguration config, string environmentName)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        var cmdArgs = args.Length > 1 ? args[1..] : [];
        if (command is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        using var provider = BuildProvider(config, environmentName);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var emergency = sp.GetRequiredService<IEmergencyAccessService>();
        var mfa = sp.GetRequiredService<INativeMfaService>();
        var users = sp.GetRequiredService<IUserRepository>();

        try
        {
            switch (command)
            {
                case "status":
                    return await StatusAsync(emergency);
                case "provision":
                    return await ProvisionAsync(emergency, cmdArgs);
                case "designate":
                    return await DesignateAsync(emergency, users, cmdArgs);
                case "set-password":
                    return await SetPasswordAsync(emergency, users, cmdArgs);
                case "enroll-mfa":
                    return await EnrollMfaAsync(mfa, users, cmdArgs, reset: false);
                case "reset-mfa":
                    return await EnrollMfaAsync(mfa, users, cmdArgs, reset: true);
                case "regenerate-codes":
                    return await RegenerateCodesAsync(mfa, users, cmdArgs);
                case "revoke":
                    return await RevokeAsync(emergency, cmdArgs);
                default:
                    Console.Error.WriteLine($"Unknown emergency command: {command}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (EmergencyAccessException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Apperture emergency access (Entra-independent Platform Administrator)");
        Console.WriteLine("  emergency status");
        Console.WriteLine("  emergency provision --display-name <name> --username <username>");
        Console.WriteLine("  emergency designate --username <username>");
        Console.WriteLine("  emergency set-password --username <username>");
        Console.WriteLine("  emergency enroll-mfa --username <username>");
        Console.WriteLine("  emergency reset-mfa --username <username>");
        Console.WriteLine("  emergency regenerate-codes --username <username>");
        Console.WriteLine("  emergency revoke --confirm-fallback");
        Console.WriteLine();
        Console.WriteLine("Passwords and authenticator codes are read from stdin (prompted when interactive).");
        Console.WriteLine("Do not pass secrets on the command line.");
    }

    private static ServiceProvider BuildProvider(IConfiguration config, string environmentName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Emergency CLI protects authenticator secrets with Windows DPAPI. Run it on Windows, or provide a host-appropriate protector.");
        }

        var services = new ServiceCollection();
        services.AddBootstrapCli(config, environmentName);
        services.AddApperturePersistence(sp =>
            sp.GetRequiredService<Apperture.Bootstrap.IBootstrapConnectionProfile>().ConnectionString);
        services.AddSingleton<Apperture.Core.Security.Secrets.Abstractions.ISecretProtector, DpapiSecretProtector>();
        services.AddAppertureEmergencyOperatorServices();
        return services.BuildServiceProvider();
    }

    private static async Task<int> StatusAsync(IEmergencyAccessService emergency)
    {
        var posture = await emergency.GetPostureAsync();
        Console.WriteLine($"Healthy: {posture.IsHealthy}");
        Console.WriteLine($"Fallback safeguard required: {posture.FallbackSafeguardRequired}");
        Console.WriteLine($"Designated user id: {posture.DesignatedUserId ?? "(none)"}");
        Console.WriteLine($"Display name: {posture.DesignatedDisplayName ?? "(none)"}");
        Console.WriteLine($"Platform Administrator: {posture.DesignatedIsPlatformAdministrator}");
        Console.WriteLine($"Local credential enabled: {posture.LocalCredentialEnabled}");
        Console.WriteLine($"Native MFA enrolled: {posture.NativeMfaEnrolled}");
        Console.WriteLine($"Unused recovery codes: {posture.UnusedRecoveryCodeCount}");
        return 0;
    }

    private static async Task<int> ProvisionAsync(IEmergencyAccessService emergency, string[] args)
    {
        var displayName = GetOption(args, "--display-name");
        var username = GetOption(args, "--username");
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(username))
        {
            Console.Error.WriteLine("provision requires --display-name and --username.");
            return 1;
        }

        var password = await ReadSecretAsync("Enter emergency local password:");
        if (password is null)
            return 1;
        var confirm = await ReadSecretAsync("Confirm password:");
        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Passwords do not match.");
            return 1;
        }

        var userId = await emergency.ProvisionAsync(displayName, username, password, CliActor);
        Console.WriteLine($"Provisioned emergency administrator userId={userId.Value}");
        Console.WriteLine("Next: run 'emergency enroll-mfa --username <username>' and store the recovery codes.");
        return 0;
    }

    private static async Task<int> DesignateAsync(IEmergencyAccessService emergency, IUserRepository users, string[] args)
    {
        var user = await ResolveUserAsync(users, args);
        if (user is null)
            return 1;
        await emergency.DesignateAsync(user.Id, CliActor);
        Console.WriteLine($"Designated emergency administrator userId={user.Id.Value}");
        return 0;
    }

    private static async Task<int> SetPasswordAsync(IEmergencyAccessService emergency, IUserRepository users, string[] args)
    {
        var user = await ResolveUserAsync(users, args);
        if (user is null)
            return 1;
        var password = await ReadSecretAsync("Enter new local password:");
        if (password is null)
            return 1;
        var confirm = await ReadSecretAsync("Confirm password:");
        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Passwords do not match.");
            return 1;
        }

        await emergency.SetLocalPasswordAsync(user.Id, password, CliActor);
        Console.WriteLine("Local password updated.");
        return 0;
    }

    private static async Task<int> EnrollMfaAsync(INativeMfaService mfa, IUserRepository users, string[] args, bool reset)
    {
        var user = await ResolveUserAsync(users, args);
        if (user is null)
            return 1;
        var username = user.IdentityLinks.FirstOrDefault(l => l.Source == IdentitySource.Local)?.ExternalId
            ?? user.DisplayName;
        if (reset)
            await mfa.ResetAsync(user.Id, CliActor);

        var challenge = await mfa.BeginEnrollmentAsync(user.Id, username);
        Console.WriteLine("Add this account in an authenticator app:");
        Console.WriteLine(challenge.OtpAuthUri);
        Console.WriteLine($"Manual key: {challenge.ManualEntryKey}");
        var totp = await ReadSecretAsync("Enter the 6-digit authenticator code:");
        if (totp is null)
            return 1;
        var codes = await mfa.CompleteEnrollmentAsync(user.Id, totp);
        WriteRecoveryCodesOnce(codes);
        return 0;
    }

    private static async Task<int> RegenerateCodesAsync(INativeMfaService mfa, IUserRepository users, string[] args)
    {
        var user = await ResolveUserAsync(users, args);
        if (user is null)
            return 1;
        var codes = await mfa.RegenerateRecoveryCodesAsync(user.Id);
        WriteRecoveryCodesOnce(codes);
        return 0;
    }

    private static async Task<int> RevokeAsync(IEmergencyAccessService emergency, string[] args)
    {
        var confirm = args.Any(a => string.Equals(a, "--confirm-fallback", StringComparison.OrdinalIgnoreCase));
        await emergency.RevokeAsync(confirm, CliActor);
        Console.WriteLine("Emergency designation cleared. Last-administrator fallback protection is required until a new emergency account is provisioned.");
        return 0;
    }

    private static async Task<User?> ResolveUserAsync(IUserRepository users, string[] args)
    {
        var userId = GetOption(args, "--user-id");
        var username = GetOption(args, "--username");
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var byId = await users.GetByIdAsync(new UserId(userId));
            if (byId is null)
            {
                Console.Error.WriteLine("User not found.");
                return null;
            }

            return byId;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            Console.Error.WriteLine("Specify --username or --user-id.");
            return null;
        }

        var byName = await users.GetByIdentityLinkAsync(IdentitySource.Local, LocalAuthHelpers.NormalizeUsername(username));
        if (byName is null)
        {
            Console.Error.WriteLine("User not found.");
            return null;
        }

        return byName;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static async Task<string?> ReadSecretAsync(string prompt)
    {
        if (!Console.IsInputRedirected)
            Console.Error.WriteLine(prompt);
        var value = await SensitiveInputHelper.ReadSensitivePayloadFromStdinAsync();
        if (string.IsNullOrEmpty(value))
        {
            Console.Error.WriteLine("No input.");
            return null;
        }

        return value;
    }

    private static void WriteRecoveryCodesOnce(IReadOnlyList<string> codes)
    {
        Console.WriteLine("Store these recovery codes now. They will not be shown again.");
        foreach (var code in codes)
            Console.WriteLine(code);
    }
}
