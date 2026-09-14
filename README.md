# WarMusic

Your mic, your music, one input.

WarMusic mixes your microphone, music from a desktop app, and local sound clips into a single microphone input for your game.

![WarMusic soundboard](docs/screenshots/soundboard.png)

## Download

Get the latest build from [Releases](https://github.com/40469420/WarMusic/releases/latest), extract the folder, and run **WarMusic.exe**.

You’ll need:

- Windows 11
- [.NET 10 Desktop Runtime — x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [VB-CABLE](https://vb-audio.com/Cable/) for routing audio into your game

## Keep control of the mix

Set separate levels for your microphone, application music, local sounds, and headphones. Voice ducking lowers the music when you speak, then brings it back afterward.

Use fades for a gradual change or **Cut music** to silence music immediately while keeping your microphone live.

## Build your soundboard

Import WAV and MP3 files, organize them into collections, and assign hotkeys. Search your library, mark favorites, queue clips, or preview a sound privately before playing it.

Your local sounds and application music share the same outgoing mix.

## Connect your audio

1. In **Audio**, choose your physical microphone and headphones.
2. Select **CABLE Input** as the virtual cable playback device.
3. In your game, select **CABLE Output** as the microphone.
4. Click **Connect** in WarMusic.
5. Start playback in your music app and select it under **Change source**.
6. Enable music when you’re ready.

Music starts muted. Your game’s push-to-talk still applies.

![WarMusic audio setup](docs/screenshots/audio.png)

## Make it yours

Adjust ducking, choose your hotkeys, and save loadouts for different setups.

![WarMusic controls](docs/screenshots/controls.png)

Tray controls, optional Windows startup, a desktop overlay, and library backups are available in **Preferences**.

![WarMusic preferences](docs/screenshots/preferences.png)

## Troubleshooting

| Problem | What to check |
| --- | --- |
| No music in the game | Check that music is enabled, WarMusic sends to CABLE Input, and the game uses CABLE Output. |
| Music is too quiet | Open **Boost & calibration**, measure the source, and apply the suggested adjustment. |
| Echo or feedback | Use headphones and keep the headphone output separate from the cable. |
| Wardogs ignores the cable | Select another microphone in the game, apply it, then select CABLE Output again. This may need repeating each session. |

See the [setup guide](docs/SETUP.md) for more details.

## Build from source

Install the .NET 10 SDK on Windows:

```powershell
dotnet publish src/WarMusic/WarMusic.csproj -c Release --self-contained false -o release
dotnet run --project tests/WarMusic.Tests -c Release
```

## About

WarMusic captures application playback through Windows, including playback from Spotify. It does not use an official Spotify integration, and neither Spotify nor VB-CABLE is bundled.

Creator links and the development disclaimer are in the app’s **Credits** tab.

[Third-party notices](docs/THIRD-PARTY.md)
