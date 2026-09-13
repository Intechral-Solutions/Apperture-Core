# Management UI CSS pipeline (Tailwind standalone CLI - no Node/npm).
#
#   build-ui  -> one-shot compile styles/app.css  => wwwroot/css/app.css
#   watch-ui  -> same compile in --watch mode (optional, second terminal)
#
# The generated stylesheet stays committed, so `dotnet build`, publish, and CI
# keep working without ever downloading Tailwind.
Set-StrictMode -Version Latest

# Pinned to the version recorded in the existing generated stylesheet banner.
# Bumping this requires refreshing the hashes below from the release's
# sha256sums.txt and re-running build-ui.
$script:AppertureTailwindVersion = '4.1.8'

# SHA256 values published at
# https://github.com/tailwindlabs/tailwindcss/releases/download/v4.1.8/sha256sums.txt
$script:AppertureTailwindHashes = @{
    'tailwindcss-windows-x64.exe' = 'c9117f6f8d9d9f8bf06f4575c505519f0484577d65c96f14b3c2fa61248bf11e'
    'tailwindcss-linux-x64'       = '8f84ce810bdff225e599781d1e2daa82b4282229021c867a71b419f59f9aa836'
    'tailwindcss-linux-arm64'     = '28a77d1e59b0e45b41683c1e3947621fdfe73f6895b05db7c34f63f3f4898e8d'
    'tailwindcss-macos-x64'       = '4a6cb260d75c4bdca0724fbcc3b23a5adb52715ad6d78595463c86128ca1c329'
    'tailwindcss-macos-arm64'     = '19e52791d356dd59db68274ae36a5879bab0ce9dac23cc7b0f19fc7b7c1d37a2'
}

function Get-UiClientDirectory {
    param([Parameter(Mandatory)][string]$RepoRoot)
    return (Join-Path $RepoRoot 'src\Apperture.ControlPlane\Apperture.Management.Ui.Client')
}

function Get-UiAssetPaths {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $uiDir = Get-UiClientDirectory -RepoRoot $RepoRoot
    return [pscustomobject]@{
        ClientDirectory = $uiDir
        StylesDirectory = (Join-Path $uiDir 'styles')
        Entry           = (Join-Path $uiDir 'styles\app.css')
        Theme           = (Join-Path $uiDir 'styles\theme.css')
        Output          = (Join-Path $uiDir 'wwwroot\css\app.css')
        Primitives      = (Join-Path $uiDir 'wwwroot\css\management-ui-primitives.css')
    }
}

function Get-AppertureTailwindAsset {
    # Windows PowerShell 5.1 has no $IsWindows/$IsLinux/$IsMacOS automatic variables.
    $onWindows = if (Test-Path variable:IsWindows) { $IsWindows } else { $true }
    $onLinux = if (Test-Path variable:IsLinux) { $IsLinux } else { $false }
    $onMac = if (Test-Path variable:IsMacOS) { $IsMacOS } else { $false }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()

    $name = $null
    if ($onWindows -and $arch -eq 'X64') { $name = 'tailwindcss-windows-x64.exe' }
    elseif ($onLinux -and $arch -eq 'X64') { $name = 'tailwindcss-linux-x64' }
    elseif ($onLinux -and $arch -eq 'Arm64') { $name = 'tailwindcss-linux-arm64' }
    elseif ($onMac -and $arch -eq 'X64') { $name = 'tailwindcss-macos-x64' }
    elseif ($onMac -and $arch -eq 'Arm64') { $name = 'tailwindcss-macos-arm64' }

    if (-not $name) {
        throw ("No pinned Tailwind $($script:AppertureTailwindVersion) binary for this platform " +
            "(arch '$arch'). Add the asset name and its sha256 to " +
            'scripts/lib/Apperture.UiAssets.ps1, or run build-ui on a supported platform. ' +
            'Note: musl-based Linux needs the -musl asset.')
    }

    return [pscustomobject]@{
        Name   = $name
        Sha256 = $script:AppertureTailwindHashes[$name]
        Uri    = "https://github.com/tailwindlabs/tailwindcss/releases/download/v$($script:AppertureTailwindVersion)/$name"
    }
}

function Save-AppertureTailwindBinary {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Destination
    )

    # Stream to disk: the release assets are >100 MB and Invoke-WebRequest
    # buffers the whole body in memory on Windows PowerShell.
    $client = [System.Net.Http.HttpClient]::new()
    try {
        $client.Timeout = [TimeSpan]::FromMinutes(10)
        $response = $client.GetAsync($Uri, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if (-not $response.IsSuccessStatusCode) {
                throw "HTTP $([int]$response.StatusCode) $($response.ReasonPhrase)"
            }
            $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            $target = [System.IO.File]::Create($Destination)
            try { $source.CopyTo($target) }
            finally { $target.Dispose(); $source.Dispose() }
        }
        finally { $response.Dispose() }
    }
    finally { $client.Dispose() }
}

function Get-AppertureTailwindCli {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $asset = Get-AppertureTailwindAsset
    $cacheDir = Join-Path $RepoRoot "scripts\.runtime\tailwind\$($script:AppertureTailwindVersion)"
    $cliPath = Join-Path $cacheDir $asset.Name

    if (Test-Path -LiteralPath $cliPath) {
        $existing = (Get-FileHash -LiteralPath $cliPath -Algorithm SHA256).Hash
        if ($existing -ieq $asset.Sha256) { return $cliPath }

        Write-Host "Cached Tailwind CLI failed checksum verification; re-downloading." -ForegroundColor Yellow
        Remove-Item -LiteralPath $cliPath -Force
    }

    if (-not (Test-Path -LiteralPath $cacheDir)) {
        New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    }

    Write-Host "Downloading Tailwind CLI $($script:AppertureTailwindVersion) ($($asset.Name))..." -ForegroundColor Cyan
    Write-Host "  $($asset.Uri)" -ForegroundColor DarkGray

    $temp = "$cliPath.download"
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }

    try {
        Save-AppertureTailwindBinary -Uri $asset.Uri -Destination $temp
    }
    catch {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
        throw ("Failed to download the Tailwind CLI from $($asset.Uri): $($_.Exception.Message). " +
            'Check network/proxy access to github.com, or place a verified ' +
            "$($asset.Name) at $cliPath manually.")
    }

    $actual = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash
    if ($actual -ine $asset.Sha256) {
        Remove-Item -LiteralPath $temp -Force
        throw ("Tailwind CLI checksum mismatch for $($asset.Name).`n" +
            "  expected: $($asset.Sha256)`n" +
            "  actual:   $actual`n" +
            'Refusing to use the download. Verify the release assets before retrying.')
    }

    Move-Item -LiteralPath $temp -Destination $cliPath -Force

    if ((Test-Path variable:IsWindows) -and -not $IsWindows) {
        & chmod +x $cliPath
    }

    Write-Host "Tailwind CLI ready: $cliPath" -ForegroundColor Green
    return $cliPath
}

function Invoke-UiAssetBuild {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [switch]$Watch,
        [string]$OutputPath
    )

    $paths = Get-UiAssetPaths -RepoRoot $RepoRoot

    foreach ($required in @($paths.Entry, $paths.Theme)) {
        if (-not (Test-Path -LiteralPath $required)) {
            throw ("Management UI stylesheet source is missing: $required. " +
                'The authored Tailwind sources must exist before CSS can be generated.')
        }
    }

    $output = if ($OutputPath) { $OutputPath } else { $paths.Output }
    $outputDir = Split-Path -Parent $output
    if ($outputDir -and -not (Test-Path -LiteralPath $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    $cli = Get-AppertureTailwindCli -RepoRoot $RepoRoot

    $cliArgs = @('--input', $paths.Entry, '--output', $output)
    # 'always' keeps watching when stdin is not an interactive console (the plain
    # --watch form exits on stdin EOF). Ctrl+C still stops it.
    if ($Watch) { $cliArgs += '--watch=always' }

    Write-Host ''
    Write-Host "Tailwind $($script:AppertureTailwindVersion) - Management UI CSS" -ForegroundColor Cyan
    Write-Host "  input:  $($paths.Entry)" -ForegroundColor DarkGray
    Write-Host "  output: $output" -ForegroundColor DarkGray

    if ($Watch) {
        Write-Host '  mode:   watch (Ctrl+C to stop)' -ForegroundColor DarkGray
        Write-Host ''
        Write-Host 'This watcher only rebuilds CSS. It does not start, stop, or restart Aspire,' -ForegroundColor DarkGray
        Write-Host 'the Management API, SQL, or the worker. Refresh the browser to pick up changes.' -ForegroundColor DarkGray
    }
    else {
        Write-Host '  mode:   one-shot' -ForegroundColor DarkGray
    }
    Write-Host ''

    # Run from the client project so relative @source globs resolve predictably.
    Push-Location $paths.ClientDirectory
    try {
        & $cli @cliArgs
        $exit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($Watch) {
        # Ctrl+C surfaces as a non-zero exit from the watcher; that is a normal stop.
        Write-Host 'Tailwind watch stopped.' -ForegroundColor Yellow
        return
    }

    if ($exit -ne 0) {
        throw "Tailwind CSS generation failed (exit code $exit). The existing $output was not updated by this run."
    }

    if (-not (Test-Path -LiteralPath $output)) {
        throw "Tailwind reported success but no output was written to $output."
    }

    $size = [math]::Round((Get-Item -LiteralPath $output).Length / 1KB, 1)
    Write-Host "Generated $output ($size KB)." -ForegroundColor Green
}
