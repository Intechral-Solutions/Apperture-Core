# Headless dotnet service launch with logs and PID files.
Set-StrictMode -Version Latest

function Start-DevServiceLogged {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$DotNetExe,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$PidServiceName
    )

    $logDir = Get-AppertureDevLogDir
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
    $safeName = $Name.ToLowerInvariant() -replace '[^a-z0-9]+', '-'
    $stdout = Join-Path $logDir "$safeName-$stamp.out.log"
    $stderr = Join-Path $logDir "$safeName-$stamp.err.log"

    Write-Host "Starting $Name..." -ForegroundColor Cyan
    Write-Host "  stdout: $stdout" -ForegroundColor DarkGray
    Write-Host "  stderr: $stderr" -ForegroundColor DarkGray

    $process = Start-Process -FilePath $DotNetExe -ArgumentList $Arguments -WorkingDirectory $RepoRoot -WindowStyle Hidden `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

    Write-DevServicePid -ServiceName $PidServiceName -ProcessId $process.Id

    return [pscustomobject]@{
        Name    = $Name
        Process = $process
        StdOut  = $stdout
        StdErr  = $stderr
    }
}

function Start-DevNativeApi {
    param(
        [Parameter(Mandatory)]$Paths,
        [Parameter(Mandatory)][string]$DotNetExe,
        [switch]$Build
    )

    if ($Build) {
        Write-Host 'Building Management.Host...' -ForegroundColor Cyan
        & $DotNetExe build $Paths.HostProject --no-incremental
        if ($LASTEXITCODE -ne 0) { throw 'Management.Host build failed' }
    }

    $hostArgs = @('run', '--no-build', '--project', $Paths.HostProject, '--launch-profile', $Paths.HostLaunchProfile)
    if (-not $Build) {
        $hostArgs = @('run', '--project', $Paths.HostProject, '--launch-profile', $Paths.HostLaunchProfile, '--no-build')
    }

    $launch = Start-DevServiceLogged -Name 'Management Host' -DotNetExe $DotNetExe -RepoRoot $Paths.RepoRoot `
        -Arguments $hostArgs -PidServiceName 'api'

    Wait-DevServiceListening -Port $Paths.ApiPort -ServiceName 'Management Host' -ExitCheck {
        if ($launch.Process.HasExited) {
            throw "Management Host exited during startup (exit code: $($launch.Process.ExitCode)). See logs: $($launch.StdOut) ; $($launch.StdErr)"
        }
    }
}

function Start-DevNativeUi {
    param(
        [Parameter(Mandatory)]$Paths,
        [Parameter(Mandatory)][string]$DotNetExe,
        [switch]$Build,
        [switch]$NoBrowser
    )

    if ($Build) {
        Write-Host 'Building Management UI...' -ForegroundColor Cyan
        & $DotNetExe build $Paths.UiServerProject --no-incremental
        if ($LASTEXITCODE -ne 0) { throw 'Management UI build failed' }
    }

    $uiArgs = @('run', '--project', $Paths.UiServerProject, '--launch-profile', $Paths.UiLaunchProfile, '--no-build')
    if (-not $Build) {
        $uiArgs = @('run', '--project', $Paths.UiServerProject, '--launch-profile', $Paths.UiLaunchProfile, '--no-build')
    }
    if ($NoBrowser) {
        $uiArgs += @('--urls', "http://localhost:$($Paths.UiPort)")
    }

    $launch = Start-DevServiceLogged -Name 'Management UI' -DotNetExe $DotNetExe -RepoRoot $Paths.RepoRoot `
        -Arguments $uiArgs -PidServiceName 'ui'

    Wait-DevServiceListening -Port $Paths.UiPort -ServiceName 'Management UI' -ExitCheck {
        if ($launch.Process.HasExited) {
            throw "Management UI exited during startup (exit code: $($launch.Process.ExitCode)). See logs: $($launch.StdOut) ; $($launch.StdErr)"
        }
    }
}

function Start-DevNativeWorker {
    param(
        [Parameter(Mandatory)]$Paths,
        [Parameter(Mandatory)][string]$DotNetExe,
        [string]$SaPassword = ''
    )

    $statePath = Get-DevWorkerStatePath
    $logDir = Get-AppertureDevLogDir
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
    $logOut = Join-Path $logDir "worker-host-$stamp.out.log"
    $logErr = Join-Path $logDir "worker-host-$stamp.err.log"

    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    if (-not [string]::IsNullOrWhiteSpace($SaPassword)) {
        $env:SqlServer__Password = $SaPassword
    }

    $runArgs = @('run', '--project', $Paths.WorkerHostProject)
    Write-Host 'Starting testing worker runtime...' -ForegroundColor Cyan

    $proc = Start-Process -FilePath $DotNetExe -ArgumentList $runArgs -WorkingDirectory $Paths.RepoRoot -WindowStyle Hidden `
        -RedirectStandardOutput $logOut -RedirectStandardError $logErr -PassThru

    Start-Sleep -Seconds 5
    if ($proc.HasExited) {
        $msg = "Worker exited during startup (exit code: $($proc.ExitCode)). See logs: $logOut ; $logErr"
        throw $msg
    }

    Write-DevServicePid -ServiceName 'worker' -ProcessId $proc.Id
    $payload = [ordered]@{
        status              = 'Running'
        message             = "Worker running (pid $($proc.Id))."
        pid                 = $proc.Id
        lastStartAttemptUtc = (Get-Date).ToUniversalTime().ToString('o')
        lastUpdatedUtc      = (Get-Date).ToUniversalTime().ToString('o')
        logPath             = $logOut
    }
    ($payload | ConvertTo-Json -Depth 5) | Set-Content -Path $statePath -Encoding UTF8
    Write-Host "Worker running (pid $($proc.Id))." -ForegroundColor Green
}

function Stop-DevNativeStack {
    param([Parameter(Mandatory)]$Paths)

    Stop-DevProcessByPidFile -ServiceName 'api'
    Stop-DevProcessByPidFile -ServiceName 'ui'
    Stop-DevProcessByPidFile -ServiceName 'worker'

    Stop-DevProcessByPort -Port $Paths.ApiPort
    Stop-DevProcessByPort -Port $Paths.UiPort
    Stop-DevDotNetByProject -ProjectPath $Paths.HostProject
    Stop-DevDotNetByProject -ProjectPath $Paths.UiServerProject
    Stop-DevDotNetByProject -ProjectPath $Paths.WorkerHostProject

    Clear-AllDevServicePids
    Clear-DevRuntimeMode
}

function Stop-DevNativeService {
    param(
        [Parameter(Mandatory)]$Paths,
        [Parameter(Mandatory)][ValidateSet('api', 'ui', 'worker')]
        [string]$Service
    )

    switch ($Service) {
        'api' {
            Stop-DevProcessByPidFile -ServiceName 'api'
            Stop-DevProcessByPort -Port $Paths.ApiPort
            Stop-DevDotNetByProject -ProjectPath $Paths.HostProject
            Clear-DevServicePid -ServiceName 'api'
            Wait-DevListenPortsFree -Ports @($Paths.ApiPort)
        }
        'ui' {
            Stop-DevProcessByPidFile -ServiceName 'ui'
            Stop-DevProcessByPort -Port $Paths.UiPort
            Stop-DevDotNetByProject -ProjectPath $Paths.UiServerProject
            Clear-DevServicePid -ServiceName 'ui'
            Wait-DevListenPortsFree -Ports @($Paths.UiPort)
        }
        'worker' {
            Stop-DevProcessByPidFile -ServiceName 'worker'
            Stop-DevDotNetByProject -ProjectPath $Paths.WorkerHostProject
            Clear-DevServicePid -ServiceName 'worker'
        }
    }
}

function Restart-DevNativeService {
    param(
        [Parameter(Mandatory)]$Paths,
        [Parameter(Mandatory)][string]$DotNetExe,
        [Parameter(Mandatory)][ValidateSet('api', 'ui', 'worker')]
        [string]$Service,
        [switch]$Build,
        [switch]$NoBrowser,
        [string]$SaPassword = ''
    )

    Stop-DevNativeService -Paths $Paths -Service $Service

    switch ($Service) {
        'api' { Start-DevNativeApi -Paths $Paths -DotNetExe $DotNetExe -Build:$Build }
        'ui' { Start-DevNativeUi -Paths $Paths -DotNetExe $DotNetExe -Build:$Build -NoBrowser:$NoBrowser }
        'worker' { Start-DevNativeWorker -Paths $Paths -DotNetExe $DotNetExe -SaPassword $SaPassword }
    }
}
