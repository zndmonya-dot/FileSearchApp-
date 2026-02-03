# MSIX 署名用の自己署名証明書を作成し、配布用 .cer をエクスポートする
# 実行: リポジトリルートで .\scripts\create-cert-for-msix.ps1
# 実行後: 表示された Thumbprint を FileSearch.Blazor.csproj の PackageCertificateThumbprint に設定してください

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$certFolder = Join-Path $root "installers\社内配布"
$cerName = "FileSearch_配布用.cer"

$subject = "CN=FileSearch App"
$friendlyName = "FileSearch MSIX Signing"

Write-Host "Creating self-signed code signing certificate..."
$cert = New-SelfSignedCertificate `
    -Subject $subject `
    -Type CodeSigningCert `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -FriendlyName $friendlyName `
    -NotAfter (Get-Date).AddYears(5)

$thumbprint = $cert.Thumbprint
Write-Host ""
Write-Host "Certificate created. Thumbprint:" -ForegroundColor Green
Write-Host "  $thumbprint" -ForegroundColor Cyan
Write-Host ""

# Export .cer for distribution
if (-not (Test-Path $certFolder)) {
    New-Item -ItemType Directory -Path $certFolder -Force | Out-Null
}
$cerPath = Join-Path $certFolder $cerName
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT
Write-Host "Exported .cer for distribution:" -ForegroundColor Green
Write-Host "  $cerPath"
Write-Host ""

Write-Host "Next step: Set PackageCertificateThumbprint in csproj" -ForegroundColor Yellow
Write-Host "  Open: src\FileSearch.Blazor\FileSearch.Blazor.csproj"
Write-Host "  Find: <PackageCertificateThumbprint>...</PackageCertificateThumbprint>"
Write-Host "  Replace with: <PackageCertificateThumbprint>$thumbprint</PackageCertificateThumbprint>"
Write-Host ""
Write-Host "Then run: dotnet build FullTextSearch.sln -c Release"
