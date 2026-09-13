# WarMusic 1.0.0

A Windows soundboard and application-audio mixer for game voice chat, designed for Arma and Wardogs players.

## Run

Download the Windows x64 ZIP from Releases, extract the whole folder somewhere writable, and open WarMusic.exe. Install the .NET 10 Desktop Runtime (x64) and VB-CABLE first if needed. Windows 11 is recommended.

- Runtime: https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- Cable: https://vb-audio.com/Cable/

## Features

Local WAV/MP3 soundboard, collections, favorites, queue, hotkeys and private previews. Selected-application capture, microphone/music mixer, source boost, calibration, peak limiting, voice ducking, fades, panic cut, loadouts, reconnection, tray, optional startup, overlay and library backups.

Application capture uses Windows process loopback. It is not a Spotify API integration or endorsement. Spotify and VB-CABLE are not bundled.

## Build

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet publish src/WarMusic/WarMusic.csproj -c Release --self-contained false -o release
dotnet run --project tests/WarMusic.Tests -c Release
```

See docs/SETUP.md for routing details and docs/THIRD-PARTY.md for dependency notices. Creator links and the project disclaimer are in the app Credits tab.

The release is compiled, framework-dependent, unsigned, and not obfuscated. Local settings, recordings, imported sounds, logs and build caches are excluded from this repository.
