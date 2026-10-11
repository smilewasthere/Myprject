# publish.ps1 — Build release artifacts for MPLC (64-bit only).
#
# Produces, under dist/:
#   MPLC-<version>-win-x64.zip        portable zip (extract & run MPLC.exe)
#   MPLC-Setup-<version>.exe          installer (install/uninstall) — if Inno
#                                     Setup is installed (best-effort)
#
# Why 64-bit only: the mpv engine this app links (shinchiro/mpv-winbuild,
# libmpv-2.dll) ships no 32-bit build, and Windows 11 is 64-bit-only.
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

    # Portable zip
    $zip = Join-Path $repo "$OutDir\MPLC-$version-$Rid.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$publish\*" -DestinationPath $zip
    Write-Host "Built portable zip: $zip" -ForegroundColor Green

    # Installer (Inno Setup) — best-effort; skip silently if not installed.
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (Test-Path $iscc) {
        Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Cyan
        & $iscc "/DSourceDir=$repo" "/DPublishDir=$publish" "/DAppVersion=$version" `
                "$repo\installer\mplc.iss"
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed ($LASTEXITCODE)" }
        Write-Host "Built installer: $(Join-Path $repo "$OutDir\MPLC-Setup-$version.exe")" -ForegroundColor Green
    } else {
        Write-Host "Inno Setup not found — skipping installer build." -ForegroundColor Yellow
        Write-Host "Install it with:  choco install innosetup -y" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "Upload these to GitHub Releases (or push a v$version tag):" -ForegroundColor Green
    Write-Host "  $zip" -ForegroundColor Green
}
finally {
    Pop-Location
}

