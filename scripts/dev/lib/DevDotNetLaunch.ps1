# Helpers for launching pinned dotnet in a dedicated terminal window.
Set-StrictMode -Version Latest

function Start-DevDotNetInNewTerminal {
    param(
        [Parameter(Mandatory)][string]$WindowTitle,
        [Parameter(Mandatory)][string]$DotnetExe,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string[]]$DotnetArgumentList
    )

    if (-not (Test-Path -LiteralPath $DotnetExe)) {
        throw "dotnet executable not found: $DotnetExe"
    }
    if (-not (Test-Path -LiteralPath $WorkingDirectory)) {
        throw "Working directory not found: $WorkingDirectory"
    }

    function Escape-SingleQuoted {
        param([string]$Value)
        $Value -replace "'", "''"
    }

    $shell = $null
    $pwshCmd = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwshCmd) { $shell = $pwshCmd.Source }
    if (-not $shell) {
        $shell = (Get-Command powershell.exe -ErrorAction Stop).Source
    }

    $wd = Escape-SingleQuoted $WorkingDirectory
    $dn = Escape-SingleQuoted $DotnetExe
    $argQuoted = $DotnetArgumentList | ForEach-Object { "'" + (Escape-SingleQuoted $_) + "'" }
    $argJoined = $argQuoted -join ','
    $tt = Escape-SingleQuoted $WindowTitle
    $command = "Set-Location -LiteralPath '$wd'; try { [Console]::Title = '$tt' } catch { }; & '$dn' @($argJoined)"

    Start-Process -FilePath $shell -ArgumentList @(
        '-NoLogo',
        '-NoExit',
        '-ExecutionPolicy', 'Bypass',
        '-NoProfile',
        '-Command',
        $command
    )
}

function Start-DevAspireAppHost {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$DotNetExe,
        [Parameter(Mandatory)][string]$AppHostProject
    )

    $runArgs = @('run', '--project', $AppHostProject)
    Start-DevDotNetInNewTerminal -WindowTitle 'Apperture AppHost (Aspire)' -DotnetExe $DotNetExe -WorkingDirectory $RepoRoot -DotnetArgumentList $runArgs

    Start-Sleep -Seconds 3
    $procs = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -and $_.CommandLine -like '*Apperture.AppHost.csproj*' } |
        Sort-Object CreationDate -Descending |
        Select-Object -First 1

    if ($procs) {
        Write-DevServicePid -ServiceName 'aspire' -ProcessId $procs.ProcessId
        Write-Host "Aspire AppHost started in a new terminal (PID $($procs.ProcessId))." -ForegroundColor Green
    }
    else {
        Write-Host 'Aspire AppHost launched in a new terminal (PID not detected yet).' -ForegroundColor Yellow
    }
}
