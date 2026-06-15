# MSIX 署名用の自己署名証明書を作成し、配布用 .cer をエクスポート
# 用法: .\scripts\create-cert-for-msix.ps1
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_repo.ps1")

$root = Get-RepoRoot
$certFolder = Join-Path $root "installers\社内配布"
$cerName = "Panoleon_配布用.cer"

Write-Host "Creating self-signed code signing certificate..."
$cert = New-SelfSignedCertificate `
    -Subject "CN=Panoleon" `
    -Type CodeSigningCert `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -FriendlyName "Panoleon MSIX 署名" `
    -NotAfter (Get-Date).AddYears(5)

$thumbprint = $cert.Thumbprint
Write-Host ""
Write-Host "Thumbprint: $thumbprint" -ForegroundColor Cyan

New-Item -ItemType Directory -Path $certFolder -Force | Out-Null
$cerPath = Join-Path $certFolder $cerName
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
Write-Host "Exported CER: $cerPath" -ForegroundColor Green
Write-Host ""
Write-Host "Set PackageCertificateThumbprint in src\FileSearch.Blazor\FileSearch.Blazor.csproj" -ForegroundColor Yellow
Write-Host "Optional: .\scripts\export-cert-pfx-cer.ps1 -Thumbprint $thumbprint"
