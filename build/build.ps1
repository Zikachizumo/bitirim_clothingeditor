<#
.SYNOPSIS
    Builds Bitirim Clothing Creator: executable, portable zip and installer.

.DESCRIPTION
    Produces a self-contained application that needs nothing preinstalled --
    no .NET, no Python, no Node. The .NET runtime is published into the app
    folder and the Python runtime is copied in beside it.

    The only external requirement is the Microsoft Edge WebView2 Runtime, which
    ships with Windows 11 and current Windows 10; the installer checks for it.

.PARAMETER PythonRuntime
    A runtime prepared by tools/bootstrap-runtime.ps1. It is trimmed on the way
    in: pip and __pycache__ are dropped, numpy's test suite with them.

.EXAMPLE
    ./build/build.ps1 -PythonRuntime C:\bcc\python
#>
[CmdletBinding()]
param(
    [string]$PythonRuntime = 'C:\bcc\python',
    [string]$Output = 'C:\bcc\release',
    [string]$Configuration = 'Release',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root "src/Bitirim.Clothing.Desktop/Bitirim.Clothing.Desktop.csproj"))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

function Step($message) { Write-Host "`n=== $message" -ForegroundColor Green }

# --------------------------------------------------------------------------
Step 'Building the user interface'
Push-Location (Join-Path $root 'src/ui')
try {
    if (-not (Test-Path 'node_modules')) { & npm install --no-audit --no-fund }
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'UI build failed.' }
}
finally { Pop-Location }

# --------------------------------------------------------------------------
Step 'Publishing the desktop host'
$appDir = Join-Path $Output 'app'
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }

# Self-contained so the user never installs a .NET runtime. Not single-file:
# the app ships a runtime folder anyway, and a plain folder is far easier to
# diagnose in the field.
& dotnet publish (Join-Path $root 'src/Bitirim.Clothing.Desktop') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:Version=$version -p:DebugType=none `
    -o $appDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }

# --------------------------------------------------------------------------
Step 'Staging the web UI'
$webui = Join-Path $appDir 'webui'
New-Item -ItemType Directory -Force -Path $webui | Out-Null
Copy-Item (Join-Path $root 'src/ui/dist/*') $webui -Recurse -Force

# --------------------------------------------------------------------------
Step 'Staging the asset service'
$serviceDir = Join-Path $appDir 'runtime/assetservice'
New-Item -ItemType Directory -Force -Path $serviceDir | Out-Null
Get-ChildItem (Join-Path $root 'src/assetservice') -Filter '*.py' |
    Copy-Item -Destination $serviceDir -Force

# --------------------------------------------------------------------------
Step 'Staging the Python runtime'
if (-not (Test-Path (Join-Path $PythonRuntime 'python.exe'))) {
    throw "No Python runtime at $PythonRuntime. Run tools/bootstrap-runtime.ps1 first."
}

$pythonDest = Join-Path $appDir 'runtime/python'
Copy-Item $PythonRuntime $pythonDest -Recurse -Force

# Trim what a shipped build never uses. trimesh/resources must stay: fivefury
# imports it at load time even though we never touch mesh import.
foreach ($pattern in @('Lib/site-packages/pip', 'Lib/site-packages/pip-*',
                       'Lib/site-packages/numpy/tests', 'Lib/site-packages/numpy/_core/tests')) {
    Get-ChildItem (Join-Path $pythonDest $pattern) -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem $pythonDest -Recurse -Directory -Filter '__pycache__' -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "  runtime: $([math]::Round((Get-ChildItem $pythonDest -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)) MB"

# --------------------------------------------------------------------------
Step 'Verifying the staged build'
& (Join-Path $pythonDest 'python.exe') -c "import fivefury; print('  fivefury import OK')"
if ($LASTEXITCODE -ne 0) { throw 'The staged Python runtime cannot import fivefury.' }

foreach ($required in @('BitirimClothingCreator.exe', 'webui/index.html',
                        'runtime/assetservice/main.py', 'runtime/python/python.exe')) {
    if (-not (Test-Path (Join-Path $appDir $required))) { throw "Missing from build: $required" }
}
$size = [math]::Round((Get-ChildItem $appDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "  application: $size MB"

# --------------------------------------------------------------------------
Step 'Creating the portable package'
$portableStage = Join-Path $Output 'portable/BitirimClothingCreator'
if (Test-Path (Split-Path $portableStage)) { Remove-Item (Split-Path $portableStage) -Recurse -Force }
New-Item -ItemType Directory -Force -Path $portableStage | Out-Null
Copy-Item "$appDir/*" $portableStage -Recurse -Force

# The marker makes the app keep its data beside the executable instead of in
# %APPDATA%, so a portable copy leaves nothing behind on the host machine.
'Portable build. User data is kept in the userdata folder beside this file.' |
    Set-Content (Join-Path $portableStage 'portable.marker') -Encoding utf8

$portableZip = Join-Path $Output "BitirimClothingCreator-$version-Portable.zip"
if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
# Zip the application folder itself, so extracting gives
# BitirimClothingCreator\BitirimClothingCreator.exe with no extra nesting.
Compress-Archive -Path $portableStage -DestinationPath $portableZip -CompressionLevel Optimal
Write-Host "  $portableZip"

# --------------------------------------------------------------------------
if (-not $SkipInstaller) {
    Step 'Building the installer'
    $wix = Get-Command wix -ErrorAction SilentlyContinue
    if (-not $wix) {
        Write-Warning 'The WiX tool was not found. Install it with: dotnet tool install --global wix'
        Write-Warning 'Skipping the installer; the portable package is still built.'
    }
    else {
        $msi = Join-Path $Output "BitirimClothingCreator-$version-Setup.msi"
        & wix build (Join-Path $PSScriptRoot 'installer.wxs') `
            -d "AppDir=$appDir" -d "Version=$version" `
            -ext WixToolset.UI.wixext `
            -o $msi
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
        Write-Host "  $msi"
    }
}

Step 'Done'
Get-ChildItem $Output -File | Select-Object Name, @{
    n = 'Size'; e = { '{0:N1} MB' -f ($_.Length / 1MB) }
} | Format-Table -AutoSize
