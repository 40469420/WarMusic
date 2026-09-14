# Release operations

Current Windows builds are published on this repository's [Releases](https://github.com/40469420/WarMusic/releases) page. Do not copy source into a separate distribution repository unless that split is explicitly provisioned.

## Current assets

Each public release should include:

- `WarMusic-<version>-win-x64.exe` — standalone, self-contained, single-file Windows x64 app
- `WarMusic-<version>-win-x64-portable.zip` — portable folder with a `WarMusic.portable` marker
- `SHA256SUMS.txt`
- `provenance.json`

Both application packages need no separate .NET installation. Current 1.0.x builds are unsigned, so Windows may show a reputation warning. Verify every file against `SHA256SUMS.txt`.

The standalone EXE and other non-portable launches store data under `%LOCALAPPDATA%\WarMusic\data`. The portable ZIP stores data beside `WarMusic.exe`.

An MSI installer exists in `packaging/` but is **not** part of the 1.0.x downloads. Building it requires `WARMUSIC_WIX_EULA_ID=wix7` after reviewing the [WiX 7 OSMF terms](https://docs.firegiant.com/wix/osmf/).

## Local packaging

`build/Build-Release.ps1` restores locked packages and emits the portable ZIP, checksums, and provenance. It also emits an MSI when the WiX EULA variable is set. It does not currently emit the standalone single-file EXE.

Portable ZIP (unsigned):

```powershell
pwsh ./build/Build-Release.ps1 -Version 1.0.2 -SkipInstaller
```

The script refuses a stable SemVer without a signing certificate. 1.0.0, 1.0.1, and 1.0.2 were published unsigned to match the previous public layout; keep that exception explicit when repeating it.

Standalone EXE (matches the 1.0.x Releases asset):

```powershell
dotnet publish src/WarMusic/WarMusic.csproj -c Release -r win-x64 --self-contained true -p:Version=1.0.2 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Rename the published `WarMusic.exe` to `WarMusic-<version>-win-x64.exe` and include it in `SHA256SUMS.txt` and `provenance.json`.

For signed builds, set `WARMUSIC_SIGNING_CERTIFICATE_PATH` and `WARMUSIC_SIGNING_CERTIFICATE_PASSWORD`. Store the same material in GitHub as `WINDOWS_SIGNING_CERTIFICATE_BASE64` and `WINDOWS_SIGNING_CERTIFICATE_PASSWORD` if the Release workflow should sign.

## Optional later split

`distribution-repository/` is a template for a binaries-only public repo. It is not live. If that split is created later:

1. Copy `distribution-repository/` into the new repo.
2. Add a fine-grained `RELEASES_REPOSITORY_TOKEN` with Contents: write on that repo only.
3. Optionally set `RELEASES_REPOSITORY` to override the default destination.

## Release gate

1. CI is green with zero Release warnings and locked restore.
2. The EXE and ZIP launch on Windows 11 x64 without a separately installed .NET runtime.
3. Installed/standalone and portable storage modes both work, including first-launch legacy copy and Export/Restore.
4. Valid, corrupt, interrupted, and already-migrated data scenarios preserve recoverable data.
5. The hardware preflight in `docs/HARDWARE-TESTING.md` passes; complete the eight-hour soak before calling a build fully qualified.
6. `SHA256SUMS.txt` matches every package. Authenticode is valid when a certificate is configured.

Tag the commit as `<version>` (for example `1.0.2`) on `main` and attach the four assets to the GitHub Release. The automated Release workflow still expects a `v*` tag, WiX acceptance, and a distribution-repo token; do not use it until those are actually configured.
