namespace Apperture.Cli.Host;

/// <summary>The help the emergency commands print: what each does, and which of them is safe and which destroys.</summary>
public static class EmergencyHelp
{
    /// <summary>Writes the overview, or the help of one <paramref name="command"/>.</summary>
    public static void Write(TextWriter writer, string? command)
    {
        switch (command)
        {
            case "reset-mfa":
                WriteResetMfa(writer);
                return;
            case "revoke":
                writer.WriteLine("emergency revoke --confirm-fallback");
                writer.WriteLine("  Clears the Recovery Access designation. It is refused unless another administrator can sign in without Microsoft,");
                writer.WriteLine("  and it needs the sign-in configuration the Management Host has recorded.");
                return;
            case "enroll-mfa":
                writer.WriteLine("emergency enroll-mfa [--user-id <id> | --username <name>]");
                writer.WriteLine("  First enrolment of an authenticator. Refused when one is already enrolled; use 'emergency reset-mfa' to change it.");
                writer.WriteLine("  The recovery codes are shown once, after the enrolment is confirmed.");
                return;
            case "regenerate-codes":
                writer.WriteLine("emergency regenerate-codes [--user-id <id> | --username <name>]");
                writer.WriteLine("  Replaces the recovery codes. The new codes are shown once, after the change is confirmed.");
                return;
            default:
                WriteOverview(writer);
                return;
        }
    }

    private static void WriteOverview(TextWriter writer)
    {
        writer.WriteLine("Apperture Emergency Access (system-managed Recovery Access account)");
        writer.WriteLine("  emergency status");
        writer.WriteLine("  emergency provision");
        writer.WriteLine("  emergency designate [--user-id <id> | --username <name>]");
        writer.WriteLine("  emergency set-password [--user-id <id> | --username <name>]");
        writer.WriteLine("  emergency enroll-mfa [--user-id <id> | --username <name>]");
        writer.WriteLine("  emergency reset-mfa [--user-id <id> | --username <name>]               replace the authenticator, no gap (normal)");
        writer.WriteLine("  emergency reset-mfa --bare-reset [--user-id <id> | --username <name>]  DESTRUCTIVE: remove it with nothing in its place");
        writer.WriteLine("  emergency regenerate-codes [--user-id <id> | --username <name>]");
        writer.WriteLine("  emergency revoke --confirm-fallback");
        writer.WriteLine("  emergency <command> --help");
        writer.WriteLine();
        writer.WriteLine("Recovery Access uses username 'breakglass'. Username and display name are not configurable.");
        writer.WriteLine("Passwords and authenticator codes are read from stdin (prompted when interactive).");
        writer.WriteLine("Do not pass secrets on the command line.");
        writer.WriteLine();
        writer.WriteLine("Exit status:");
        writer.WriteLine("  0  done and confirmed");
        writer.WriteLine("  1  refused (nothing changed): a safety rule, a missing prerequisite, or you cancelled");
        writer.WriteLine("  2  invalid usage or input (nothing changed): unknown command or option, a wrong or missing code or password");
        writer.WriteLine("  3  busy or conflicting change (nothing changed): run the command again");
        writer.WriteLine("  4  the sign-in configuration needed for the decision is not available (nothing changed): start the Management Host");
        writer.WriteLine("  5  result not confirmed: the change may or may not have been applied, or was applied but its result could not be shown.");
        writer.WriteLine("     Run 'emergency status' before doing anything else.");
        writer.WriteLine("  6  unexpected internal failure");
    }

    private static void WriteResetMfa(TextWriter writer)
    {
        writer.WriteLine("emergency reset-mfa [--user-id <id> | --username <name>]");
        writer.WriteLine("  Replaces the account's authenticator without a gap. This is the normal way and it is safe to abandon:");
        writer.WriteLine("    1. A new authenticator is staged and its key is shown once. Add it to an authenticator app.");
        writer.WriteLine("    2. Enter a code from the new authenticator. It is checked against the new key, not the current one.");
        writer.WriteLine("    3. Only then is the new authenticator made active and the recovery codes replaced, in one audited step;");
        writer.WriteLine("       the new recovery codes are shown once, after that step is confirmed.");
        writer.WriteLine("  Until step 3 the current authenticator and its recovery codes keep working. Cancelling, a wrong code, or");
        writer.WriteLine("  letting the replacement expire (10 minutes) changes nothing. It is refused when no authenticator is enrolled.");
        writer.WriteLine();
        writer.WriteLine("emergency reset-mfa --bare-reset [--user-id <id> | --username <name>]");
        writer.WriteLine("  DESTRUCTIVE. Removes the authenticator and every recovery code at once, with nothing in their place. The account");
        writer.WriteLine("  cannot complete sign-in until 'emergency enroll-mfa' is run. It is refused when that would leave no administrator");
        writer.WriteLine("  able to sign in, and it is never done instead of a replacement that failed.");
    }
}
