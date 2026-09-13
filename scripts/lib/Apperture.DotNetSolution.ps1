# Solution-wide dotnet restore/build/clean/rebuild/publish helpers.
Set-StrictMode -Version Latest

function Get-AppertureSolutionPath {
    param([Parameter(Mandatory)][string]$RepoRoot)
    return (Join-Path $RepoRoot 'Apperture.slnx')
}

function Invoke-AppertureDotNetRestore {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$DotNetExe,
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration = 'Debug'
    )

    $solution = Get-AppertureSolutionPath -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $solution)) {
        throw "Solution not found: $solution"
    }

    Write-Host 'Restoring solution...' -ForegroundColor Cyan
    Push-Location $RepoRoot
    try {
        & $DotNetExe restore $solution
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
        Write-Host 'Restore succeeded.' -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}

function Invoke-AppertureDotNetBuild {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$DotNetExe,
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration = 'Debug',
        [switch]$NoRestore
    )

    $solution = Get-AppertureSolutionPath -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $solution)) {
        throw "Solution not found: $solution"
    }

    Push-Location $RepoRoot
    try {
        if (-not $NoRestore) {
            & $DotNetExe restore $solution
            if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
        }

        Write-Host "Building solution ($Configuration)..." -ForegroundColor Cyan
        & $DotNetExe build $solution -c $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
        Write-Host 'Build succeeded.' -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}

function Invoke-AppertureDotNetClean {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$DotNetExe,
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration = 'Debug'
    )

    $solution = Get-AppertureSolutionPath -RepoRoot $RepoRoot
    Write-Host "Cleaning solution ($Configuration)..." -ForegroundColor Cyan
    Push-Location $RepoRoot
    try {
        & $DotNetExe clean $solution -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Clean failed.' }
        Write-Host 'Clean succeeded.' -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}

function Invoke-AppertureDotNetRebuild {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$DotNetExe,
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration = 'Debug'
    )

    Invoke-AppertureDotNetClean -RepoRoot $RepoRoot -DotNetExe $DotNetExe -Configuration $Configuration
    Invoke-AppertureDotNetRestore -RepoRoot $RepoRoot -DotNetExe $DotNetExe -Configuration $Configuration
    Invoke-AppertureDotNetBuild -RepoRoot $RepoRoot -DotNetExe $DotNetExe -Configuration $Configuration -NoRestore
}

function Invoke-AppertureDotNetPublish {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$DotNetExe,
        [Parameter(Mandatory)][string]$ProjectPath,
        [ValidateSet('Debug', 'Release')]
        [string]$Configuration = 'Release',
        [string]$OutputPath = ''
    )

    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        throw "Project not found: $ProjectPath"
    }

    $args = @('publish', $ProjectPath, '-c', $Configuration)
    if ($OutputPath) {
        $args += @('-o', $OutputPath)
    }

    Write-Host "Publishing $ProjectPath ($Configuration)..." -ForegroundColor Cyan
    Push-Location $RepoRoot
    try {
        & $DotNetExe @args
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Write-Host 'Publish succeeded.' -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}
