# index.html と UserMessages の文言一致を検査
# 用法: pwsh -File scripts/check-webview-strings.ps1
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_repo.ps1")

$root = Get-RepoRoot
$um = Join-Path $root "src\FileSearch.Messages\UserMessages.cs"
$html = Join-Path $root "src\FileSearch.Blazor\wwwroot\index.html"
$bootJs = Join-Path $root "src\FileSearch.Blazor\wwwroot\js\boot-splash.js"

function Get-UserMessage([string]$name) {
    $line = Select-String -Path $um -Pattern "${name}\s*=\s*""([^""]+)""" | Select-Object -First 1
    if (-not $line) { throw "UserMessages.$name not found in $um" }
    return $line.Matches.Groups[1].Value
}

$htmlText = Get-Content $html -Raw
$bootJsText = Get-Content $bootJs -Raw
$webviewText = $htmlText + $bootJsText
$ok = $true

$checks = @(
    @{ Name = "AppTitle";       Test = { $htmlText -match [regex]::Escape("<title>$(Get-UserMessage 'AppTitle')</title>") }; Label = "<title> matches AppTitle" },
    @{ Name = "BootSplashStarting"; Test = { $htmlText -match ('aria-label="' + [regex]::Escape((Get-UserMessage 'BootSplashStarting')) + '"') }; Label = "boot splash aria-label matches BootSplashStarting" },
    @{ Name = "BootSplashTagline"; Test = { $webviewText -match [regex]::Escape((Get-UserMessage 'BootSplashTagline')) }; Label = "contains BootSplashTagline" },
    @{ Name = "BootSplashVersion"; Test = { $htmlText -match 'class="boot-splash__version"' -and $htmlText -notmatch 'boot-splash__version">\s*v\d' }; Label = "boot-splash version element present (not hardcoded)" },
    @{ Name = "BootSplashStarting"; Test = { $webviewText -match [regex]::Escape((Get-UserMessage 'BootSplashStarting')) }; Label = "contains BootSplashStarting" },
    @{ Name = "BootSplashReady"; Test = { $webviewText -match [regex]::Escape((Get-UserMessage 'BootSplashReady')) }; Label = "contains BootSplashReady" },
    @{ Name = "WebViewLoadError"; Test = { $htmlText -match [regex]::Escape((Get-UserMessage 'WebViewLoadError')) }; Label = "contains WebViewLoadError text" },
    @{ Name = "WebViewReload";  Test = { $htmlText -match [regex]::Escape((Get-UserMessage 'WebViewReload')) }; Label = "contains WebViewReload text" }
)

foreach ($c in $checks) {
    if (& $c.Test) {
        Write-Host "OK: $($c.Label)"
    }
    else {
        Write-Warning "NG: $($c.Label) (UserMessages.$($c.Name))"
        $ok = $false
    }
}

exit $(if ($ok) { 0 } else { 1 })
