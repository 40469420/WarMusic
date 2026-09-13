# WarMusic

You already have a mic, a music player, and a game that only hears one of them.

WarMusic sits between those three. It mixes your voice with whatever you’re playing — Spotify, a local file, a radio clip — and sends that mix into the virtual cable your game uses as a microphone. Squad radio still uses your push-to-talk. Music ducks when you talk. There’s a panic cut for the moment something loud and stupid starts playing mid-brief.

Built for long Arma / Wardogs nights. Works in anything that will take VB-CABLE as a mic.

<p align="center">
  <img src="docs/screenshots/soundboard.png" alt="WarMusic soundboard with mixer, loadouts, and local clips" width="920">
</p>

## What it actually does

Most “play music in game” setups turn into a VoiceMeeter graph you have to relearn every few months. This one is meant to stay out of the way after the first setup.

- Grab audio from a specific app (Windows process loopback — not a Spotify login, not an official integration)
- Mix that with your real microphone
- Send the mix to **CABLE Input**, which the game hears as **CABLE Output**
- Keep a private headphone mix so you can hear yourself without blasting the room
- Drop in local WAV/MP3 clips for radio checks, convoy beds, memes, whatever your unit actually uses
- Duck music when you speak, fade it, cut it, or match clip loudness so one sound doesn’t nuke the rest

Voice always wins. If the music is in the way, hit **Cut music**.

## The rest of the app

**Audio** — real mic, headphones, virtual cable. Test the route before you brief.

<img src="docs/screenshots/audio.png" alt="Audio devices and routing" width="920">

**Controls** — voice ducking, comms key, hold-to-transmit, and the hotkeys you’ll actually remember.

<img src="docs/screenshots/controls.png" alt="Voice ducking and global hotkeys" width="920">

**Preferences** — tray, auto-reconnect, startup, overlay, and a backup of the whole library.

<img src="docs/screenshots/preferences.png" alt="Preferences and library backup" width="920">

## Get it running

1. Install the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) if Windows doesn’t already have it.
2. Install [VB-CABLE](https://vb-audio.com/Cable/).
3. Grab the Windows x64 ZIP from [Releases](https://github.com/40469420/WarMusic/releases), extract the whole folder somewhere you can write to, and run `WarMusic.exe`.
4. Windows 11 is the happy path.

Then the five-minute route:

1. **Audio** tab — physical mic, headphones, `CABLE Input` as the virtual playback endpoint.
2. In the game, set your microphone to **CABLE Output**. Your PTT still applies.
3. Hit **Connect**. Music starts muted on purpose.
4. Play something in your music app. **Change source** on the Soundboard and attach that app if it isn’t already.
5. Turn music on when you’re ready. Use headphones or you’ll hear yourself in the mic.

More routing notes live in [docs/SETUP.md](docs/SETUP.md).

### Troubleshooting

| Symptom | Try this |
| --- | --- |
| Wardogs players!!! | You MUST deselect CABLE, go to another input, apply, then back to CABLE. EVERY START. This is not optional and I don't know what causes it. |
| Squad hears you twice | Game mic is still the physical one. Switch it to **CABLE Output**. |
| Music in your ears but not in game | Music stays muted until you enable transmit. Check **Music to game**. |
| Feedback / echo | Headphones. Don’t monitor speakers while the mic is live. |
| Music buried or clipping | **Boost & calibration** on the mixer. Measure, then apply the suggested gain. |
| Something awful starts playing | **Cut music** (top right) or the panic hotkey. Voice stays up. |

## Things people usually want

**Loadouts.** Wardogs on one, a milsim unit on another. Devices, ducking, and the clip set travel with the loadout.

**Voice ducking.** Music drops when you talk. Threshold and reduction live on Controls. There’s a separate toggle for ducking local clips.

**Comms key.** Hold CapsLock (or whatever you bind) if you only want music in the cable while you’re keyed. Toggle mode is there if you’d rather just leave it on.

**Overlay.** Tiny always-on-top strip you can drag. Useful in borderless / windowed games.

**Backup.** Preferences → Export writes a `.warmusic` file with sounds and loadouts. Restore merges. It doesn’t wipe what you already have.

**Private listen.** “Listen live” is just your mic. The 10-second test mix is voice + local clips. Application music is left out so you don’t record a track you don’t own.

## Build it yourself

Windows, [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0):

```powershell
dotnet publish src/WarMusic/WarMusic.csproj -c Release --self-contained false -o release
dotnet run --project tests/WarMusic.Tests -c Release
```

The published build is framework-dependent, unsigned, and not obfuscated. Settings, recordings, imported sounds, logs, and build caches stay off this repo.

## What this is not

- Not a Spotify client, plugin, or partnership. The app name in the UI is just the process it captured.
- VB-CABLE is a separate install from VB-Audio. Nothing from them is bundled here.
- Not a replacement for your game’s radio. It only feeds the microphone the game already has.

Third-party notices are in [docs/THIRD-PARTY.md](docs/THIRD-PARTY.md). Creator links and the in-app disclaimer sit on the **Credits** tab.

If it helps your squad hear the same song at the same time without someone sharing a desktop, it’s doing the job.
