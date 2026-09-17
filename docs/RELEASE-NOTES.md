# WarMusic Windows release

WarMusic mixes a physical microphone, one application's audio, and local clips into a game-compatible virtual microphone.

## 1.0.3 - Quick setup and clearer routing

A four-step setup walks you through your microphone, headphones, virtual cable, and the matching input to select in your game. Choices are saved at the end, and setup can be reopened from the Audio tab. The Soundboard now separates application audio being received, music being enabled or muted, and audio being sent to the cable. Silent sources and headphones-only playback are easier to spot, and the status no longer implies the game is receiving audio before you check its mic test.

## 1.0.2

- Smaller window and a layout that reflows: the header wraps, and the mixer stacks under the soundboard on a narrow or tall layout.
- Import a whole folder of WAV and MP3 files, including subfolders. Collections follow the folder names. Import shows progress and can be cancelled.
- Virtualized sound list so large libraries stay responsive.
- Credits tab lists testers and collaborators.

## 1.0.1

- Fix a headphone-output crash that dropped the route as soon as Connect started. NAudio's `WaveBuffer` float view is a type-punned `byte[]`; `Array.Copy` rejected it and WASAPI reported "Source array type cannot be assigned to destination array type."
- Build against any .NET 10 SDK. The previous pin required `10.0.401` and failed on current 10.0.1xx installs.
- **Open full setup guide** on the Audio tab opens the GitHub SETUP.md instead of a local file.

## Before installing

- Windows 11 x64 is required.
- Install and enable VB-CABLE separately.
- Use headphones to prevent microphone feedback.
- Verify the EXE or ZIP against `SHA256SUMS.txt`.
- These downloads are unsigned, so Windows may show a reputation warning.

Both downloads are self-contained and need no separate .NET installation. The standalone EXE and installed copies store user data under `%LOCALAPPDATA%`; the portable ZIP keeps it beside the executable.

WarMusic contains no telemetry or automatic updater. Follow this Releases page for future versions.
