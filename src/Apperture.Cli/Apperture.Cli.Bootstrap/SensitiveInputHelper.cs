namespace Apperture.Cli.Bootstrap;

/// <summary>
/// Helpers for reading sensitive input in bootstrap credential-update commands.
/// For use by set-password and other credential-update commands; do not use argv for secrets.
/// </summary>
public static class SensitiveInputHelper
{
    /// <summary>
    /// Reads one line of sensitive input from stdin (e.g. piped from PowerShell SecureString or prompt).
    /// Use for set-password and other commands that need a credential; never take the value from command-line arguments.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trimmed line, or null if no input. Caller must not log or persist this value.</returns>
    public static async Task<string?> ReadSensitivePayloadFromStdinAsync(CancellationToken cancellationToken = default)
    {
        var line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return line?.Trim();
    }
}
