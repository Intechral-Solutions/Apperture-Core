using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Apperture.Cli.Host;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Cli;

/// <summary>
/// The built emergency executable as a real process, started in a disposable directory with a settings file of the test's choosing. These
/// cases reach no database: the settings file cannot be loaded, or the command is settled by its own usage check before any configuration is read.
/// </summary>
public sealed class EmergencyCliProcessTests : IDisposable
{
    private const string Marker = "Sup3rS3cretMarker";

    // A truncated file that carries a value an operator would not want echoed back.
    private const string MalformedSettings = "{ \"ConnectionStrings\": { \"DefaultConnection\": \"Server=x;Password=" + Marker + "\" ";

    // The apphost the SDK writes beside the program: with a suffix on Windows, without one elsewhere.
    private static readonly string Executable = OperatingSystem.IsWindows() ? "Apperture.Cli.Host.exe" : "Apperture.Cli.Host";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "apperture-cli-process-" + Guid.NewGuid().ToString("N"));

    public EmergencyCliProcessTests() => CopyExecutable(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A scratch directory in the temp folder; a leftover is harmless.
        }
    }

    /// <summary>Copies the executable and the assemblies its dependency file lists (and nothing else) beside a settings file the test then writes.</summary>
    private static void CopyExecutable(string target)
    {
        var source = AppContext.BaseDirectory;
        var deps = Path.Combine(source, "Apperture.Cli.Host.deps.json");
        File.Exists(deps).Should().BeTrue("the test project references the command line host, which brings its dependency file");

        Directory.CreateDirectory(target);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Executable,
            "Apperture.Cli.Host.deps.json",
        };
        using (var document = JsonDocument.Parse(File.ReadAllBytes(deps)))
        {
            foreach (var framework in document.RootElement.GetProperty("targets").EnumerateObject())
                foreach (var library in framework.Value.EnumerateObject())
                    if (library.Value.TryGetProperty("runtime", out var runtime))
                        foreach (var file in runtime.EnumerateObject())
                            names.Add(Path.GetFileName(file.Name));
        }

        foreach (var name in names)
        {
            var from = Path.Combine(source, name);
            if (File.Exists(from))
                File.Copy(from, Path.Combine(target, name));
        }

        File.Exists(Path.Combine(target, Executable)).Should().BeTrue("the executable is part of the test project's output");

        // The Microsoft.Extensions assemblies the host needs are app-local files beside the shipped executable but come from the ASP.NET Core
        // shared framework in this project's output, so the copy declares that framework as well. The code under test is the same executable.
        File.WriteAllText(
            Path.Combine(target, "Apperture.Cli.Host.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
                  { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" }
                ]
              }
            }
            """);
    }

    private sealed record Run(int Exit, string Out, string Err);

    private async Task<Run> RunAsync(string? settings, params string[] args)
    {
        var settingsFile = Path.Combine(_directory, "appsettings.json");
        if (settings is not null)
            File.WriteAllText(settingsFile, settings, new UTF8Encoding(false));
        else if (File.Exists(settingsFile))
            File.Delete(settingsFile);

        var psi = new ProcessStartInfo(Path.Combine(_directory, Executable))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _directory,
        };
        psi.ArgumentList.Add("emergency");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Nothing from the machine or from the test run can name a database: the process knows only the settings file under test.
        foreach (var name in psi.Environment.Keys.Where(k =>
                     k.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase) ||
                     k.StartsWith("APPERTURE_", StringComparison.OrdinalIgnoreCase)).ToList())
            psi.Environment.Remove(name);
        psi.Environment["DOTNET_ENVIRONMENT"] = "Production";
        psi.Environment.Remove("ASPNETCORE_ENVIRONMENT");

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("the emergency executable did not finish in 60 s");
        }

        process.WaitForExit();
        return new Run(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public static TheoryData<string[]> CommandsThatWouldChangeSomething => new()
    {
        { new[] { "status" } },
        { new[] { "provision" } },
        { new[] { "enroll-mfa", "--user-id", "usr-a" } },
        { new[] { "reset-mfa", "--user-id", "usr-a" } },
        { new[] { "reset-mfa", "--bare-reset", "--user-id", "usr-a" } },
        { new[] { "regenerate-codes", "--user-id", "usr-a" } },
        { new[] { "revoke", "--confirm-fallback" } },
    };

    [Theory]
    [MemberData(nameof(CommandsThatWouldChangeSomething))]
    public async Task A_settings_file_that_cannot_be_loaded_ends_the_command_with_the_internal_failure_status_and_nothing_is_started(string[] args)
    {
        var run = await RunAsync(MalformedSettings, args);

        run.Exit.Should().Be(EmergencyExitCodes.InternalFailure, run.Err);
        run.Out.Should().BeEmpty("no command ran, so no result, key or code was produced");
        run.Err.Should().StartWith("Error:").And.Contain("InvalidDataException");
        run.Err.Should().NotContain("Unhandled exception").And.NotContain("   at ", "no stack trace is shown to the operator");
        (run.Out + run.Err).Should().NotContain(Marker, "a configured value is never echoed").And.NotContain("appsettings.json", "the exception message, which names the file, is not shown");
        run.Err.Should().Contain("Next:");
    }

    [Fact]
    public async Task Usage_is_settled_before_the_settings_are_read_so_help_and_a_usage_error_work_with_a_broken_settings_file()
    {
        var help = await RunAsync(MalformedSettings, "reset-mfa", "--help");
        help.Exit.Should().Be(EmergencyExitCodes.Success, help.Err);
        help.Out.Should().Contain("--bare-reset").And.Contain("DESTRUCTIVE");
        help.Err.Should().BeEmpty();

        var typo = await RunAsync(MalformedSettings, "reset-mfa", "--bare-rest");
        typo.Exit.Should().Be(EmergencyExitCodes.InvalidUsage, typo.Err);
        typo.Err.Should().Contain("--bare-rest").And.NotContain("InvalidDataException");
    }

    [Fact]
    public async Task A_well_formed_settings_file_starts_normally()
    {
        // No database is named, so the commands are the ones settled by usage alone; this is the control that the broken file, not the harness, caused the failures above.
        var help = await RunAsync("{}", "reset-mfa", "--help");
        help.Exit.Should().Be(EmergencyExitCodes.Success, help.Err);
        help.Out.Should().Contain("reset-mfa").And.Contain("--bare-reset");

        var overview = await RunAsync("{}", "--help");
        overview.Exit.Should().Be(EmergencyExitCodes.Success, overview.Err);

        var bothAccounts = await RunAsync("{}", "reset-mfa", "--user-id", "a", "--username", "b");
        bothAccounts.Exit.Should().Be(EmergencyExitCodes.InvalidUsage, bothAccounts.Err);
    }
}
