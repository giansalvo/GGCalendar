# Publishes GGCalendar (self-contained, win-x64) and builds website\downloads\GGCalendar-v<version>-Setup.exe.
# Requires Inno Setup 6 (winget install JRSoftware.InnoSetup) and the Google OAuth client json in OAuth\.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\GGCalendar.App\GGCalendar.App.csproj'
$publish = Join-Path $root 'artifacts\publish'

if (-not (Get-ChildItem (Join-Path $root 'OAuth') -Filter 'client_secret*.json' -ErrorAction SilentlyContinue)) {
    throw 'Manca OAuth\client_secret*.json: senza, l''app installata non potrebbe collegarsi a Google.'
}

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Version non trovata nel csproj.' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 non trovato: winget install JRSoftware.InnoSetup' }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish non riuscito.' }

& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'GGCalendar.iss')
if ($LASTEXITCODE -ne 0) { throw 'Compilazione dell''installer non riuscita.' }

Get-Item (Join-Path $root "website\downloads\GGCalendar-v$version-Setup.exe")
