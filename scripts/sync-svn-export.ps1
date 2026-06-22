# ビルドに必要な最小ソースのみ svn\ に生成する（bin/obj 等の成果物は含めない）
# FILE_LIST.md は毎回末尾で再生成する
# 用法: pwsh -File scripts/sync-svn-export.ps1
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_repo.ps1")

$root = Get-RepoRoot
$dest = Join-Path $root "svn"
$templateDir = Join-Path $PSScriptRoot "svn"

$BuildDirNames = @('bin', 'obj', '.vs', 'Debug', 'Release', 'publish', 'Build', 'build', 'Out', 'out')

function Remove-BuildArtifacts {
    param([string]$TreeRoot)
    if (-not (Test-Path $TreeRoot)) { return }
    foreach ($name in $BuildDirNames) {
        Get-ChildItem -Path $TreeRoot -Directory -Recurse -Force -Filter $name -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    Get-ChildItem -Path $TreeRoot -Recurse -Force -Include @(
        '*.user', '*.suo', '*.cache', 'build.log', 'project.lock.json'
    ) -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Copy-SourceTree {
    param(
        [string]$Source,
        [string]$Target
    )
    if (-not (Test-Path $Source)) {
        throw "コピー元がありません: $Source"
    }
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    $xd = ($BuildDirNames | ForEach-Object { "/XD"; $_ }) -join ' '
    $xf = '/XF build.log *.user *.suo'
    $cmd = "robocopy `"$Source`" `"$Target`" /E /NFL /NDL /NJH /NJS /NC /NS /NP $xd $xf"
    cmd /c $cmd | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE): $Source" }
    Remove-BuildArtifacts -TreeRoot $Target
}

function Write-SvnFileList {
    param([string]$TreeRoot)
    $listPath = Join-Path $TreeRoot "FILE_LIST.md"
    $files = Get-ChildItem $TreeRoot -Recurse -File |
        Where-Object { $_.Name -ne 'FILE_LIST.md' } |
        ForEach-Object { $_.FullName.Substring($TreeRoot.Length + 1).Replace('\', '/') } |
        Sort-Object

    $buildRequired = $files | Where-Object { $_ -notin @('README.md', 'svn-ignore.txt') }
    $meta = $files | Where-Object { $_ -in @('README.md', 'svn-ignore.txt') }
    $generatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# SVN フォルダ ファイル一覧（末端）")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("自動生成: ``scripts/sync-svn-export.ps1``")
    [void]$sb.AppendLine("生成日時: $generatedAt")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("| 区分 | ファイル数 |")
    [void]$sb.AppendLine("|------|-----------|")
    [void]$sb.AppendLine("| ビルド必須 | $($buildRequired.Count) |")
    [void]$sb.AppendLine("| SVN 運用メモ | $($meta.Count) |")
    [void]$sb.AppendLine("| **合計** | **$($files.Count)** |")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("パスは ``svn/`` からの相対パス。``bin/`` / ``obj/`` / 展開済み ``system_core.dic`` は含みません。")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("## ビルド必須（$($buildRequired.Count)）")
    [void]$sb.AppendLine()
    foreach ($f in $buildRequired) {
        [void]$sb.AppendLine("- ``$f``")
    }
    if ($meta.Count -gt 0) {
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("## SVN 運用メモ（$($meta.Count)）")
        [void]$sb.AppendLine()
        foreach ($f in $meta) {
            [void]$sb.AppendLine("- ``$f``")
        }
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("## ビルド後に SVN へ載せないもの")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("- 各プロジェクト配下の ``bin/`` / ``obj/``")
    [void]$sb.AppendLine("- ``tools/sudachi/resources/system_core.dic``（ZIP から展開）")
    [void]$sb.AppendLine("- ``publish/`` / ``.vs/``")

    [System.IO.File]::WriteAllText($listPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
}

Write-Host "SVN ビルド最小構成を生成: $dest"

if (Test-Path $dest) {
    Remove-Item $dest -Recurse -Force
}
New-Item -ItemType Directory -Path $dest -Force | Out-Null

Copy-Item (Join-Path $templateDir "FullTextSearch.sln") (Join-Path $dest "FullTextSearch.sln") -Force
Copy-Item (Join-Path $root "Directory.Build.props") (Join-Path $dest "Directory.Build.props") -Force
Copy-Item (Join-Path $templateDir "README.md") (Join-Path $dest "README.md") -Force
Copy-Item (Join-Path $templateDir "svn-ignore.txt") (Join-Path $dest "svn-ignore.txt") -Force

$srcDest = Join-Path $dest "src"
New-Item -ItemType Directory -Path $srcDest -Force | Out-Null
foreach ($item in @(
    'FileSearch.Blazor',
    'FileSearch.Messages',
    'FullTextSearch.Core',
    'FullTextSearch.Infrastructure'
)) {
    Copy-SourceTree -Source (Join-Path $root "src\$item") -Target (Join-Path $srcDest $item)
}
Copy-Item (Join-Path $root "src\SudachiNative.targets") (Join-Path $srcDest "SudachiNative.targets") -Force

$toolsDest = Join-Path $dest "tools\sudachi"
$sudachiSrc = Join-Path $root "tools\sudachi"
New-Item -ItemType Directory -Path (Join-Path $toolsDest "resources") -Force | Out-Null
Copy-Item (Join-Path $sudachiSrc "sudachi_ffi.dll") (Join-Path $toolsDest "sudachi_ffi.dll") -Force
foreach ($res in @('system_core.dic.zip', 'sudachi.json', 'char.def', 'unk.def')) {
    Copy-Item (Join-Path $sudachiSrc "resources\$res") (Join-Path $toolsDest "resources\$res") -Force
}

Remove-BuildArtifacts -TreeRoot $dest

if (-not (Test-Path (Join-Path $toolsDest "sudachi_ffi.dll"))) {
    throw "sudachi_ffi.dll がありません: $toolsDest"
}
if (-not (Test-Path (Join-Path $toolsDest "resources\system_core.dic.zip"))) {
    throw "system_core.dic.zip がありません: $toolsDest"
}

Write-SvnFileList -TreeRoot $dest

Write-Host "Updated: svn/FILE_LIST.md"
Write-Host "Done: $dest （ソースのみ。bin/obj は含みません）"
