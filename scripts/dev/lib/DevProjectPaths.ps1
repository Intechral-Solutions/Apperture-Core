# Canonical dev project paths, ports, and launch profiles.
Set-StrictMode -Version Latest

function Get-DevProjectPaths {
    param([Parameter(Mandatory)][string]$RepoRoot)

    return [pscustomobject]@{
        RepoRoot          = $RepoRoot
        Solution          = Join-Path $RepoRoot 'Apperture.slnx'
        HostProject       = Join-Path $RepoRoot 'src\Apperture.ControlPlane\Apperture.Management.Host\Apperture.Management.Host.csproj'
        UiServerProject   = Join-Path $RepoRoot 'src\Apperture.ControlPlane\Apperture.Management.Ui.Server\Apperture.Management.Ui.Server.csproj'
        WorkerHostProject = Join-Path $RepoRoot 'src\Apperture.RuntimeHost\Apperture.RuntimeHost.Windows\Apperture.RuntimeHost.Windows.WorkerHost\Apperture.RuntimeHost.Windows.WorkerHost.csproj'
        AppHostProject    = Join-Path $RepoRoot 'src\Apperture.AppHost\Apperture.AppHost.csproj'
        UiClientDir       = Join-Path $RepoRoot 'src\Apperture.ControlPlane\Apperture.Management.Ui.Client'
        ApiPort           = 9000
        UiPort            = 9001
        SqlPort           = 1433
        HostLaunchProfile = 'http'
        UiLaunchProfile   = 'Ui.Server'
        SqlContainerName  = 'apperture-sqlserver'
        SqlServiceName    = 'sqlserver'
        DatabaseName      = 'Apperture'
    }
}

function Test-DevProjectPaths {
    param(
        [Parameter(Mandatory)]
        $Paths,
        [string[]]$Required = @('HostProject', 'UiServerProject', 'WorkerHostProject', 'AppHostProject')
    )

    foreach ($name in $Required) {
        $path = $Paths.$name
        if (-not (Test-Path -LiteralPath $path)) {
            throw "$name not found: $path"
        }
    }
}
