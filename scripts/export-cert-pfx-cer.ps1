# 既存の MSIX 署名用証明書を PFX と CER にエクスポートする
# 実行: リポジトリルートで .\scripts\export-cert-pfx-cer.ps1 -Thumbprint <拇印>
# パスワードは実行時にプロンプトで入力（リポジトリに書かないこと）

param(
    [Parameter(Mandatory = $true)]
    [string] $Thumbprint,
    [string] $PfxPassword
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$outDir = Join-Path $root "installers\社内配布"
$pfxName = "全文検索システム.pfx"
$cerName = "全文検索システム_配布用.cer"

# 証明書を取得（CurrentUser\My）
$cert = Get-ChildItem -Path "Cert:\CurrentUser\My" -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $Thumbprint }
if (-not $cert) {
    Write-Error "証明書が見つかりません。Thumbprint: $Thumbprint  (certmgr.msc の「個人」で確認してください)"
}

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# PFX 用パスワード
if (-not $PfxPassword) {
    $sec = Read-Host "PFX 用パスワードを入力" -AsSecureString
    $PfxPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}

$pfxPath = Join-Path $outDir $pfxName
$cerPath = Join-Path $outDir $cerName
$pwdSecure = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText

# PFX（署名用・秘密キー含む）
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pwdSecure
Write-Host "Exported PFX: $pfxPath" -ForegroundColor Green

# CER（配布用・公開鍵のみ）
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT
Write-Host "Exported CER: $cerPath" -ForegroundColor Green

Write-Host ""
Write-Host "PFX は署名・バックアップ用。CER を利用者に配布してください。" -ForegroundColor Yellow
