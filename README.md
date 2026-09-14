# WarMusic

Windows mixer that combines a physical microphone with per-process application audio and local clip playback, then writes the mix to a virtual-cable playback device. The game records the matching virtual-cable capture endpoint as its microphone. In-game push-to-talk is unchanged.

Signal path:

- **Capture** — WASAPI microphone; Windows process loopback (`VAD\Process_Loopback`) on a chosen PID; local WAV/MP3
- **Mix** — 48 kHz stereo IEEE float; voice-priority ducking; optional comms-gated music send; peak limiter
- **Output** — WASAPI shared playback to the virtual cable (game) and a separate monitor device (headphones)
- **Cut** — zeros the music send and transmission gate; microphone remains live

Requires a virtual cable such as VB-CABLE. Any title that can select that cable as a microphone will work.

<p align="center">
  <img src="docs/screenshots/soundboard.png" alt="WarMusic soundboard with mixer, loadouts, and local clips" width="920">
</p>

## Architecture

WarMusic is a fixed route, not a general DAW graph.

- Application audio is captured from one process tree via Windows process loopback. There is no Spotify (or other service) login or official API.
- Microphone, application audio, and local clips are mixed on a background thread and written to **CABLE Input**. The game should use **CABLE Output** as its microphone.
- A second WASAPI output is the private monitor. Headphones and the cable must be different devices; using the same endpoint is rejected to avoid feedback.
- Local WAV/MP3 clips can be injected into the same mix. Optional RMS/peak normalization is applied per clip.
- Voice ducking attenuates application audio (and optionally clips) when the mic exceeds a threshold, when the comms key is held, or both. Attack / hold / release are configurable.
- Music send starts muted. Transmit is a separate gate from Connect.

## UI

**Audio** — physical microphone, monitor output, virtual-cable playback endpoint. Connect starts the saved route.

<img src="docs/screenshots/audio.png" alt="Audio devices and routing" width="920">

**Controls** — duck threshold/reduction, comms key, hold-to-transmit vs toggle, global hotkeys.

<img src="docs/screenshots/controls.png" alt="Voice ducking and global hotkeys" width="920">

**Preferences** — tray, auto-reconnect, startup registration, overlay, library export/restore.

<img src="docs/screenshots/preferences.png" alt="Preferences and library backup" width="920">

## Install

1. [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
2. [VB-CABLE](https://vb-audio.com/Cable/)
3. Windows x64 ZIP from [Releases](https://github.com/40469420/WarMusic/releases). Extract to a writable directory and run `WarMusic.exe`.
4. Target platform: Windows 11.

## Route

1. **Audio** — set physical mic, headphones, and `CABLE Input` as the virtual playback device.
2. In the game, set microphone to **CABLE Output**. Game PTT still gates what the game transmits.
3. **Connect**. Music send is muted until enabled.
4. Start playback in the source app. **Change source** and attach that process if it is not already captured.
5. Enable music transmit. Monitor on headphones; speaker monitoring with a live mic will feed back.

Routing notes: [docs/SETUP.md](docs/SETUP.md).

### Troubleshooting

| Symptom | Cause / action |
| --- | --- |
| Game does not pick up the cable after launch (observed on Wardogs) | Deselect CABLE, select another input, apply, then select CABLE Output again. Repeat each session. Cause unknown. |
| Double voice | Game mic is still the physical device. Set it to **CABLE Output**. |
| Monitor has music, game does not | Music send is gated. Enable **Music to game** after Connect. |
| Echo / feedback | Monitor on headphones. Do not use speakers while the mic is open. Headphones and cable must be distinct outputs. |
| Music too quiet or clipping | **Boost & calibration** measures source RMS/peak and suggests gain. Apply explicitly. |
| Unwanted source audio | **Cut music** or the panic hotkey. Clears the music send and gate; mic stays up. |

## Features

**Loadouts.** Named profiles for devices, duck settings, and the clip library.

**Voice ducking.** Application (and optionally clip) gain follows mic level and/or the comms key. Threshold and reduction are on Controls.

**Comms key.** Hold or toggle. In hold mode, music is written to the cable only while the key is down.

**Overlay.** Always-on-top status strip for borderless/windowed sessions.

**Backup.** Preferences → Export writes a `.warmusic` archive of sounds and loadouts. Restore merges; existing items are not wiped.

**Private listen / mix test.** Live monitor of the mic only. The 10 s recorded test is mic + local clips. Application audio is omitted from the recording.

## Build

Windows, [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0):

```powershell
dotnet publish src/WarMusic/WarMusic.csproj -c Release --self-contained false -o release
dotnet run --project tests/WarMusic.Tests -c Release
```

Framework-dependent, unsigned, not obfuscated. Settings, recordings, imported sounds, logs, and build caches are not in this repository.

## Scope

- Not a Spotify client, plugin, or partnership. The UI label is the captured process name.
- VB-CABLE is a separate VB-Audio install and is not bundled.
- Not a replacement for the game’s radio stack. It only feeds the microphone device the game already exposes.

Third-party notices: [docs/THIRD-PARTY.md](docs/THIRD-PARTY.md). Creator links and the in-app disclaimer are on **Credits**.
