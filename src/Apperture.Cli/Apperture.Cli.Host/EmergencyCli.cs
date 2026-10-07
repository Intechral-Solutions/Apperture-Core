using Apperture.Cli.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
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
        catch (AuditOutcomeUnknownException ex)
        {
            // Never reported as failed or unchanged: run 'emergency status' to see what was applied (design gate §13.2).
            Console.Error.WriteLine($"Error: {ex.Message} Run 'emergency status' to check before trying again. Operation: {ex.OperationEventId}");
            return 1;
        }
        catch (AuditOperationNotCompletedException ex)
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
        Console.WriteLine("Apperture Emergency Access (system-managed Recovery Access account)");
        Console.WriteLine("  emergency status");
        Console.WriteLine("  emergency provision");
        Console.WriteLine("  emergency designate --username <username>");
        Console.WriteLine("  emergency set-password");
        Console.WriteLine("  emergency enroll-mfa");
        Console.WriteLine("  emergency reset-mfa");
        Console.WriteLine("  emergency regenerate-codes");
        Console.WriteLine("  emergency revoke --confirm-fallback");
        Console.WriteLine();
        Console.WriteLine("Recovery Access uses username 'breakglass'. Username and display name are not configurable.");
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
        try
        {
            await emergency.EnsureCanonicalIdentityAsync(AuditActor.EmergencyCli);
        }
        catch (EmergencyAccessException ex) when (ex.Code == EmergencyAccessErrorCodes.UsernameConflict)
        {
            Console.Error.WriteLine(ex.Message);
        }

        var posture = await emergency.GetPostureAsync();
        Console.WriteLine($"Healthy: {posture.IsHealthy}");
        Console.WriteLine($"Fallback safeguard required: {posture.FallbackSafeguardRequired}");
        Console.WriteLine($"Account: {posture.DesignatedDisplayName ?? "(none)"}");
        Console.WriteLine($"Username: {posture.DesignatedUsername ?? "(none)"}");
        Console.WriteLine($"Canonical identity: {posture.CanonicalIdentityApplied}");
        if (posture.CanonicalUsernameConflict)
            Console.WriteLine("Username conflict: another user already owns the reserved username. Recovery Access was not renamed.");
        if (posture.HasEntraIdentityLink)
            Console.WriteLine("Warning: Recovery Access has a Microsoft identity linked. Recovery still depends only on local password + native MFA.");
        Console.WriteLine($"Designated user id: {posture.DesignatedUserId ?? "(none)"}");
        Console.WriteLine($"Platform Administrator: {posture.DesignatedIsPlatformAdministrator}");
        Console.WriteLine($"Local credential enabled: {posture.LocalCredentialEnabled}");
        Console.WriteLine($"Authenticator: {(posture.NativeMfaEnrolled ? "Enrolled" : "Not enrolled")}");
        Console.WriteLine($"Recovery codes: {posture.UnusedRecoveryCodeCount} available");
        return 0;
    }

    private static async Task<int> ProvisionAsync(IEmergencyAccessService emergency, string[] args)
    {
        _ = args;
        var posture = await emergency.GetPostureAsync();
        if (!string.IsNullOrEmpty(posture.DesignatedUserId))
        {
            try
            {
                await emergency.EnsureCanonicalIdentityAsync(AuditActor.EmergencyCli);
            }
            catch (EmergencyAccessException ex) when (ex.Code == EmergencyAccessErrorCodes.UsernameConflict)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            Console.WriteLine($"Emergency Access already provisioned userId={posture.DesignatedUserId}");
            Console.WriteLine($"Account: {EmergencyAccessIdentity.DisplayName}  Username: {EmergencyAccessIdentity.LocalUsername}");
            return 0;
        }

        var password = await ReadAcceptedPasswordAsync("Enter emergency local password:");
        if (password is null)
            return 1;

        var userId = await emergency.ProvisionAsync(password, CliActor);
        Console.WriteLine($"Provisioned Recovery Access userId={userId.Value}");
        Console.WriteLine($"Username: {EmergencyAccessIdentity.LocalUsername}");
        Console.WriteLine("Next: run 'emergency enroll-mfa' and store the recovery codes.");
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
        var password = await ReadAcceptedPasswordAsync("Enter new local password:");
        if (password is null)
            return 1;

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
            var designated = await users.FindDesignatedEmergencyUserAsync();
            if (designated is not null)
                return designated;
            username = EmergencyAccessIdentity.LocalUsername;
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

    private static async Task<string?> ReadAcceptedPasswordAsync(string prompt)
    {
        while (true)
        {
            var password = await ReadSecretAsync(prompt);
            if (password is null)
                return null;
            var confirm = await ReadSecretAsync("Confirm password:");
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Passwords do not match.");
                if (Console.IsInputRedirected)
                    return null;
                continue;
            }

            try
            {
                LocalPasswordPolicy.RequireRecoveryAccess(
                    password,
                    LocalPasswordPolicy.ForLocalIdentity(
                        EmergencyAccessIdentity.LocalUsername,
                        EmergencyAccessIdentity.DisplayName));
                return password;
            }
            catch (PasswordPolicyException ex)
            {
                Console.Error.WriteLine(ex.Message);
                foreach (var suggestion in ex.Evaluation.Suggestions)
                    Console.Error.WriteLine(suggestion);
                if (Console.IsInputRedirected)
                    return null;
            }
        }
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
