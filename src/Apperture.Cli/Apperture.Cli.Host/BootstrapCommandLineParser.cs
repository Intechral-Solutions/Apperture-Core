using Apperture.Bootstrap.Models;

namespace Apperture.Cli.Host;

public static class BootstrapCommandLineParser
{
    public static bool TryParseSetProfile(string[] args, out SqlConnectionBootstrapProfile? profile, out string? error)
    {
        profile = null;
        error = null;
        string? server = null;
        string? database = null;
        var trustServerCert = true;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                server = args[++i];
            }
            else if (arg.Equals("--database", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                database = args[++i];
            }
            else if (arg.Equals("--trust-server-cert", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (!bool.TryParse(args[++i], out trustServerCert))
                {
                    error = "Invalid value for --trust-server-cert; use true or false.";
                    return false;
                }
            }
            else
            {
                error = $"Unknown argument: {arg}";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            error = "set-profile requires --server and --database.";
            return false;
        }

        profile = new SqlConnectionBootstrapProfile
        {
            Server = server,
            Database = database,
            AuthMode = BootstrapAuthMode.SqlPassword,
            TrustServerCertificate = trustServerCert
        };
        return true;
    }

    public static bool TryParseAuthMode(string[] args, out BootstrapAuthMode? mode, out string? error)
    {
        mode = null;
        error = null;
        if (args.Length != 1)
        {
            error = "set-auth-mode requires exactly one argument: the auth mode name.";
            return false;
        }

        if (!Enum.TryParse<BootstrapAuthMode>(args[0], ignoreCase: true, out var parsed))
        {
            error = $"Unknown auth mode '{args[0]}'.";
            return false;
        }

        mode = parsed;
        return true;
    }
}
