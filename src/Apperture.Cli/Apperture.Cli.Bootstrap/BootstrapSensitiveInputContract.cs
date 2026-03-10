namespace Apperture.Cli.Bootstrap;

/// <summary>
/// Contract for obtaining sensitive input for bootstrap credential updates (e.g. set-password).
/// Sensitive payload must be obtained via stdin, secure prompt, or other non-argv channel.
/// Must not be passed via command-line arguments or logged.
/// </summary>
/// <remarks>
/// When implementing set-password or other credential-update commands, use
/// <see cref="SensitiveInputHelper.ReadSensitivePayloadFromStdinAsync"/> or an interactive secure prompt.
/// Do not add a --password or similar flag that would pass secrets via argv.
/// </remarks>
public static class BootstrapSensitiveInputContract
{
    /// <summary>
    /// Summary of the contract for use in help and documentation.
    /// </summary>
    public const string ContractSummary =
        "Sensitive payload for credential updates must be obtained via stdin, secure prompt, or other non-argv channel. Must not be passed via command-line arguments or logged.";
}
