using Apperture.Bootstrap.Models;
using Apperture.Cli.Bootstrap;
using Apperture.Cli.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var argsList = args.Length > 0 ? args : ["--help"];
var (subcommand, subArgs) = ParseArgs(argsList);

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var environmentName = CliEnvironmentResolver.GetEnvironmentName();
var services = new ServiceCollection();
services.AddBootstrapCli(config, environmentName);
var provider = services.BuildServiceProvider();

try
{
    var bootstrapCli = provider.GetRequiredService<BootstrapCliService>();

    switch (subcommand.ToLowerInvariant())
    {
        case "bootstrap":
            await RunBootstrapAsync(bootstrapCli, subArgs);
            break;
        case "--help":
        case "-h":
        case "help":
            PrintHelp();
            break;
        default:
            Console.Error.WriteLine($"Unknown command: {subcommand}");
            PrintHelp();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

return 0;

static (string Subcommand, string[] SubArgs) ParseArgs(string[] a)
{
    if (a.Length == 0) return ("help", []);
    if (a[0].StartsWith('-')) return ("help", []);
    return (a[0], a.Length > 1 ? a[1..] : []);
}

static async Task RunBootstrapAsync(BootstrapCliService bootstrapCli, string[] subArgs)
{
    var cmd = subArgs.Length > 0 ? subArgs[0].ToLowerInvariant() : "show";
    var cmdArgs = subArgs.Length > 1 ? subArgs[1..] : [];

    switch (cmd)
    {
        case "show":
            var showResult = await bootstrapCli.ShowAsync();
            WriteResult(showResult);
            break;
        case "validate":
            var validateResult = await bootstrapCli.ValidateAsync();
            Console.WriteLine(validateResult.Success ? "Validation succeeded." : $"Validation failed: {string.Join("; ", validateResult.Errors)}");
            break;
        case "sql":
            if (cmdArgs.Length > 0 && cmdArgs[0].ToLowerInvariant() == "test")
            {
                var testResult = await bootstrapCli.TestSqlConnectionAsync();
                WriteResult(testResult);
            }
            else
            {
                Console.Error.WriteLine("bootstrap sql test - test SQL connection");
            }
            break;
        case "set-profile":
        {
            if (!BootstrapCommandLineParser.TryParseSetProfile(cmdArgs, out var setProfile, out var setProfileError))
            {
                Console.Error.WriteLine(setProfileError);
                return;
            }

            WriteResult(await bootstrapCli.SetProfileAsync(setProfile!));
            break;
        }
        case "set-auth-mode":
        {
            if (!BootstrapCommandLineParser.TryParseAuthMode(cmdArgs, out var authMode, out var authModeError))
            {
                Console.Error.WriteLine(authModeError);
                return;
            }

            WriteResult(await bootstrapCli.SetAuthModeAsync(authMode!.Value));
            break;
        }
        case "bind-keyvault-secret":
            if (cmdArgs.Length != 1)
            {
                Console.Error.WriteLine("bind-keyvault-secret requires a secret URI argument.");
                return;
            }

            WriteResult(await bootstrapCli.BindKeyVaultAsync(cmdArgs[0]));
            break;
        case "bind-managed-identity":
            WriteResult(await bootstrapCli.BindManagedIdentityAsync());
            break;
        case "bind-integrated-security":
            WriteResult(await bootstrapCli.BindIntegratedSecurityAsync());
            break;
        case "set-password":
            WriteResult(await bootstrapCli.SetPasswordAsync(new object()));
            break;
        case "rotate-sql-credential":
        case "repair":
        case "recovery-unlock":
            Console.WriteLine($"Bootstrap subcommand '{cmd}' - not implemented (deferred).");
            break;
        default:
            Console.Error.WriteLine($"Unknown bootstrap subcommand: {cmd}");
            PrintBootstrapHelp();
            break;
    }
}

static void WriteResult(BootstrapCommandResult r)
{
    Console.WriteLine(r.Success ? r.Message : $"Error: {r.Message}");
    if (r.RestartRecommended)
        Console.WriteLine("Restart recommended.");
}

static void PrintHelp()
{
    Console.WriteLine("Apperture CLI");
    Console.WriteLine("  bootstrap show           - show bootstrap config (no secrets)");
    Console.WriteLine("  bootstrap validate       - validate config");
    Console.WriteLine("  bootstrap sql test       - test SQL connection");
    Console.WriteLine("  bootstrap set-profile --server <host> --database <name> [--trust-server-cert true|false]");
    Console.WriteLine("  bootstrap set-auth-mode <mode>  - SqlPassword, WindowsIntegrated, ManagedIdentity, etc.");
    Console.WriteLine("  bootstrap bind-keyvault-secret <uri> | bind-managed-identity | bind-integrated-security");
    Console.WriteLine("  bootstrap set-password   - deferred (Epic 108b); use bind-* commands");
    Console.WriteLine("  bootstrap repair | recovery-unlock | rotate-sql-credential - deferred");
}

static void PrintBootstrapHelp()
{
    Console.WriteLine("bootstrap show | validate | sql test | set-profile | set-auth-mode | set-password |");
    Console.WriteLine("  bind-keyvault-secret | bind-managed-identity | bind-integrated-security |");
    Console.WriteLine("  rotate-sql-credential | repair | recovery-unlock");
    Console.WriteLine("  set-password: obtain credential via stdin or secure prompt; do not use command-line arguments.");
}
