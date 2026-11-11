# Builds every release artifact and copies it into dist\:
#   GGCalendar-v<version>-Setup.exe   installer (Inno Setup), from installer\build-installer.ps1
#   GGCalendar-v<version>.msix        local test package, signed with the self-signed certificate
#   GGCalendar-v<version>-Store.msix  package for Microsoft Partner Center (unsigned, Microsoft signs it)
# The version comes from <Version> in src\GGCalendar.App\GGCalendar.App.csproj.
# Requires: .NET SDK, Inno Setup 6, Windows SDK, the signing certificate in CurrentUser\My, OAuth\client_secret*.json.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$appDir = Join-Path $root 'src\GGCalendar.App'
$dist = Join-Path $root 'dist'

$version = ([xml](Get-Content (Join-Path $appDir 'GGCalendar.App.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Version non trovata nel csproj.' }

function Invoke-Step($title, [scriptblock]$action) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$title non riuscito (codice $LASTEXITCODE)." }
}

# dist is never emptied: it keeps the files of every version built (the same version is overwritten).
New-Item -ItemType Directory -Force $dist | Out-Null

# The scripts run in their own PowerShell: build_msix.ps1 changes folder and calls exit on errors.
$ps = (Get-Process -Id $PID).Path

Invoke-Step 'Installer (Setup.exe)' { & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'installer\build-installer.ps1') }
Invoke-Step 'MSIX locale (firmato)' { & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $appDir 'build_msix.ps1') -NoPause }
Invoke-Step 'MSIX per lo Store (non firmato)' { & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $appDir 'build_msix.ps1') -Store -NoPause }

$setup = Join-Path $root "website\downloads\GGCalendar-v$version-Setup.exe"
$local = Join-Path $appDir 'GGCalendar.msix'
$store = Join-Path $appDir 'GGCalendar-Store.msix'
foreach ($f in $setup, $local, $store) { if (-not (Test-Path $f)) { throw "File non trovato: $f" } }

# The hash is recomputed on every build, because the Setup.exe changes each time (same format as sha256sum -b).
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
$hashFile = "$setup.sha256"
[IO.File]::WriteAllText($hashFile, "$hash *$(Split-Path $setup -Leaf)`n", [Text.UTF8Encoding]::new($false))

Copy-Item $setup $dist -Force
Copy-Item $hashFile $dist -Force
Copy-Item $local (Join-Path $dist "GGCalendar-v$version.msix") -Force
Copy-Item $store (Join-Path $dist "GGCalendar-v$version-Store.msix") -Force

Write-Host ""
Write-Host "RELEASE $version COMPLETATA: $dist" -ForegroundColor Green
Get-ChildItem $dist -File -Filter "*v$version*" | Select-Object Name, @{n='MB'; e={[math]::Round($_.Length / 1MB, 1)}}
