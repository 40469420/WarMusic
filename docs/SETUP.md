# Audio setup

WarMusic supports Windows 11 x64 and requires a virtual cable such as VB-CABLE. It checks both requirements before opening the game route.

1. Install VB-CABLE directly from VB-Audio, then restart Windows if its installer requests it.
2. In **Audio**, select your physical microphone and headphones.
3. Select **CABLE Input** as the virtual-cable playback endpoint.
4. In the game, select **CABLE Output** as the microphone.
5. Click **Connect**. Voice routing begins with music muted.
6. Start playback in the source application. Under **Change source**, select and connect it.
7. Enable music in WarMusic. The game's push-to-talk still applies.

Use headphones to prevent feedback. The physical microphone, monitor output, and cable must be distinct roles. WarMusic rejects using the cable as its own microphone or monitor.

**Music to game** controls transmission level. **Boost & calibration** measures source loudness and proposes an adjustment; applying it never enables transmission. **Cut music** immediately silences music while preserving voice.

**Listen live** previews only the microphone through the private monitor. The recorded test includes microphone and local sounds, but deliberately excludes application music.

## Storage and migration

- Standalone EXE (`WarMusic-<version>-win-x64.exe`): `%LOCALAPPDATA%\WarMusic\data`
- Portable ZIP or `--portable`: `data` beside `WarMusic.exe`

A non-portable launch copies adjacent legacy data on first start only when the installed data folder is empty. The old folder is not changed or deleted. For data in any other location, use **Preferences → Export** in the old copy and **Restore** in the new copy.

Settings writes are atomic and versioned. If a settings file is corrupt or too new, WarMusic starts with safe defaults, records the problem in `diagnostics.log`, and preserves the original with a timestamped suffix.

Removing the app does not delete `%LOCALAPPDATA%\WarMusic\data`, so sounds and settings survive a reinstall. Remove that folder manually only when its contents are no longer needed.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| WarMusic says VB-CABLE is unavailable | Enable or reinstall VB-CABLE, then refresh devices. Do not select CABLE Output as the physical microphone. |
| Game does not pick up the cable after launch | Deselect CABLE Output in the game, apply another input, then select CABLE Output again. Some games require this each session. |
| Double voice | The game still uses the physical microphone. Change it to CABLE Output. |
| Monitor has music, game does not | Music transmission is muted. Enable it after Connect. |
| Echo or feedback | Use headphones and keep the monitor separate from CABLE Input. |
| Music is quiet or clips | Run Boost & calibration, apply the suggestion, and watch the limiter indicator. |
| A device or source disappears | Leave automatic recovery enabled. Recovery always returns with music muted. |

WarMusic does not update itself or contact a service. Check the public GitHub Releases page for newer versions.
