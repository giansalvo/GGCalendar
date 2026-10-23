# Builds the MSIX package.
#   .\build_msix.ps1          local test package, signed with the self-signed certificate
#                             (the manifest Publisher is replaced by the certificate Subject in the package only)
#   .\build_msix.ps1 -Store   package for Microsoft Partner Center: manifest identity as is (assigned by the Store),
#                             NOT signed, because the Store signs it. It cannot be installed locally.
param(
    [switch]$Store,
    [switch]$NoPause
)

Set-Location -Path $PSScriptRoot
$ErrorActionPreference = "Stop"

$certThumbprint = "629AC2C6369DE84AFE90082BC84FA154E0807E86"
$manifestPath = Join-Path $PSScriptRoot "Package.appxmanifest"
$publishDir = Join-Path $PSScriptRoot "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
$msixPath = Join-Path $PSScriptRoot $(if ($Store) { "GGCalendar-Store.msix" } else { "GGCalendar.msix" })

# Gli strumenti del Windows SDK (makepri, makeappx, signtool) non sono nel PATH di una PowerShell normale:
# se mancano, li cerca nella versione più recente installata in Windows Kits\10\bin.
$sdkBin = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64" -Directory -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Parent.Name } -Descending | Select-Object -First 1
foreach ($tool in "makepri", "makeappx", "signtool") {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        if (-not $sdkBin -or -not (Test-Path (Join-Path $sdkBin.FullName "$tool.exe"))) {
            throw "$tool non trovato: installa il Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100) o usa un Developer PowerShell."
        }
        $env:Path += ";$($sdkBin.FullName)"
    }
}

try {
    # 1. Legge il manifest
    if (-not (Test-Path $manifestPath)) {
        throw "Manifest non trovato: $manifestPath"
    }

    [xml]$manifest = Get-Content $manifestPath -Raw
    $identityNode = $manifest.SelectSingleNode("//*[local-name()='Identity']")

    if (-not $identityNode) {
        throw "Elemento Identity non trovato in Package.appxmanifest."
    }

    # La versione del pacchetto viene da <Version> nel csproj (unica fonte); lo Store vuole la revisione finale a 0.
    $csprojPath = Join-Path $PSScriptRoot "GGCalendar.App.csproj"
    $version = ([xml](Get-Content $csprojPath)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw "Version non trovata nel csproj." }
    $parts = @($version.Split('.'))
    while ($parts.Count -lt 3) { $parts += '0' }
    $identityNode.SetAttribute("Version", ($parts[0..2] -join '.') + ".0")

    # Senza il json OAuth l'app pacchettizzata non potrebbe collegarsi a Google.
    $oauthDir = Join-Path $PSScriptRoot "..\..\OAuth"
    if (-not (Get-ChildItem $oauthDir -Filter 'client_secret*.json' -ErrorAction SilentlyContinue)) {
        throw "Manca OAuth\client_secret*.json: senza, l'app installata non potrebbe collegarsi a Google."
    }

    Write-Host "Identity Name:      $($identityNode.GetAttribute('Name'))" -ForegroundColor Gray
    Write-Host "Identity Publisher: $($identityNode.GetAttribute('Publisher'))" -ForegroundColor Gray
    Write-Host "Version:            $($identityNode.GetAttribute('Version'))" -ForegroundColor Gray

    # 2. Per il pacchetto locale serve il certificato di firma; per lo Store no
    if (-not $Store) {
        $cert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Thumbprint -eq $certThumbprint -and $_.HasPrivateKey } |
            Select-Object -First 1

        if (-not $cert) {
            throw "Il certificato atteso non è disponibile in CurrentUser\My."
        }

        Write-Host "Certificato selezionato: $($cert.FriendlyName)" -ForegroundColor Green
        Write-Host "Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray

        # Il Publisher del manifest è quello dello Store: nel pacchetto locale va allineato al certificato.
        $identityNode.SetAttribute("Publisher", $cert.Subject)
    }

    # 3. Compila e pubblica il progetto .NET
    Write-Host "Compilazione del progetto..." -ForegroundColor Cyan
    # Self-contained: il pacchetto non può contare sul runtime .NET installato; senza .pdb.
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    dotnet publish -c Release -r win-x64 --self-contained true -p:DebugType=none

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish è terminato con codice $LASTEXITCODE."
    }

    if (-not (Test-Path $publishDir)) {
        throw "Cartella di pubblicazione non trovata: $publishDir"
    }

    # I pacchetti nativi (HarfBuzz, Skia) portano comunque i loro .pdb: non servono nel pacchetto.
    Get-ChildItem $publishDir -Filter *.pdb | Remove-Item -Force

    # 4. Copia manifest e Assets
    Write-Host "Copia del manifest..." -ForegroundColor Cyan
    $manifest.Save((Join-Path $publishDir "AppxManifest.xml"))

    Write-Host "Copia della cartella Assets..." -ForegroundColor Cyan
    Copy-Item (Join-Path $PSScriptRoot "Assets") $publishDir -Recurse -Force

    # 5. Indice delle risorse (resources.pri): serve a risolvere i file Assets con suffisso di scala (es. StoreLogo.scale-200.png)
    Write-Host "Generazione di resources.pri..." -ForegroundColor Cyan
    $priConfig = Join-Path $env:TEMP "GGCalendar-priconfig.xml"
    makepri createconfig /cf $priConfig /dq it-IT /pv 10.0.0 /o

    if ($LASTEXITCODE -ne 0) {
        throw "makepri createconfig è terminato con codice $LASTEXITCODE."
    }

    # Senza <packaging> makepri non divide le risorse in resources.scale-*.pri: restano tutte in resources.pri
    $priXml = [xml](Get-Content $priConfig -Raw)
    $priXml.SelectNodes("//packaging") | ForEach-Object { [void]$_.ParentNode.RemoveChild($_) }
    $priXml.Save($priConfig)

    Get-ChildItem $publishDir -Filter "resources*.pri" | Remove-Item -Force
    makepri new /pr $publishDir /cf $priConfig /mn (Join-Path $publishDir "AppxManifest.xml") /of (Join-Path $publishDir "resources.pri") /o

    if ($LASTEXITCODE -ne 0) {
        throw "makepri new è terminato con codice $LASTEXITCODE."
    }

    # 6. Genera l'MSIX
    Write-Host "Generazione del pacchetto MSIX..." -ForegroundColor Cyan
    makeappx pack /d $publishDir /p $msixPath /o

    if ($LASTEXITCODE -ne 0) {
        throw "makeappx pack è terminato con codice $LASTEXITCODE."
    }

    if ($Store) {
        Write-Host ""
        Write-Host "PACCHETTO PER LO STORE CREATO (non firmato)" -ForegroundColor Green
        Write-Host "Pacchetto: $msixPath"
        Write-Host "Caricalo in Partner Center > Packages."
    }
    else {
        # 7. Firma con lo stesso certificato
        Write-Host "Firma del pacchetto MSIX..." -ForegroundColor Cyan
        signtool sign /v /fd SHA256 /sha1 $cert.Thumbprint $msixPath

        if ($LASTEXITCODE -ne 0) {
            throw "signtool sign è terminato con codice $LASTEXITCODE."
        }

        # 8. Verifica la firma
        Write-Host "Verifica della firma..." -ForegroundColor Cyan
        signtool verify /v /pa $msixPath

        if ($LASTEXITCODE -ne 0) {
            throw "La verifica della firma è fallita."
        }

        # 9. Verifica che l'MSIX sia firmato dal certificato atteso
        $signature = Get-AuthenticodeSignature -FilePath $msixPath

        if (-not $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
            throw "Il certificato firmatario dell'MSIX non corrisponde a quello atteso."
        }

        Write-Host ""
        Write-Host "BUILD E FIRMA COMPLETATI CON SUCCESSO" -ForegroundColor Green
        Write-Host "Pacchetto: $msixPath"
        Write-Host "Thumbprint firmatario: $($signature.SignerCertificate.Thumbprint)"
    }
}
catch {
    Write-Host ""
    Write-Host "ERRORE: $($_.Exception.Message)" -ForegroundColor Red
    if (-not $NoPause) { Read-Host "Premi INVIO per chiudere" }
    exit 1
}

if (-not $NoPause) {
    Write-Host ""
    Read-Host "Premi INVIO per chiudere"
}
