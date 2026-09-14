# Windows hardware qualification

Run these gates on Windows 11 x64 with a physical microphone, headphones, and VB-CABLE. Never use speakers during a live-microphone test.

## Deterministic and device checks

```powershell
dotnet test tests/WarMusic.UnitTests/WarMusic.UnitTests.csproj -c Release
dotnet run --project tests/WarMusic.HardwareTests -c Release -- --capture-test --isolation-test --engine-test --routing-regression --recovery-test
```

The first command is required in CI. The second is intentionally manual because it opens real audio endpoints and process-loopback capture.

To confirm the headphone WASAPI path stays up on the devices saved in the app (monitor gain is forced to 0):

```powershell
dotnet run --project tests/WarMusic.HardwareTests -c Release -- --list-devices
dotnet run --project tests/WarMusic.HardwareTests -c Release -- --route-smoke --mic "<microphone-id>" --monitor "<headphones-id>" --cable "<CABLE Input-id>"
```

## Eight-hour soak

List active endpoints and copy the three required IDs:

```powershell
dotnet run --project tests/WarMusic.HardwareTests -c Release -- --list-devices
```

Run the soak with an actual physical microphone and headphones:

```powershell
dotnet run --project tests/WarMusic.HardwareTests -c Release -- --soak-test --hours 8 --mic "<microphone-id>" --monitor "<headphones-id>" --cable "<CABLE Input-id>"
```

Use `--minutes 2` for a preflight run. The harness continuously exercises physical-microphone capture, per-process capture, clip playback, private preview, ducking, panic/mute, and monitoring. It records JSON-lines diagnostics in the printed temporary results folder.

The soak fails if the route or source stops, a buffer exceeds its 250 ms bound, a render callback exceeds 30 ms, or CABLE Output contains a detected gap longer than 30 ms. A passing run must also show cable and render activity and finish with music muted.

## Recovery matrix

During separate manual sessions, verify each event below. After every automatic recovery, music transmission must remain muted until deliberately enabled.

| Scenario | Required result |
| --- | --- |
| Sleep and resume | Route, microphone, monitor, and source reconnect; music stays muted. |
| Remove and reconnect microphone | Error is visible, retry is bounded, route returns muted. |
| Remove and reconnect headphones | No crash or feedback; route returns muted. |
| Change Windows default input/output | Saved explicit endpoints remain in use; missing endpoints recover safely. |
| Stop and restart target process | New process ID is captured; music stays muted. |
| Disable and re-enable VB-CABLE | Route stops safely and later returns muted. |

Record the Windows build, device drivers, endpoint formats, WarMusic commit, soak results path, and pass/fail outcome in the release issue. A release cannot be marked stable without a completed eight-hour run and this recovery matrix.
