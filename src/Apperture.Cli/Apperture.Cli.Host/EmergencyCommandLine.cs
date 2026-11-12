namespace Apperture.Cli.Host;

/// <summary>An emergency command and the options it was given, checked before anything is opened.</summary>
public sealed record EmergencyCommand(
    string Name,
    string? UserId = null,
    string? Username = null,
    bool BareReset = false,
    bool ConfirmFallback = false,
    bool Help = false);

/// <summary>
/// Reads the emergency command line strictly: an option the command does not take, a missing value, a repeated option or two ways of naming
/// the user is a usage error, never ignored. A typed-wrong option must not turn a replacement into something else, or aim a change at the
/// default account.
/// </summary>
public static class EmergencyCommandLine
{
    private const string UserIdOption = "--user-id";
    private const string UsernameOption = "--username";
    private const string BareResetOption = "--bare-reset";
    private const string ConfirmFallbackOption = "--confirm-fallback";

    private static readonly string[] AccountOptions = [UserIdOption, UsernameOption];

    /// <summary>The commands and the options each takes.</summary>
    private static readonly Dictionary<string, string[]> Commands = new(StringComparer.Ordinal)
    {
        ["status"] = [],
        ["provision"] = [],
        ["designate"] = AccountOptions,
        ["set-password"] = AccountOptions,
        ["enroll-mfa"] = AccountOptions,
        ["reset-mfa"] = [UserIdOption, UsernameOption, BareResetOption],
        ["regenerate-codes"] = AccountOptions,
        ["revoke"] = [ConfirmFallbackOption],
    };

    public static bool IsCommand(string name) => Commands.ContainsKey(name);

    public static bool TryParse(string[] args, out EmergencyCommand? command, out string? error)
    {
        command = null;
        error = null;
        var name = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        if (name is "help" or "--help" or "-h")
        {
            command = new EmergencyCommand("help", Help: true);
            return true;
        }

        if (!Commands.TryGetValue(name, out var allowed))
        {
            error = $"Unknown emergency command: {args[0]}";
            return false;
        }

        string? userId = null;
        string? username = null;
        var bareReset = false;
        var confirmFallback = false;
        var help = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < args.Length; i++)
        {
            var option = args[i].ToLowerInvariant();
            if (option is "--help" or "-h")
            {
                help = true;
                continue;
            }

            if (!allowed.Contains(option, StringComparer.Ordinal))
            {
                error = $"'emergency {name}' does not take '{args[i]}'.";
                return false;
            }

            if (!seen.Add(option))
            {
                error = $"'{option}' was given more than once.";
                return false;
            }

            switch (option)
            {
                case BareResetOption:
                    bareReset = true;
                    break;
                case ConfirmFallbackOption:
                    confirmFallback = true;
                    break;
                default:
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = $"'{option}' needs a value.";
                        return false;
                    }

                    if (option == UserIdOption)
                        userId = args[++i];
                    else
                        username = args[++i];
                    break;
            }
        }

        if (userId is not null && username is not null)
        {
            error = $"Give the account with '{UserIdOption}' or '{UsernameOption}', not both.";
            return false;
        }

        command = new EmergencyCommand(name, userId, username, bareReset, confirmFallback, help);
        return true;
    }
}
