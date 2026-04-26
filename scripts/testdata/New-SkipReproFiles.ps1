#Requires -Version 5.1
<#
.SYNOPSIS
    10MB 超ファイルによるインデックス「スキップ」の再現用フォルダを作成する。

.DESCRIPTION
    - small_searchable.txt … 検索できる小さなテキスト
    - oversize_11mb.bin   … 約 11MB（ContentLimits.IndexMaxFileBytesForExtract=10MB 超過でスキップ対象）

.PARAMETER OutputPath
    作成先ディレクトリ。未指定時は %TEMP%\FullTextSearch_SkipRepro_<日時>
#>
param(
    [string] $OutputPath = ""
)

$ErrorActionPreference = "Stop"

# 10MB 超（IndexMaxFileBytesForExtract=10*1024*1024 より大きいこと。再現用に 11MB）
$oversizeBytes = 11L * 1024L * 1024L

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputPath = Join-Path $env:TEMP "FullTextSearch_SkipRepro_$stamp"
}

if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath | Out-Null
}

$small = Join-Path $OutputPath "small_searchable.txt"
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($small, @"
全文検索システム 手動テスト用
キーワード: スキップ再現用の小さなファイル
検索語サンプル: 再現用
"@, $utf8NoBom)

$big = Join-Path $OutputPath "oversize_11mb.bin"
Write-Host "作成中: $big （約 $([math]::Round($oversizeBytes / 1MB, 0)) MB）…"

$bufferSize = 1MB
$buffer = New-Object byte[] $bufferSize
$remaining = $oversizeBytes
$fs = [System.IO.File]::Create($big)
try {
    while ($remaining -gt 0) {
        $chunk = [math]::Min($remaining, $bufferSize)
        if ($chunk -lt $bufferSize) {
            $fs.Write($buffer, 0, [int]$chunk)
        } else {
            $fs.Write($buffer, 0, $bufferSize)
        }
        $remaining -= $chunk
    }
}
finally {
    $fs.Dispose()
}

Write-Host ""
Write-Host "完了。テスト用フォルダ:" -ForegroundColor Green
Write-Host "  $OutputPath"
Write-Host ""
Write-Host "アプリの「検索対象フォルダ」に上記パスを追加し、全体を再構築してください。" -ForegroundColor Cyan
Write-Host "oversize_11mb.bin がスキップされ、small_searchable.txt はインデックスされます。" -ForegroundColor Cyan
