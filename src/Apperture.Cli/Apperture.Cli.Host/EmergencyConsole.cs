using Apperture.Cli.Bootstrap;

namespace Apperture.Cli.Host;

/// <summary>Where the emergency commands write and read. The process uses the real console; a test scripts it.</summary>
public interface IEmergencyConsole
{
    /// <summary>Results and instructions (a staged secret, recovery codes that were committed).</summary>
    TextWriter Out { get; }

    /// <summary>Diagnostics, prompts and errors.</summary>
    TextWriter Error { get; }

    /// <summary>True when input is piped or redirected: nobody is at a prompt, so nothing is asked twice.</summary>
    bool IsInputRedirected { get; }

    /// <summary>
    /// Reads one line of sensitive input (a password, an authenticator code), prompting only when interactive. Null when nothing was entered.
    /// Throws <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled while waiting.
    /// </summary>
    Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellationToken);
}

/// <summary>The process console. Sensitive input comes from stdin and never from the command line.</summary>
public sealed class SystemEmergencyConsole : IEmergencyConsole
{
    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;

    public bool IsInputRedirected => Console.IsInputRedirected;

    public async Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellationToken)
    {
        if (!Console.IsInputRedirected)
            Console.Error.WriteLine(prompt);

        // A blocking console read cannot be interrupted, so the read runs beside a wait on the token: when the operator cancels, the
        // command stops waiting (and discards what it staged) while the abandoned read dies with the process.
        var read = SensitiveInputHelper.ReadSensitivePayloadFromStdinAsync(CancellationToken.None);
        var cancelled = Task.Delay(Timeout.Infinite, cancellationToken);
        var finished = await Task.WhenAny(read, cancelled).ConfigureAwait(false);
        if (finished != read)
            throw new OperationCanceledException(cancellationToken);
        return await read.ConfigureAwait(false);
    }
}
