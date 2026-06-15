# 既存の MSIX 署名用証明書を PFX と CER にエクスポート
# 用法: .\scripts\export-cert-pfx-cer.ps1 -Thumbprint <拇印>
param(
    [Parameter(Mandatory = $true)]
    [string] $Thumbprint,
    [string] $PfxPassword
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_repo.ps1")

$root = Get-RepoRoot
$outDir = Join-Path $root "installers\社内配布"

$cert = Get-ChildItem -Path "Cert:\CurrentUser\My" -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $Thumbprint } |
    Select-Object -First 1
if (-not $cert) {
    throw "証明書が見つかりません。Thumbprint: $Thumbprint"
}

if (-not $PfxPassword) {
    $sec = Read-Host "PFX 用パスワードを入力" -AsSecureString
    $PfxPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$pfxPath = Join-Path $outDir "Panoleon.pfx"
$cerPath = Join-Path $outDir "Panoleon_配布用.cer"
$pwdSecure = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pwdSecure | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null

Write-Host "Exported PFX: $pfxPath" -ForegroundColor Green
Write-Host "Exported CER: $cerPath" -ForegroundColor Green
