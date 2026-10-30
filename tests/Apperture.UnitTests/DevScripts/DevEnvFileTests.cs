using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.DevScripts;

/// <summary>
/// The development script that syncs the SQL password from User Secrets into the repo-root <c>.env</c> must change that one key
/// and leave everything else alone. Every scenario runs the real function in PowerShell 7 against its own temporary folder; the
/// repository <c>.env</c> is never read or written. All scenarios run in one <c>pwsh</c> process (starting one per case cost about
/// a second each), and each test asserts on its scenario's stored result. PowerShell 7 (<c>pwsh</c>) is required, and a machine
/// without it fails the tests rather than skipping them.
/// </summary>
public sealed class DevEnvFileTests : IClassFixture<DevEnvFileTests.Scenarios>
{
    private const string NewPassword = "New-Pw-Marker-7c1e!";
    private const string OldPassword = "Old-Pw-Marker-3b9d!";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly DateTime Past = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly Scenarios _scenarios;

    public DevEnvFileTests(Scenarios scenarios) => _scenarios = scenarios;

    // ---- Sync-DevEnvFromUserSecrets: the real entry point -----------------------------------------------------------------

    [Fact]
    public void Sync_changes_only_the_password_and_keeps_every_other_setting_comment_and_blank_line_in_order()
    {
        var run = _scenarios.Run("preserve-settings");

        run.Failed.Should().BeFalse(run.Output);
        _scenarios.Text("preserve-settings").Should().Be(Scenarios.Initial("preserve-settings").Replace(OldPassword, NewPassword));
        _scenarios.Text("preserve-settings").Should().Contain("Password=keep-this-1").And.Contain("Password=keep-this-2");
    }

    [Fact]
    public void Sync_never_prints_a_password_or_another_setting()
    {
        var run = _scenarios.Run("no-print");

        run.Failed.Should().BeFalse(run.Output);
        run.Output.Should().NotContain(NewPassword).And.NotContain(OldPassword).And.NotContain("keep-this-3");
    }

    [Fact]
    public void Sync_creates_the_file_with_only_the_password_when_there_is_none()
    {
        var run = _scenarios.Run("create-when-missing");

        run.Failed.Should().BeFalse(run.Output);
        _scenarios.Text("create-when-missing").Should().Be($"MSSQL_SA_PASSWORD={NewPassword}{Environment.NewLine}");
    }

    [Theory]
    [InlineData("append-lf-no-final-newline", "\n")]
    [InlineData("append-lf-final-newline", "\n")]
    [InlineData("append-crlf-final-newline", "\r\n")]
    [InlineData("append-crlf-no-final-newline", "\r\n")]
    public void Sync_appends_the_password_in_the_files_own_line_ending_when_it_is_missing(string id, string newline)
    {
        var run = _scenarios.Run(id);

        run.Failed.Should().BeFalse(run.Output);
        var before = Scenarios.Initial(id);
        var kept = before.EndsWith('\n') ? before : before + newline;
        _scenarios.Text(id).Should().Be(kept + "MSSQL_SA_PASSWORD=" + NewPassword + newline);
    }

    [Fact]
    public void Sync_keeps_crlf_line_endings_on_every_line()
    {
        _scenarios.Run("crlf").Failed.Should().BeFalse();

        _scenarios.Text("crlf").Should().Be($"A=1\r\nMSSQL_SA_PASSWORD={NewPassword}\r\nB=2\r\n");
    }

    [Fact]
    public void Sync_keeps_mixed_line_endings_exactly()
    {
        _scenarios.Run("mixed").Failed.Should().BeFalse();

        _scenarios.Text("mixed").Should().Be($"A=1\r\nMSSQL_SA_PASSWORD={NewPassword}\nB=2\r\n");
    }

    [Fact]
    public void Sync_keeps_a_utf8_byte_order_mark()
    {
        _scenarios.Run("bom").Failed.Should().BeFalse();

        var after = _scenarios.Bytes("bom");
        after.Take(3).Should().Equal(Bom);
        Utf8.GetString(after, 3, after.Length - 3).Should().Be($"A=1\nMSSQL_SA_PASSWORD={NewPassword}\n");
    }

    [Fact]
    public void Sync_does_not_add_a_byte_order_mark_to_a_file_without_one()
    {
        _scenarios.Run("no-bom").Failed.Should().BeFalse();

        _scenarios.Bytes("no-bom").Take(3).Should().NotEqual(Bom);
        _scenarios.Text("no-bom").Should().Be($"A=1\nMSSQL_SA_PASSWORD={NewPassword}\n");
    }

    [Fact]
    public void Sync_updates_every_assignment_of_the_key()
    {
        _scenarios.Run("duplicates").Failed.Should().BeFalse();

        _scenarios.Text("duplicates").Should().Be($"MSSQL_SA_PASSWORD={NewPassword}\nA=1\nMSSQL_SA_PASSWORD={NewPassword}\n");
    }

    [Fact]
    public void Sync_keeps_an_export_prefix_and_indentation()
    {
        _scenarios.Run("export-indent").Failed.Should().BeFalse();

        _scenarios.Text("export-indent").Should().Be($"export MSSQL_SA_PASSWORD={NewPassword}\n  MSSQL_SA_PASSWORD={NewPassword}\n");
    }

    [Fact]
    public void Sync_leaves_comments_and_look_alike_keys_alone()
    {
        _scenarios.Run("lookalikes").Failed.Should().BeFalse();

        _scenarios.Text("lookalikes").Should().Be(Scenarios.Initial("lookalikes") + $"MSSQL_SA_PASSWORD={NewPassword}\n");
    }

    [Fact]
    public void Sync_does_not_rewrite_the_file_when_the_password_is_already_current()
    {
        var run = _scenarios.Run("unchanged");

        run.Failed.Should().BeFalse(run.Output);
        _scenarios.Text("unchanged").Should().Be(Scenarios.Initial("unchanged"));
        File.GetLastWriteTimeUtc(_scenarios.EnvPath("unchanged")).Should().Be(Past);
    }

    [Fact]
    public void Sync_is_idempotent()
    {
        _scenarios.Run("idempotent").Failed.Should().BeFalse();

        _scenarios.Bytes("idempotent").Should().Equal(File.ReadAllBytes(Path.Combine(_scenarios.Dir("idempotent"), "after-first-run.bin")));
    }

    [Theory]
    [InlineData("preserve-settings")]
    [InlineData("refuse-invalid-utf8")]
    public void Sync_leaves_no_temporary_file_behind(string id)
    {
        _scenarios.Run(id);

        _scenarios.EnvFiles(id).Should().Equal(".env");
    }

    // ---- refusals: a file that is unsafe to update is left exactly as it was -------------------------------------------------

    [Theory]
    [InlineData("refuse-invalid-utf8")]
    [InlineData("refuse-nul")]
    [InlineData("refuse-utf16")]
    public void Sync_refuses_a_file_it_cannot_update_safely_and_leaves_it_untouched(string id)
    {
        var run = _scenarios.Run(id);

        run.Failed.Should().BeTrue();
        run.Output.Should().Contain("Refusing");
        _scenarios.Bytes(id).Should().Equal(Scenarios.InitialBytes(id));
    }

    [Theory]
    [InlineData("refuse-value-lf")]
    [InlineData("refuse-value-crlf")]
    [InlineData("refuse-value-cr")]
    [InlineData("refuse-key-space")]
    [InlineData("refuse-key-digit")]
    [InlineData("refuse-key-equals")]
    public void Setting_a_value_with_a_line_break_or_a_key_that_is_not_a_variable_name_is_refused(string id)
    {
        var run = _scenarios.Run(id);

        run.Failed.Should().BeTrue();
        run.Output.Should().Contain("Refusing");
        _scenarios.Bytes(id).Should().Equal(Scenarios.InitialBytes(id));
    }

    [Fact]
    public void Setting_a_key_reports_what_it_did_without_printing_the_value()
    {
        var run = _scenarios.Run("set-added");

        run.Failed.Should().BeFalse(run.Output);
        run.Output.Should().Contain("RESULT:added").And.NotContain("secret-value-xyz");
        _scenarios.Text("set-added").Should().Be("A=1\nB=secret-value-xyz\n");
    }

    // ---- harness -------------------------------------------------------------------------------------------------------------

    public sealed record ScenarioResult(bool Failed, string Output);

    private sealed record Definition(string Id, string Mode, byte[]? Initial, string? Key = null, string? Value = null, bool PastWrite = false, bool Twice = false);

    /// <summary>Builds every scenario folder, runs the PowerShell driver once over all of them, and keeps the results.</summary>
    public sealed class Scenarios : IDisposable
    {
        private static readonly Definition[] Definitions =
        [
            Sync("preserve-settings",
                "# Local settings\n" +
                "APPERTURE_TEST_SQL_CONNECTION_STRING=Server=127.0.0.1,1433;User Id=sa;Password=keep-this-1;TrustServerCertificate=True;Initial Catalog=Apperture;\n" +
                "\n" +
                $"MSSQL_SA_PASSWORD={OldPassword}\n" +
                "APPERTURE_TEST_SQL=Server=ci,1433;Password=keep-this-2;Initial Catalog=Apperture_ManagementGovernanceTests;\n" +
                "OTHER=1\n"),
            Sync("no-print", $"KEEP=keep-this-3\nMSSQL_SA_PASSWORD={OldPassword}\n"),
            new("create-when-missing", "sync", Initial: null),
            Sync("append-lf-no-final-newline", "A=1\nB=2"),
            Sync("append-lf-final-newline", "A=1\nB=2\n"),
            Sync("append-crlf-final-newline", "A=1\r\nB=2\r\n"),
            Sync("append-crlf-no-final-newline", "A=1\r\nB=2"),
            Sync("crlf", $"A=1\r\nMSSQL_SA_PASSWORD={OldPassword}\r\nB=2\r\n"),
            Sync("mixed", $"A=1\r\nMSSQL_SA_PASSWORD={OldPassword}\nB=2\r\n"),
            new("bom", "sync", [.. Bom, .. Utf8.GetBytes($"A=1\nMSSQL_SA_PASSWORD={OldPassword}\n")]),
            Sync("no-bom", $"A=1\nMSSQL_SA_PASSWORD={OldPassword}\n"),
            Sync("duplicates", $"MSSQL_SA_PASSWORD={OldPassword}\nA=1\nMSSQL_SA_PASSWORD=another-old\n"),
            Sync("export-indent", $"export MSSQL_SA_PASSWORD={OldPassword}\n  MSSQL_SA_PASSWORD={OldPassword}\n"),
            Sync("lookalikes", $"# MSSQL_SA_PASSWORD={OldPassword}\nMSSQL_SA_PASSWORD_OLD=x\nXMSSQL_SA_PASSWORD=y\nmssql_sa_password=z\n"),
            new("unchanged", "sync", Utf8.GetBytes($"A=1\nMSSQL_SA_PASSWORD={NewPassword}\n"), PastWrite: true),
            new("idempotent", "sync", Utf8.GetBytes($"A=1\nMSSQL_SA_PASSWORD={OldPassword}\n"), Twice: true),
            new("refuse-invalid-utf8", "sync", [0x41, 0x3D, 0x80, 0x0A]),
            new("refuse-nul", "sync", [0x41, 0x3D, 0x31, 0x00, 0x0A]),
            new("refuse-utf16", "sync", [0xFF, 0xFE, 0x41, 0x00, 0x3D, 0x00, 0x31, 0x00]),
            Set("refuse-value-lf", "MSSQL_SA_PASSWORD", "abc\ndef"),
            Set("refuse-value-crlf", "MSSQL_SA_PASSWORD", "abc\r\ndef"),
            Set("refuse-value-cr", "MSSQL_SA_PASSWORD", "abc\rdef"),
            Set("refuse-key-space", "BAD KEY", "v"),
            Set("refuse-key-digit", "1BAD", "v"),
            Set("refuse-key-equals", "A=B", "v"),
            new("set-added", "set", Utf8.GetBytes("A=1\n"), Key: "B", Value: "secret-value-xyz"),
        ];

        private static Definition Sync(string id, string initial) => new(id, "sync", Utf8.GetBytes(initial));

        private static Definition Set(string id, string key, string value) =>
            new(id, "set", Utf8.GetBytes("A=1\nB=2\n"), key, value);

        private readonly string _root = Path.Combine(Path.GetTempPath(), "apperture-devenv-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, ScenarioResult> _results = new(StringComparer.Ordinal);

        public Scenarios()
        {
            Directory.CreateDirectory(_root);
            var manifest = new List<Dictionary<string, object?>>();
            foreach (var definition in Definitions)
            {
                var dir = Dir(definition.Id);
                Directory.CreateDirectory(dir);
                if (definition.Initial is not null)
                {
                    File.WriteAllBytes(EnvPath(definition.Id), definition.Initial);
                    if (definition.PastWrite)
                        File.SetLastWriteTimeUtc(EnvPath(definition.Id), Past);
                }

                manifest.Add(new Dictionary<string, object?>
                {
                    ["id"] = definition.Id,
                    ["mode"] = definition.Mode,
                    ["dir"] = dir,
                    ["key"] = definition.Key,
                    ["value"] = definition.Value ?? NewPassword,
                    ["twice"] = definition.Twice,
                });
            }

            var manifestPath = Path.Combine(_root, "manifest.json");
            var driverPath = Path.Combine(_root, "driver.ps1");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
            File.WriteAllText(driverPath, Driver);

            RunPowerShell(driverPath, manifestPath);

            foreach (var definition in Definitions)
            {
                var path = Path.Combine(Dir(definition.Id), "result.json");
                File.Exists(path).Should().BeTrue($"the driver must record a result for '{definition.Id}'");
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                _results[definition.Id] = new ScenarioResult(
                    json.RootElement.GetProperty("failed").GetBoolean(),
                    json.RootElement.GetProperty("output").GetString() ?? string.Empty);
            }
        }

        public static string Initial(string id) => Utf8.GetString(InitialBytes(id));

        public static byte[] InitialBytes(string id) => Definitions.Single(d => d.Id == id).Initial!;

        public ScenarioResult Run(string id) => _results[id];

        public string Dir(string id) => Path.Combine(_root, id);

        public string EnvPath(string id) => Path.Combine(Dir(id), ".env");

        public byte[] Bytes(string id) => File.ReadAllBytes(EnvPath(id));

        public string Text(string id) => Utf8.GetString(Bytes(id));

        /// <summary>Every file in the scenario folder whose name starts with <c>.env</c>: the file itself and any temporary sibling.</summary>
        public IEnumerable<string> EnvFiles(string id) =>
            Directory.EnumerateFiles(Dir(id)).Select(f => Path.GetFileName(f)!).Where(n => n.StartsWith(".env", StringComparison.Ordinal)).Order();

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // best effort; the folder is in the OS temp directory
            }
        }

        private static void RunPowerShell(string driverPath, string manifestPath)
        {
            var start = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", driverPath, "-Lib", Library(), "-Manifest", manifestPath })
                start.ArgumentList.Add(arg);

            Process process;
            try
            {
                process = Process.Start(start)!;
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException("PowerShell 7 (pwsh) must be on PATH to run the development-script tests.", ex);
            }

            using (process)
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(milliseconds: 120_000))
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException("pwsh did not finish the development-script scenarios within 120 s.");
                }

                process.ExitCode.Should().Be(0, "the driver itself must not fail: " + stdout.Result + stderr.Result);
            }
        }

        private static string Library()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Apperture.slnx")))
                    return Path.Combine(dir.FullName, "scripts", "dev", "lib", "DevDockerSql.ps1");
            }

            throw new InvalidOperationException("Repository root (Apperture.slnx) not found above " + AppContext.BaseDirectory);
        }

        /// <summary>
        /// Runs each scenario in its own folder and records <c>result.json</c> (failed flag and everything the scenario wrote to any
        /// stream). 'set' calls the env-file function directly. 'sync' builds a fake APPDATA with a User Secrets file and a fake repo
        /// root (the function only checks that the Management.Host project file exists) and calls the real entry point, so the dotnet
        /// CLI is never run. Paths use the same backslash expressions as the function itself so the cases hold on every platform.
        /// </summary>
        private const string Driver = """
            param([string]$Lib, [string]$Manifest)
            $ErrorActionPreference = 'Stop'
            . $Lib
            $items = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
            foreach ($item in $items) {
                $failed = $false
                $output = ''
                try {
                    $output = & {
                        $envPath = Join-Path $item.dir '.env'
                        if ($item.mode -eq 'set') {
                            $r = Set-DevEnvFileValue -Path $envPath -Key $item.key -Value $item.value
                            "RESULT:$r"
                            return
                        }

                        $env:APPDATA = Join-Path $item.dir 'appdata'
                        $secrets = Join-Path $env:APPDATA "Microsoft\UserSecrets\a1b2c3d4-5e6f-4a5b-8c9d-0e1f2a3b4c5d\secrets.json"
                        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $secrets) | Out-Null
                        Set-Content -LiteralPath $secrets -Value (@{ 'SqlServer:Password' = $item.value } | ConvertTo-Json)
                        $project = Join-Path $item.dir 'src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj'
                        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $project) | Out-Null
                        New-Item -ItemType File -Force -Path $project | Out-Null
                        $pw = Sync-DevEnvFromUserSecrets -RepoRoot $item.dir -DotNetExe 'dotnet-must-not-run'
                        if ($item.twice) {
                            Copy-Item -LiteralPath $envPath -Destination (Join-Path $item.dir 'after-first-run.bin')
                            $pw = Sync-DevEnvFromUserSecrets -RepoRoot $item.dir -DotNetExe 'dotnet-must-not-run'
                        }
                        "RETURNED-LENGTH:$($pw.Length)"
                    } *>&1 | Out-String
                }
                catch {
                    $failed = $true
                    $output = $_.Exception.Message
                }

                @{ failed = $failed; output = $output } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $item.dir 'result.json')
            }
            """;
    }
}
