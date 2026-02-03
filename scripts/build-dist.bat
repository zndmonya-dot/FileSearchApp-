@echo off
chcp 65001 >nul
setlocal
set "ROOT=%~dp0.."
set "PUBLISH_DIR=%ROOT%\publish\win10-x64"
set "DIST_DIR=%ROOT%\installers\dist"
set "ZIP_NAME=FileSearch_win-x64.zip"

echo Building standalone exe for distribution...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
mkdir "%PUBLISH_DIR%"

dotnet publish "%ROOT%\src\FileSearch.Blazor\FileSearch.Blazor.csproj" -f net8.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None --self-contained true -o "%PUBLISH_DIR%"
if errorlevel 1 exit /b 1

if exist "%ROOT%\installers\社内配布\インストール手順.txt" copy /y "%ROOT%\installers\社内配布\インストール手順.txt" "%PUBLISH_DIR%\インストール手順.txt"

if not exist "%PUBLISH_DIR%\sudachi_tokenize.py" (
  echo WARNING: sudachi_tokenize.py not in publish output.
)

if not exist "%DIST_DIR%" mkdir "%DIST_DIR%"
set "ZIP_PATH=%DIST_DIR%\%ZIP_NAME%"
if exist "%ZIP_PATH%" del "%ZIP_PATH%"

powershell -NoProfile -Command "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_PATH%' -Force"

echo Done: %ZIP_PATH%
echo Share this ZIP; users extract and run FileSearch.Blazor.exe
exit /b 0
