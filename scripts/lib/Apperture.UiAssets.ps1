# Shared UI asset build/watch scaffold.
Set-StrictMode -Version Latest

function Get-UiClientDirectory {
    param([Parameter(Mandatory)][string]$RepoRoot)
    return (Join-Path $RepoRoot 'src\Apperture.ControlPlane\Apperture.Management.Ui.Client')
}

function Invoke-UiAssetBuild {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [switch]$Watch
    )

    $uiDir = Get-UiClientDirectory -RepoRoot $RepoRoot
    $packageJson = Join-Path $uiDir 'package.json'

    if (-not (Test-Path -LiteralPath $packageJson)) {
        Write-Host 'UI asset pipeline not configured yet.' -ForegroundColor Yellow
        Write-Host "  Expected: $packageJson" -ForegroundColor DarkGray
        Write-Host '  TODO: add package.json with npm scripts css:build and css:watch' -ForegroundColor DarkGray
        Write-Host '  Output target: wwwroot/css/app.css' -ForegroundColor DarkGray
        if ($Watch) {
            Write-Host '  Watch mode will run: npm run css:watch' -ForegroundColor DarkGray
        }
        else {
            Write-Host '  Build mode will run: npm run css:build' -ForegroundColor DarkGray
        }
        return
    }

    $npm = Get-Command npm -ErrorAction SilentlyContinue
    if (-not $npm) {
        throw 'npm is not on PATH. Install Node.js to build UI assets.'
    }

    $scriptName = if ($Watch) { 'css:watch' } else { 'css:build' }
    Push-Location $uiDir
    try {
        Write-Host "Running npm run $scriptName..." -ForegroundColor Cyan
        & npm run $scriptName
        if ($LASTEXITCODE -ne 0) { throw "npm run $scriptName failed." }
        Write-Host 'UI asset command completed.' -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}
