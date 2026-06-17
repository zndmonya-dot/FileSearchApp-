# Sudachi ネイティブ DLL のビルドと辞書取得（ビルド担当者向け）。
# 完了後 tools\sudachi\ をリポジトリにコミットする。
# 用法: pwsh -File scripts/build-sudachi-native.ps1 [-Rebuild]

param([switch]$Rebuild)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "_repo.ps1")

$root = Get-RepoRoot

$nativeDir = Join-Path $root "native\sudachi-ffi"
$resDir = Join-Path $root "tools\sudachi\resources"
$dictPath = Join-Path $resDir "system_core.dic"
$dllDest = Join-Path $root "tools\sudachi\sudachi_ffi.dll"
$cargo = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"

$baseUrl = "https://raw.githubusercontent.com/WorksApplications/sudachi.rs/v0.6.11/resources"
$dictUrl = "https://d2ej7fkh96fzlu.cloudfront.net/sudachidict/sudachi-dictionary-latest-core.zip"

function Ensure-ResourceFile([string]$name) {
    $dest = Join-Path $resDir $name

    if (Test-Path $dest) {
        return
    }

    Write-Host "Downloading $name ..."

    Invoke-WebRequest `
        -Uri "$baseUrl/$name" `
        -OutFile $dest `
        -UseBasicParsing
}

function Ensure-Dictionary {
    if (Test-Path $dictPath) {
        return
    }

    Write-Host "Downloading Sudachi core dictionary (~70MB) ..."

    $dictZip = Join-Path $env:TEMP "sudachi-dictionary-latest-core.zip"
    $extractDir = Join-Path $env:TEMP "sudachi-dict-extract"

    if (Test-Path $dictZip) {
        Remove-Item $dictZip -Force
    }

    if (Test-Path $extractDir) {
        Remove-Item $extractDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

    Invoke-WebRequest `
        -Uri $dictUrl `
        -OutFile $dictZip `
        -UseBasicParsing

    Expand-Archive `
        -Path $dictZip `
        -DestinationPath $extractDir `
        -Force

    $dic = Get-ChildItem `
        -Path $extractDir `
        -Recurse `
        -Filter "system_core.dic" |
        Select-Object -First 1

    if (-not $dic) {
        throw "system_core.dic not found in dictionary zip"
    }

    Copy-Item `
        -Path $dic.FullName `
        -Destination $dictPath `
        -Force

    Write-Host "Dictionary installed: $dictPath"
}

function Sync-SudachiJson {
    $jsonPath = Join-Path $resDir "sudachi.json"

    if (-not (Test-Path $jsonPath)) {
        throw "Missing $jsonPath"
    }

    $json = Get-Content `
        -Path $jsonPath `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json

    $json.systemDict = "system_core.dic"

    $json |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -Path $jsonPath `
            -Encoding UTF8
}

function Build-Dll {
    if (-not (Test-Path $cargo)) {
        throw "cargo not found at $cargo. Install Rust: https://rustup.rs"
    }

    if (-not (Test-Path $nativeDir)) {
        throw "Native source directory not found: $nativeDir"
    }

    Write-Host "Building sudachi_ffi (release) ..."

    Push-Location $nativeDir

    try {
        & $cargo build --release

        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }

    $dllSrc = Join-Path $nativeDir "target\release\sudachi_ffi.dll"

    if (-not (Test-Path $dllSrc)) {
        throw "Built DLL not found: $dllSrc"
    }

    $dllDir = Split-Path $dllDest -Parent

    New-Item `
        -ItemType Directory `
        -Path $dllDir `
        -Force |
        Out-Null

    Copy-Item `
        -Path $dllSrc `
        -Destination $dllDest `
        -Force

    Write-Host "Built: $dllDest"
}

function Update-DictionaryZip {
    if (-not (Test-Path $dictPath)) {
        return
    }

    $zipPath = Join-Path $resDir "system_core.dic.zip"

    Write-Host "Updating system_core.dic.zip ..."

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive `
        -Path $dictPath `
        -DestinationPath $zipPath `
        -CompressionLevel Optimal
}

New-Item `
    -ItemType Directory `
    -Path $resDir `
    -Force |
    Out-Null

foreach ($file in @("sudachi.json", "char.def", "unk.def")) {
    Ensure-ResourceFile $file
}

Ensure-Dictionary

Sync-SudachiJson

Update-DictionaryZip

if ($Rebuild -or -not (Test-Path $dllDest)) {
    Build-Dll
}
else {
    Write-Host "DLL already exists (use -Rebuild to force): $dllDest"
}

Write-Host ""
Write-Host "Next: commit tools\sudachi\ to the repository." -ForegroundColor Yellow
