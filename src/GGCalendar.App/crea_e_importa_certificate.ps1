
Set-Location -Path $PSScriptRoot
$ErrorActionPreference = "Stop"

$certName = "GGCalendarCodeSigning"
$subject = "CN=GiansalvoGusinu"
$cerPath = Join-Path $PSScriptRoot "GGCalendarTestCert.cer"

# Verifica i privilegi amministrativi prima di procedere
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdmin = $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdmin) {
    Write-Host "Richiesti privilegi amministrativi. Apro PowerShell..." -ForegroundColor Yellow
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`""
    )
    exit
}

try {
    Write-Host "Ricerca del certificato di Code Signing..." -ForegroundColor Cyan

    # Riutilizza un certificato valido con chiave privata ed EKU Code Signing
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object {
            $_.FriendlyName -eq $certName -and
            $_.Subject -eq $subject -and
            $_.HasPrivateKey -and
            $_.NotAfter -gt (Get-Date) -and
            $_.EnhancedKeyUsageList.ObjectId.Value -contains "1.3.6.1.5.5.7.3.3"
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        Write-Host "Creo un nuovo certificato..." -ForegroundColor Yellow

        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $subject `
            -FriendlyName $certName `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -Provider "Microsoft Software Key Storage Provider"
    }
    else {
        Write-Host "Riutilizzo del certificato esistente." -ForegroundColor Green
    }

    if (-not $cert.HasPrivateKey) {
        throw "Il certificato non dispone di una chiave privata."
    }

    Write-Host "Subject:    $($cert.Subject)"
    Write-Host "Thumbprint: $($cert.Thumbprint)"

    # Esporta solo il certificato pubblico
    Export-Certificate -Cert $cert -FilePath $cerPath -Force |
        Out-Null

    # Installa il certificato pubblico tra le persone attendibili
    $trustedStore = "Cert:\LocalMachine\TrustedPeople"

    $alreadyTrusted = Get-ChildItem $trustedStore |
        Where-Object { $_.Thumbprint -eq $cert.Thumbprint } |
        Select-Object -First 1

    if (-not $alreadyTrusted) {
        Import-Certificate `
            -FilePath $cerPath `
            -CertStoreLocation $trustedStore |
            Out-Null

        Write-Host "Certificato installato in LocalMachine\TrustedPeople." -ForegroundColor Green
    }
    else {
        Write-Host "Certificato già presente tra quelli attendibili." -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "OPERAZIONE COMPLETATA" -ForegroundColor Green
    Write-Host "Archivio firma: CurrentUser\My"
    Write-Host "Archivio fiducia: LocalMachine\TrustedPeople"
    Write-Host "File pubblico: $cerPath"
    Write-Host "Thumbprint: $($cert.Thumbprint)"
}
catch {
    Write-Host "ERRORE: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Read-Host "Premi INVIO per chiudere"
