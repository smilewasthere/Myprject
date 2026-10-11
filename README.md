# MPLC

A personal VLC-style video player for Windows, powered by the real **mpv** engine
(libmpv) underneath — mpv's video quality and subtitle typesetting, with a
VLC-like interface, written in C# / WPF (.NET 10).

> **Name:** MPLC ("MPV + VLC"). Personal project — not affiliated with mpv or VLC.

## Download & install (for users)

Grab the latest installer from the **[Releases](../../releases)** page:

1. Download **`MPLC-Setup-<version>.exe`** (the single file under "Assets").
2. Double-click it and follow the installer (Next → Install).
3. Launch **MPLC** from the Start Menu or Desktop shortcut.

**Uninstall** like any normal program: **Settings → Apps → Installed apps → MPLC → Uninstall**
(or **Control Panel → Programs and Features**). This removes MPLC completely.

> The same page also offers **Source code (zip / tar.gz)** if you'd rather build it yourself.

> **64-bit only.** MPLC links the mpv engine (shinchiro/mpv-winbuild, `libmpv-2.dll`),
> which publishes no 32-bit build, and Windows 11 is 64-bit-only — so there is no
> 32-bit download.

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

## Making a release (64-bit)

```powershell
# Builds the single installer:  dist\MPLC-Setup-<version>.exe
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php)
(`installer\mplc.iss`). If you don't have it: `choco install innosetup -y`.

A GitHub Actions workflow (`.github/workflows/release.yml`) builds the same
installer automatically and attaches it to a GitHub Release when you push a
version tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The release page then shows **one `.exe`** (`MPLC-Setup-1.0.0.exe`) plus GitHub's
automatic **Source code (zip / tar.gz)** links.

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
scripts/publish.ps1     builds the zip + installer under dist/
scripts/make-icon.py    regenerates the app icon (MPLC.ico)
installer/mplc.iss      Inno Setup script (install/uninstall installer)
native/win-x64/         mpv engine binaries (git-ignored)
src/MyPlayer/           the WPF application (ships as MPLC.exe)
```

## Contributors

- **[smilewasthere](https://github.com/smilewasthere)** — creator & maintainer

This is a personal project. Contributions aren't actively sought, but issues
and ideas are welcome via [Issues](../../issues).

## Credits & license

- **mpv / libmpv** — the video engine. GPL-2.0-or-later / LGPL. <https://mpv.io>
- **libass** — subtitle typesetting, used through mpv. ISC.
- **HanumanInstitute.LibMpv** — .NET binding for libmpv. MIT.
- **Inno Setup** — builds the Windows installer. Freeware (JR Software).
- App icon and MPLC source code — personal use; no third-party assets.

MPLC is not affiliated with or endorsed by the mpv or VideoLAN (VLC) projects.
