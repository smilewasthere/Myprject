# fetch-mpv.ps1 — Downloads the mpv engine (libmpv-2.dll) for MyPlayer
# Run once from the repo root:  powershell -ExecutionPolicy Bypass -File scripts\fetch-mpv.ps1
param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\native\win-x64")
)

$ErrorActionPreference = "Stop"

$api = "https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest"

Write-Host "Querying latest mpv Windows build..."
$release = Invoke-RestMethod -Uri $api -UseBasicParsing

# Matches mpv-dev-x86_64-<date>-... but NOT the -v3- (AVX2-only) variant
$asset = $release.assets |
    Where-Object { $_.name -match '^mpv-dev-x86_64-\d' } |
    Select-Object -First 1

if (-not $asset) {
    throw "No mpv-dev-x86_64 asset found in release $($release.tag_name)"
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) "myplayer-mpv"
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
New-Item -ItemType Directory -Path $temp | Out-Null

$archive = Join-Path $temp $asset.name
Write-Host "Downloading $($asset.name) ..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archive -UseBasicParsing

Write-Host "Extracting..."
$tar = Get-Command tar.exe -ErrorAction SilentlyContinue
if ($tar) {
    # Windows 10+ ships tar.exe (libarchive), which reads .7z
    & tar.exe -xf $archive -C $temp
    if ($LASTEXITCODE -ne 0) { throw "tar.exe failed to extract the archive" }
} else {
    $sevenZip = @(
        "C:\Program Files\7-Zip\7z.exe",
        "C:\Program Files (x86)\7-Zip\7z.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $sevenZip) {
        throw "Need tar.exe (built into Windows 10+) or 7-Zip to extract the archive"
    }
    & $sevenZip x $archive "-o$temp" -y | Out-Null
}

$dll = Get-ChildItem -Path $temp -Recurse -Filter "libmpv-2.dll" |
    Select-Object -First 1
if (-not $dll) { throw "libmpv-2.dll not found inside the archive" }

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item $dll.FullName (Join-Path $Destination "libmpv-2.dll") -Force

Write-Host ""
Write-Host "OK: libmpv-2.dll -> $Destination" -ForegroundColor Green
Write-Host "mpv version: $($release.tag_name)"
