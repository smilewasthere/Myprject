# publish.ps1 — Build a release package for MPLC (64-bit only).
#
# Produces a self-contained, single-file MPLC.exe plus the mpv engine DLL
# under dist/win-x64/, ready to upload to GitHub Releases.
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

    Write-Host "Publishing MPLC ($Rid, self-contained single-file)..." -ForegroundColor Cyan
    dotnet publish "src\MyPlayer\MyPlayer.csproj" `
        -c $Configuration `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    # The mpv engine DLL must sit next to the exe (P/Invoke loads it by name).
    Copy-Item $engine -Destination $publish -Force

    $version = (Get-Content "src\MyPlayer\MyPlayer.csproj" |
        Select-String -Pattern '<Version>(.*?)</Version>').Matches.Groups[1].Value
    if (-not $version) { $version = "1.0.0" }

    $stage = Join-Path $repo "$OutDir\$Rid\MPLC-$version-$Rid"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage | Out-Null
    Get-ChildItem $publish | Copy-Item -Destination $stage -Recurse

    $zip = Join-Path $repo "$OutDir\MPLC-$version-$Rid.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$stage\*" -DestinationPath $zip

    Write-Host ""
    Write-Host "Done. Upload this to GitHub Releases:" -ForegroundColor Green
    Write-Host "  $zip" -ForegroundColor Green
    Write-Host "(portable zip — extract anywhere and run MPLC.exe)" -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
