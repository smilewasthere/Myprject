# MyPlayer

A personal VLC-style video player for Windows, powered by the real **mpv** engine
(libmpv) underneath — mpv's video quality and subtitle typesetting, with a
VLC-like interface, written in C# / WPF (.NET 10).

## Prerequisites

- **Windows 10/11** (the app runs only on Windows)
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

## Development notes

- Solution file: `MyPlayer.slnx` — build with `dotnet build MyPlayer.slnx`
- The project can be **compiled** on Linux (`EnableWindowsTargeting` is set),
  but **running** the app requires Windows.
- mpv configuration lives in `%APPDATA%\MyPlayer\` (`mpv.conf`, `input.conf`,
  `scripts\` for Lua) — same format as mpv itself.

## Project layout

```
scripts/fetch-mpv.ps1   downloads libmpv-2.dll into native/win-x64/
native/win-x64/         mpv engine binaries (git-ignored)
src/MyPlayer/           the WPF application
```