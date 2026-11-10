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
public sealed class SystemEmergencyConsole(TextReader? input = null) : IEmergencyConsole
{
    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;

    public bool IsInputRedirected => Console.IsInputRedirected;

    public async Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Console.IsInputRedirected)
            Console.Error.WriteLine(prompt);

        // The console's reader blocks the calling thread (its async read completes synchronously), so the read runs on a thread of its own
        // beside a wait on the token: when the operator cancels, the command stops waiting (and discards what it staged) and the
        // abandoned read dies with the process.
        var reader = input ?? Console.In;
        var read = Task.Run(() => reader.ReadLine(), CancellationToken.None);
        var cancelled = Task.Delay(Timeout.Infinite, cancellationToken);
        await Task.WhenAny(read, cancelled).ConfigureAwait(false);

        // A line that arrives together with the cancellation is never used: the operator asked to stop.
        cancellationToken.ThrowIfCancellationRequested();
        return (await read.ConfigureAwait(false))?.Trim();
    }
}
