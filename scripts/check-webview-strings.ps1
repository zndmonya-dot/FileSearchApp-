# リポジトリルートから: pwsh -File scripts/check-webview-strings.ps1
# UserMessages の AppTitle / PreviewLoading / WebViewLoadError / WebViewReload と
# wwwroot/index.html の一致をざっくり検査する。
# （静的 HTML は C# と自動同期されないため、文言変更後は必ず実行。build-dist.bat からも呼ばれる）
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$um = Join-Path $root 'src\FileSearch.Messages\UserMessages.cs'
$html = Join-Path $root 'src\FileSearch.Blazor\wwwroot\index.html'
if (-not (Test-Path $um) -or -not (Test-Path $html)) {
    Write-Error "Paths not found. um=$um html=$html"
}

function Get-UserMessage([string]$name) {
    $line = Select-String -Path $um -Pattern "${name}\s*=\s*""([^""]+)""" | Select-Object -First 1
    if (-not $line) { throw "UserMessages.$name not found in $um" }
    return $line.Matches.Groups[1].Value
}

$htmlText = Get-Content $html -Raw
$ok = $true

$appTitle = Get-UserMessage 'AppTitle'
if ($htmlText -notmatch [regex]::Escape("<title>$appTitle</title>")) {
    Write-Warning "index.html <title> should be '$appTitle' (UserMessages.AppTitle)"
    $ok = $false
} else { Write-Host "OK: <title> matches AppTitle" }

$previewLoading = Get-UserMessage 'PreviewLoading'
if ($htmlText -notmatch [regex]::Escape($previewLoading)) {
    Write-Warning "index.html should contain loading text '$previewLoading' (PreviewLoading)"
    $ok = $false
} else { Write-Host "OK: index.html contains PreviewLoading text" }

$webViewError = Get-UserMessage 'WebViewLoadError'
if ($htmlText -notmatch [regex]::Escape($webViewError)) {
    Write-Warning "index.html should contain '$webViewError' (WebViewLoadError)"
    $ok = $false
} else { Write-Host "OK: index.html contains WebViewLoadError text" }

$webViewReload = Get-UserMessage 'WebViewReload'
if ($htmlText -notmatch [regex]::Escape($webViewReload)) {
    Write-Warning "index.html should contain '$webViewReload' (WebViewReload)"
    $ok = $false
} else { Write-Host "OK: index.html contains WebViewReload text" }

if (-not $ok) { exit 1 }
exit 0
