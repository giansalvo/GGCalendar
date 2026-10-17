# Recupera il certificato esatto da conservare
$keepThumb = "629AC2C6369DE84AFE90082BC84FA154E0807E86"
$cerPath = Join-Path $PSScriptRoot "GGCalendarTestCert.cer"

$cert = Get-Item "Cert:\CurrentUser\My\$keepThumb" -ErrorAction Stop

if (-not $cert.HasPrivateKey) {
    throw "Il certificato selezionato non ha la chiave privata."
}

if ($cert.NotAfter -le (Get-Date)) {
    throw "Il certificato selezionato è scaduto."
}

Write-Host "Certificato da conservare: $($cert.Subject)" -ForegroundColor Cyan
Write-Host "Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray

# Esporta il certificato pubblico corretto
Export-Certificate -Cert $cert -FilePath $cerPath -Force |
    Out-Null

# Verifica che il file CER corrisponda al certificato selezionato
$exportedCert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($cerPath)

if ($exportedCert.Thumbprint -ne $keepThumb) {
    throw "Il thumbprint del file CER non coincide. Pulizia interrotta."
}

Write-Host "CER esportato e verificato: $cerPath" -ForegroundColor Green