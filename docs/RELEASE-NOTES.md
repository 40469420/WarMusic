# WarMusic Windows release

WarMusic mixes a physical microphone, one application's audio, and local clips into a game-compatible virtual microphone.

## 1.0.1

- Fix a headphone-output crash that dropped the route as soon as Connect started. NAudio's `WaveBuffer` float view is a type-punned `byte[]`; `Array.Copy` rejected it and WASAPI reported "Source array type cannot be assigned to destination array type."
- Build against any .NET 10 SDK. The previous pin required `10.0.401` and failed on current 10.0.1xx installs.

## Before installing

- Windows 11 x64 is required.
- Install and enable VB-CABLE separately.
- Use headphones to prevent microphone feedback.
- Verify the EXE or ZIP against `SHA256SUMS.txt`.
- These downloads are unsigned, so Windows may show a reputation warning.

Both downloads are self-contained and need no separate .NET installation. The standalone EXE and installed copies store user data under `%LOCALAPPDATA%`; the portable ZIP keeps it beside the executable.

WarMusic contains no telemetry or automatic updater. Follow this Releases page for future versions.
