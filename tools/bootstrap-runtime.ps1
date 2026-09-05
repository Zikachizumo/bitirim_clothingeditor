<#
.SYNOPSIS
    Fetches the embedded Python runtime and installs the asset backend into it.

.DESCRIPTION
    This is the Phase 1 answer to "can we ship without making the user install
    Python?" -- the embeddable CPython distribution is a self-contained folder:
    no installer, no registry keys, no PATH changes, nothing machine-wide. The
    shipped product carries the same folder beside its executable.

    Run once after cloning. Takes about a minute.

.PARAMETER Destination
    Where the runtime goes. Keep it SHORT. pip fails with WinError 206 when the
    resulting site-packages paths exceed MAX_PATH, which a deep repo checkout
    will do on its own.

.EXAMPLE
    ./tools/bootstrap-runtime.ps1 -Destination C:\bcc\python
#>
[CmdletBinding()]
param(
    [string]$Destination = 'C:\bcc\python',
    [string]$PythonVersion = '3.12.10',
    [string]$FiveFuryVersion = '0.4.20'
)

$ErrorActionPreference = 'Stop'

if ($Destination.Length -gt 40) {
    Write-Warning "Destination is $($Destination.Length) characters. pip may fail on MAX_PATH; prefer something like C:\bcc\python."
}

$zipUrl = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
$zipPath = Join-Path $env:TEMP "python-$PythonVersion-embed-amd64.zip"

if (-not (Test-Path $Destination)) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
}

Write-Host "Downloading $zipUrl"
Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing

Write-Host "Extracting to $Destination"
Expand-Archive -Path $zipPath -DestinationPath $Destination -Force
Remove-Item $zipPath -Force

# The embeddable distribution ships with site disabled, which also disables
# site-packages. Enable it so pip-installed modules are importable.
$pthMajorMinor = $PythonVersion -replace '^(\d+)\.(\d+).*$', '$1$2'
$pth = Join-Path $Destination "python$pthMajorMinor._pth"
@"
python$pthMajorMinor.zip
.
Lib\site-packages

import site
"@ | Set-Content -Path $pth -Encoding utf8

Write-Host 'Bootstrapping pip'
$getPip = Join-Path $Destination 'get-pip.py'
Invoke-WebRequest -Uri 'https://bootstrap.pypa.io/get-pip.py' -OutFile $getPip -UseBasicParsing
& (Join-Path $Destination 'python.exe') $getPip --no-warn-script-location -q
Remove-Item $getPip -Force

Write-Host "Installing fivefury==$FiveFuryVersion"
& (Join-Path $Destination 'python.exe') -m pip install --no-warn-script-location -q "fivefury==$FiveFuryVersion"

Write-Host 'Verifying'
& (Join-Path $Destination 'python.exe') -c "import fivefury; print('fivefury import OK')"

$size = (Get-ChildItem $Destination -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("Runtime ready at {0} ({1:N0} MB)" -f $Destination, $size)
Write-Host ''
Write-Host 'Set these for the test harness and test suite:'
Write-Host ("  $env:BCC_PYTHON = '{0}'" -f (Join-Path $Destination 'python.exe'))
Write-Host '  $env:BCC_ASSETSERVICE = "<repo>\src\assetservice\main.py"'
