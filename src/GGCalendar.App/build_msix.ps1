
Set-Location -Path $PSScriptRoot
$ErrorActionPreference = "Stop"

$certName = "GGCalendarCodeSigning"
$subject = "CN=GiansalvoGusinu"
$manifestPath = Join-Path $PSScriptRoot "Package.appxmanifest"
$msixPath = Join-Path $PSScriptRoot "GGCalendar.msix"
$publishDir = Join-Path $PSScriptRoot "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

try {
    # 1. Recupera il certificato usato dallo script di configurazione
	$cert = Get-ChildItem Cert:\CurrentUser\My |
		Where-Object {
			$_.Thumbprint -eq "629AC2C6369DE84AFE90082BC84FA154E0807E86" -and
			$_.HasPrivateKey
		} |
		Select-Object -First 1

	if (-not $cert) {
		throw "Il certificato atteso non è disponibile in CurrentUser\My."
	}

	Write-Host "Certificato selezionato: $($cert.FriendlyName)" -ForegroundColor Green
	Write-Host "Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray

    # 2. Controlla il Publisher del manifest prima della build
    if (-not (Test-Path $manifestPath)) {
        throw "Manifest non trovato: $manifestPath"
    }

    [xml]$manifest = Get-Content $manifestPath -Raw
    $identityNode = $manifest.SelectSingleNode("//*[local-name()='Identity']")

    if (-not $identityNode) {
        throw "Elemento Identity non trovato in Package.appxmanifest."
    }

    $publisher = $identityNode.GetAttribute("Publisher")

    if ($publisher -cne $cert.Subject) {
        throw "Publisher del manifest ('$publisher') diverso dal Subject del certificato ('$($cert.Subject)'). Correggi Package.appxmanifest."
    }

    # 3. Compila e pubblica il progetto .NET
    Write-Host "Compilazione del progetto..." -ForegroundColor Cyan
    dotnet publish -c Release -r win-x64

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish è terminato con codice $LASTEXITCODE."
    }

    if (-not (Test-Path $publishDir)) {
        throw "Cartella di pubblicazione non trovata: $publishDir"
    }

    # 4. Copia manifest e Assets
    Write-Host "Copia del manifest..." -ForegroundColor Cyan
    Copy-Item $manifestPath (Join-Path $publishDir "AppxManifest.xml") -Force

    Write-Host "Copia della cartella Assets..." -ForegroundColor Cyan
    Copy-Item (Join-Path $PSScriptRoot "Assets") $publishDir -Recurse -Force

    # 5. Genera l'MSIX
    Write-Host "Generazione del pacchetto MSIX..." -ForegroundColor Cyan
    makeappx pack /d $publishDir /p $msixPath /o

    if ($LASTEXITCODE -ne 0) {
        throw "makeappx pack è terminato con codice $LASTEXITCODE."
    }

    # 6. Firma con lo stesso certificato
    Write-Host "Firma del pacchetto MSIX..." -ForegroundColor Cyan
    signtool sign /v /fd SHA256 /sha1 $cert.Thumbprint $msixPath

    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign è terminato con codice $LASTEXITCODE."
    }

    # 7. Verifica la firma
    Write-Host "Verifica della firma..." -ForegroundColor Cyan
    signtool verify /v /pa $msixPath

    if ($LASTEXITCODE -ne 0) {
        throw "La verifica della firma è fallita."
    }

    # 8. Verifica che l'MSIX sia firmato dal certificato atteso
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
catch {
    Write-Host ""
    Write-Host "ERRORE: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Read-Host "Premi INVIO per chiudere"
