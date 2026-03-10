using Apperture.Cli.Bootstrap;
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
        case "set-auth-mode":
        case "bind-keyvault-secret":
        case "bind-managed-identity":
        case "bind-integrated-security":
        case "rotate-sql-credential":
        case "repair":
        case "recovery-unlock":
            Console.WriteLine($"Bootstrap subcommand '{cmd}' - not implemented (scaffolding).");
            break;
        case "set-password":
            // When implementing: obtain payload via SensitiveInputHelper.ReadSensitivePayloadFromStdinAsync or secure prompt only; never from subArgs or argv.
            Console.WriteLine("Bootstrap subcommand 'set-password' - not implemented (scaffolding). Password must not be passed on the command line.");
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
    Console.WriteLine("  bootstrap set-profile    - set server/database (not implemented)");
    Console.WriteLine("  bootstrap set-password   - set SQL credential via stdin or prompt; never pass password on command line (not implemented)");
    Console.WriteLine("  bootstrap repair         - repair path (not implemented)");
    Console.WriteLine("  bootstrap recovery-unlock - recovery only (not implemented)");
}

static void PrintBootstrapHelp()
{
    Console.WriteLine("bootstrap show | validate | sql test | set-profile | set-auth-mode | set-password |");
    Console.WriteLine("  bind-keyvault-secret | bind-managed-identity | bind-integrated-security |");
    Console.WriteLine("  rotate-sql-credential | repair | recovery-unlock");
    Console.WriteLine("  set-password: obtain credential via stdin or secure prompt; do not use command-line arguments.");
}
