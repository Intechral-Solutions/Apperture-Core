<#
.SYNOPSIS
    Starts (or restarts) the Apperture testing worker runtime.
.DESCRIPTION
    Launches WorkerHost using the pinned SDK resolver and writes launch state to:
      artifacts/devlogs/worker-runtime-state.json
    This script is safe to run repeatedly for dev retry/start workflows.
#>
[CmdletBinding()]
param(
    [switch]$Restart,
    [switch]$Headless
)

$ErrorActionPreference = "Stop"
$RepoRoot = if ($env:APPERTURE_REPO_ROOT) { $env:APPERTURE_REPO_ROOT } else { (Get-Item $PSScriptRoot).Parent.Parent.FullName }
$WorkerHostProject = Join-Path $RepoRoot "src\Apperture.RuntimeHost\Apperture.RuntimeHost.Windows\Apperture.RuntimeHost.Windows.WorkerHost\Apperture.RuntimeHost.Windows.WorkerHost.csproj"
$ResolveScript = Join-Path $PSScriptRoot "Get-DevDotNetSdk.ps1"
$LogDir = Join-Path $RepoRoot "artifacts\devlogs"
$StatePath = Join-Path $LogDir "worker-runtime-state.json"
$StartTs = (Get-Date).ToUniversalTime()

if (-not (Test-Path $WorkerHostProject)) {
    throw "Runtime WorkerHost project not found: $WorkerHostProject"
}

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

function Write-State {
    param(
        [string]$Status,
        [string]$Message,
        [int]$ProcessId = 0,
        [string]$LogPath = ""
    )
    $payload = [ordered]@{
        status = $Status
        message = $Message
        pid = if ($ProcessId -gt 0) { $ProcessId } else { $null }
        lastStartAttemptUtc = $StartTs.ToString("o")
        lastUpdatedUtc = (Get-Date).ToUniversalTime().ToString("o")
        logPath = if ([string]::IsNullOrWhiteSpace($LogPath)) { $null } else { $LogPath }
    }
    ($payload | ConvertTo-Json -Depth 5) | Set-Content -Path $StatePath -Encoding UTF8
}

function Stop-ExistingWorker {
    $workers = Get-CimInstance Win32_Process | Where-Object {
        $_.Name -ieq "dotnet.exe" -and $_.CommandLine -and $_.CommandLine.Contains("Apperture.RuntimeHost.Windows.WorkerHost.csproj")
    }
    foreach ($w in $workers) {
        try { Stop-Process -Id $w.ProcessId -Force -ErrorAction Stop } catch { }
    }
}

if ($Restart) {
    Stop-ExistingWorker
    Start-Sleep -Seconds 1
}

$sdk = & $ResolveScript -RepoRoot $RepoRoot
$dotnetExe = $sdk.DotNetExe
$env:DOTNET_ROOT = $sdk.DotNetRoot
$env:PATH = "$($sdk.DotNetRoot);$env:PATH"

$saPassword = $env:SqlServer__Password
if ([string]::IsNullOrWhiteSpace($saPassword)) {
    $envPath = Join-Path $RepoRoot ".env"
    if (Test-Path $envPath) {
        $line = (Get-Content -Path $envPath | Select-String -Pattern "^MSSQL_SA_PASSWORD=").Line
        if ($line) {
            $saPassword = $line.Split("=", 2)[1]
        }
    }
}

$args = @("run", "--project", $WorkerHostProject)
$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$logOut = Join-Path $LogDir "worker-host-$stamp.out.log"
$logErr = Join-Path $LogDir "worker-host-$stamp.err.log"

Write-State -Status "Starting" -Message "Worker launch requested."

$startInfo = @{
    FilePath = $dotnetExe
    ArgumentList = $args
    WorkingDirectory = $RepoRoot
    PassThru = $true
}

if ($Headless) {
    $startInfo["WindowStyle"] = "Hidden"
    $startInfo["RedirectStandardOutput"] = $logOut
    $startInfo["RedirectStandardError"] = $logErr
} else {
    $startInfo["WindowStyle"] = "Normal"
}

$env:DOTNET_ENVIRONMENT = "Development"
$env:ASPNETCORE_ENVIRONMENT = "Development"
if (-not [string]::IsNullOrWhiteSpace($saPassword)) {
    $env:SqlServer__Password = $saPassword
}

$proc = Start-Process @startInfo
Start-Sleep -Seconds 5

if ($proc.HasExited) {
    $msg = "Worker exited during startup (exit code: $($proc.ExitCode))."
    if ($Headless) {
        $msg = "$msg See logs: $logOut ; $logErr"
    }
    Write-State -Status "Failed" -Message $msg -ProcessId $proc.Id -LogPath $logErr
    throw $msg
}

$okMsg = "Worker running (pid $($proc.Id))."
Write-State -Status "Running" -Message $okMsg -ProcessId $proc.Id -LogPath $logOut
Write-Host $okMsg -ForegroundColor Green
