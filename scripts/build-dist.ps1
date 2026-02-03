# Build standalone exe for internal distribution and create ZIP
# Run from repo root: .\scripts\build-dist.ps1
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\FileSearch.Blazor\FileSearch.Blazor.csproj"
$publishDir = Join-Path $root "publish\win10-x64"
$distDir = Join-Path $root "installers\dist"
$zipName = "FileSearch_win-x64.zip"
$zipPath = Join-Path $distDir $zipName

Write-Host "Building standalone exe for distribution..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $project -f net8.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$readmeSrc = Join-Path $root "installers\社内配布\インストール手順.txt"
if (Test-Path $readmeSrc) {
    Copy-Item $readmeSrc (Join-Path $publishDir "インストール手順.txt") -Force
}

if (-not (Test-Path (Join-Path $publishDir "sudachi_tokenize.py"))) {
    Write-Warning "sudachi_tokenize.py not found in publish output. Check csproj CopyToPublishDirectory."
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "Done: $zipPath"
Write-Host 'Share this ZIP; users extract and run FileSearch.Blazor.exe'
