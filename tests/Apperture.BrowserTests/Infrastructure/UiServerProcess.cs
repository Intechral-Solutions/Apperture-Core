using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Apperture.BrowserTests.Infrastructure;

/// <summary>
/// The real Management UI host (<c>Apperture.Management.Ui.Server</c>) started from its own build output as
/// a child process at <c>http://localhost:{ui}</c>. It serves the real Blazor WebAssembly client. The
/// harness owns the process: it is tracked by PID, assigned to a kill-on-close job object, and stopped in
/// <see cref="DisposeAsync"/>. No other process is ever signalled.
/// </summary>
public sealed class UiServerProcess : IAsyncDisposable
{
    private const int LogLines = 200;

    private readonly HarnessOrigins _origins;
    private readonly ChildProcessJob _job = new();
    private readonly Queue<string> _log = new();
    private Process? _process;

    public UiServerProcess(HarnessOrigins origins) => _origins = origins;

    /// <summary>Last output lines, for failure diagnostics. Contains no secrets: the UI host has none.</summary>
    public string RecentOutput
    {
        get
        {
            lock (_log)
                return string.Join(Environment.NewLine, _log);
        }
    }

    public static string ResolveAssemblyPath()
    {
        var path = typeof(UiServerProcess).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UiServerAssemblyPath")?.Value;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "Ui.Server build output not found. Build tests/Apperture.BrowserTests (it builds Ui.Server first).", path);
        }

        return path;
    }

    public async Task StartAsync(TimeSpan timeout)
    {
        var dll = ResolveAssemblyPath();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(dll);

        // Development: the only environment in which the host serves the client's static web assets from
        // the build output. The developer's own Aspire variables are not inherited into anything that matters.
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = _origins.Ui;
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Append(e.Data);
        _process.ErrorDataReceived += (_, e) => Append(e.Data);
        _process.Start();
        _job.Assign(_process);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"Ui.Server exited early (code {_process.ExitCode}).{Environment.NewLine}{RecentOutput}");

            try
            {
                using var response = await http.GetAsync(_origins.Ui + "/_framework/blazor.webassembly.js").ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return;

                last = new InvalidOperationException($"blazor.webassembly.js returned {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        throw new TimeoutException($"Ui.Server did not become ready within {timeout}. Last error: {last?.Message}{Environment.NewLine}{RecentOutput}");
    }

    public async ValueTask DisposeAsync()
    {
        var process = _process;
        _process = null;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // The job object is the backstop.
            }

            process.Dispose();
        }

        _job.Dispose();
    }

    private void Append(string? line)
    {
        if (line is null)
            return;

        lock (_log)
        {
            _log.Enqueue(line);
            while (_log.Count > LogLines)
                _log.Dequeue();
        }
    }
}
