<#
.SYNOPSIS
    Helpers for launching pinned dotnet in a dedicated terminal window (dev scripts).
#>

function Start-DevDotNetInNewTerminal {
    <#
    .SYNOPSIS
        Starts dotnet with the given arguments in a new PowerShell window (reliable when Start-Process dotnet.exe
        is absorbed by the parent terminal host, e.g. some Windows Terminal / IDE integrations).
    #>
    [CmdletBinding()]
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
    if ($pwshCmd) {
        $shell = $pwshCmd.Source
    }
    if (-not $shell) {
        $shell = (Get-Command powershell.exe -ErrorAction Stop).Source
    }

    $wd = Escape-SingleQuoted $WorkingDirectory
    $dn = Escape-SingleQuoted $DotnetExe
    $argQuoted = $DotnetArgumentList | ForEach-Object { "'" + (Escape-SingleQuoted $_) + "'" }
    $argJoined = $argQuoted -join ","

    $tt = Escape-SingleQuoted $WindowTitle
    $command = "Set-Location -LiteralPath '$wd'; try { [Console]::Title = '$tt' } catch { }; & '$dn' @($argJoined)"

    Start-Process -FilePath $shell -ArgumentList @(
        "-NoLogo",
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-NoProfile",
        "-Command",
        $command
    )
}
