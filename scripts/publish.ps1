# publish.ps1 — Build the release installer for MPLC (64-bit only).
#
# Produces:  dist\MPLC-Setup-<version>.exe   (install / uninstall installer)
#
# Why 64-bit only: the mpv engine this app links (shinchiro/mpv-winbuild,
# libmpv-2.dll) ships no 32-bit build, and Windows 11 is 64-bit-only.
#
# Requires Inno Setup for the installer step:  choco install innosetup -y
#
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File scripts\fetch-mpv.ps1   # once, for libmpv-2.dll
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Rid = "win-x64",
    [string] $OutDir = "dist"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $engine = Join-Path $repo "native\$Rid\libmpv-2.dll"
    if (-not (Test-Path $engine)) {
        Write-Host "libmpv-2.dll not found at $engine" -ForegroundColor Yellow
        Write-Host "Fetching the mpv engine first..." -ForegroundColor Yellow
        & (Join-Path $PSScriptRoot "fetch-mpv.ps1")
    }

    $publish = Join-Path $repo "$OutDir\$Rid\publish"
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

    # Self-contained FOLDER publish (not single-file): MPLC loads libmpv-2.dll
    # via P/Invoke, which is unreliable from a single-file bundle.
    Write-Host "Publishing MPLC ($Rid, self-contained folder)..." -ForegroundColor Cyan
    dotnet publish "src\MyPlayer\MyPlayer.csproj" `
        -c $Configuration -r $Rid --self-contained true -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    # The mpv engine DLL must sit next to the exe (P/Invoke loads it by name).
    Copy-Item $engine -Destination $publish -Force

    $version = (Get-Content "src\MyPlayer\MyPlayer.csproj" |
        Select-String -Pattern '<Version>(.*?)</Version>').Matches.Groups[1].Value
    if (-not $version) { $version = "1.0.0" }

    # Installer (Inno Setup) — the single .exe users download.
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) {
        throw "Inno Setup not found at '$iscc'. Install it with: choco install innosetup -y"
    }
    Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Cyan
    & $iscc "/DSourceDir=$repo" "/DPublishDir=$publish" "/DAppVersion=$version" `
            "$repo\installer\mplc.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed ($LASTEXITCODE)" }

    $setup = Join-Path $repo "$OutDir\MPLC-Setup-$version.exe"
    Write-Host ""
    Write-Host "Built installer:" -ForegroundColor Green
    Write-Host "  $setup" -ForegroundColor Green
    Write-Host "Double-click to install; uninstall via Add/Remove Programs." -ForegroundColor DarkGray
    Write-Host "(Or push tag v$version to build & attach it to a GitHub Release.)" -ForegroundColor DarkGray
}
finally {
    Pop-Location
}

