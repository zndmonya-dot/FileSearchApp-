# Sudachi ネイティブ DLL のビルドと辞書取得。
# リポジトリルートから: pwsh -File scripts/build-sudachi-native.ps1
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$nativeDir = Join-Path $root "native\sudachi-ffi"
$resDir = Join-Path $root "tools\sudachi\resources"
$dictPath = Join-Path $resDir "system_core.dic"
$dictZip = Join-Path $env:TEMP "sudachi-dictionary-latest-core.zip"
$baseUrl = "https://raw.githubusercontent.com/WorksApplications/sudachi.rs/v0.6.11/resources"
$cargo = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"

New-Item -ItemType Directory -Path $resDir -Force | Out-Null

foreach ($file in @("sudachi.json", "char.def", "unk.def")) {
    $dest = Join-Path $resDir $file
    if (-not (Test-Path $dest)) {
        Write-Host "Downloading $file ..."
        Invoke-WebRequest -Uri "$baseUrl/$file" -OutFile $dest -UseBasicParsing
    }
}

# systemDict を同梱辞書名に合わせる
$jsonPath = Join-Path $resDir "sudachi.json"
$json = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$json.systemDict = "system_core.dic"
$json | ConvertTo-Json -Depth 10 | Set-Content $jsonPath -Encoding UTF8

if (-not (Test-Path $dictPath)) {
    Write-Host "Downloading Sudachi core dictionary (~70MB) ..."
    Invoke-WebRequest -Uri "https://d2ej7fkh96fzlu.cloudfront.net/sudachidict/sudachi-dictionary-latest-core.zip" -OutFile $dictZip -UseBasicParsing
    Expand-Archive -Path $dictZip -DestinationPath (Join-Path $env:TEMP "sudachi-dict-extract") -Force
    $dic = Get-ChildItem -Path (Join-Path $env:TEMP "sudachi-dict-extract") -Recurse -Filter "system_core.dic" | Select-Object -First 1
    if (-not $dic) { throw "system_core.dic not found in dictionary zip" }
    Copy-Item $dic.FullName $dictPath -Force
    Write-Host "Dictionary installed: $dictPath"
}

if (-not (Test-Path $cargo)) {
    throw "cargo not found at $cargo. Install Rust: https://rustup.rs"
}

Write-Host "Building sudachi_ffi (release) ..."
Push-Location $nativeDir
try {
    & $cargo build --release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

$dllSrc = Join-Path $nativeDir "target\release\sudachi_ffi.dll"
$dllDest = Join-Path $root "tools\sudachi\sudachi_ffi.dll"
Copy-Item $dllSrc $dllDest -Force
Write-Host "Built: $dllDest"
