# リポジトリルートから: pwsh -File scripts/check-webview-strings.ps1
# UserMessages の AppTitle / PreviewLoading と wwwroot/index.html の一致をざっくり検査する。
# （静的 HTML は C# と自動同期されないため、文言変更後は必ず実行。build-dist.bat からも呼ばれる）
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$um = Join-Path $root 'src\FileSearch.Messages\UserMessages.cs'
$html = Join-Path $root 'src\FileSearch.Blazor\wwwroot\index.html'
if (-not (Test-Path $um) -or -not (Test-Path $html)) {
    Write-Error "Paths not found. um=$um html=$html"
}
$appLine = Select-String -Path $um -Pattern 'AppTitle\s*=\s*"([^"]+)"' | Select-Object -First 1
$loadLine = Select-String -Path $um -Pattern 'PreviewLoading\s*=\s*"([^"]+)"' | Select-Object -First 1
$htmlText = Get-Content $html -Raw
$appFromCs = $appLine.Matches.Groups[1].Value
$loadFromCs = $loadLine.Matches.Groups[1].Value
$ok = $true
if ($htmlText -notmatch [regex]::Escape("<title>$appFromCs</title>")) {
    Write-Warning "index.html <title> should be '$appFromCs' (UserMessages.AppTitle)"
    $ok = $false
} else { Write-Host "OK: <title> matches AppTitle" }
if ($htmlText -notmatch [regex]::Escape($loadFromCs)) {
    Write-Warning "index.html should contain loading text '$loadFromCs' (PreviewLoading)"
    $ok = $false
} else { Write-Host "OK: index.html contains PreviewLoading text" }
if (-not $ok) { exit 1 }
exit 0
