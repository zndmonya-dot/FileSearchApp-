# スタンドアロン exe の publish と配布 ZIP 作成
# 用法: pwsh -File scripts/build-dist.ps1
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_repo.ps1")

$root = Get-RepoRoot
$project = Get-BlazorProject -Root $root
$publishDir = Join-Path $root "publish\win10-x64"
$distDir = Join-Path $root "installers\dist"
$zipPath = Join-Path $distDir "Panoleon_win-x64.zip"

Assert-SudachiBundle -Root $root

Write-Host "Checking wwwroot strings vs UserMessages..."
& (Join-Path $PSScriptRoot "check-webview-strings.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publishing standalone exe..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $project `
    -f net8.0-windows10.0.19041.0 `
    -c Release `
    -p:RuntimeIdentifierOverride=win-x64 `
    -p:WindowsPackageType=None `
    --self-contained true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$readmeSrc = Join-Path $root "installers\社内配布\インストール手順.txt"
if (Test-Path $readmeSrc) {
    Copy-Item $readmeSrc (Join-Path $publishDir "インストール手順.txt") -Force
}

if (-not (Test-Path (Join-Path $publishDir "sudachi_ffi.dll"))) {
    Write-Warning "sudachi_ffi.dll not found in publish output."
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "Done: $zipPath"
