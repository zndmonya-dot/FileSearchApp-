# リポジトリルート解決（各スクリプトから dot-source する）
function Get-RepoRoot {
    $root = Resolve-Path (Join-Path $PSScriptRoot "..")
    if (-not (Test-Path (Join-Path $root "FullTextSearch.sln"))) {
        throw "リポジトリルートを特定できません: $root"
    }
    return $root
}

function Get-BlazorProject {
    param([string]$Root = (Get-RepoRoot))
    Join-Path $Root "src\FileSearch.Blazor\FileSearch.Blazor.csproj"
}

function Test-SudachiBundle {
    param([string]$Root = (Get-RepoRoot))
    $base = Join-Path $Root "tools\sudachi"
    $dic = Join-Path $base "resources\system_core.dic"
    $dicZip = Join-Path $base "resources\system_core.dic.zip"
    if (-not (Test-Path (Join-Path $base "sudachi_ffi.dll"))) { return $false }
    if (-not (Test-Path (Join-Path $base "resources\sudachi.json"))) { return $false }
    if (-not (Test-Path $dic) -and -not (Test-Path $dicZip)) { return $false }
    return $true
}

function Assert-SudachiBundle {
    param([string]$Root = (Get-RepoRoot))
    if (-not (Test-SudachiBundle -Root $Root)) {
        throw "tools\sudachi\ が不足しています。リポジトリを更新するか、build-sudachi-native.ps1 をビルド担当者が実行してください。"
    }
}
