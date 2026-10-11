# MPLC

A personal VLC-style video player for Windows, powered by the real **mpv** engine
(libmpv) underneath — mpv's video quality and subtitle typesetting, with a
VLC-like interface, written in C# / WPF (.NET 10).

> **Name:** MPLC ("MPV + VLC"). Personal project — not affiliated with mpv or VLC.

## Features

- VLC-style dark interface: menu bar, toolbar, right-click track menus
- Highest-quality playback via mpv (gpu-next / libplacebo, HDR, debanding)
- Full subtitle typesetting (ASS/SSA) + a live Subtitle Settings panel
- Right-click / hotkey audio & subtitle track switching (any number of tracks)
- Volume up to 300%, audio-device selection, audio/subtitle delay
- Open Network Stream (Ctrl+N), resume-where-you-stopped, docked playlist
- mpv-native config: plain `mpv.conf` + `input.conf` in `%APPDATA%\MPLC\`

## Prerequisites

- **Windows 10/11, 64-bit** (the app runs only on Windows)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

## First-time setup

```powershell
git clone https://github.com/smilewasthere/Myprject.git
cd Myprject

# Download the mpv engine DLL (once, ~40 MB)
powershell -ExecutionPolicy Bypass -File scripts\fetch-mpv.ps1

# Build and run
dotnet run --project src\MyPlayer
```

`libmpv-2.dll` is placed in `native\win-x64\` and copied to the build output
automatically. It is git-ignored; re-run the fetch script on a fresh clone.

## Making a release package (64-bit)

```powershell
# Produces dist\MPLC-<version>-win-x64.zip (portable: extract & run MPLC.exe)
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

A GitHub Actions workflow (`.github/workflows/release.yml`) builds the same
package automatically and attaches it to a GitHub Release when you push a
version tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

> **64-bit only.** The mpv engine MPLC links (shinchiro/mpv-winbuild, `libmpv-2.dll`)
> publishes no 32-bit build, and Windows 11 is 64-bit-only — so there is no
> 32-bit download.

## App icon

`src/MyPlayer/MPLC.ico` is generated locally by `scripts/make-icon.py`
(a pure-Python, public-domain play-button mark — no third-party assets, so no
licensing baggage). Regenerate after edits:

```powershell
python scripts\make-icon.py
```

## Development notes

- Solution file: `MyPlayer.slnx` — build with `dotnet build MyPlayer.slnx`
- The project can be **compiled** on Linux (`EnableWindowsTargeting` is set),
  but **running** the app requires Windows.
- mpv configuration lives in `%APPDATA%\MPLC\` (`mpv.conf`, `input.conf`,
  `scripts\` for Lua) — same format as mpv itself.

## Project layout

```
scripts/fetch-mpv.ps1   downloads libmpv-2.dll into native/win-x64/
scripts/publish.ps1     builds dist\MPLC-<version>-win-x64.zip
scripts/make-icon.py    regenerates the app icon (MPLC.ico)
native/win-x64/         mpv engine binaries (git-ignored)
src/MyPlayer/           the WPF application (ships as MPLC.exe)
```
