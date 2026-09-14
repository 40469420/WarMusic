# Release operations

The source repository builds on Windows and publishes binary-only assets to `40469420/WarMusic-Releases`. Do not copy source or source-repository history into the distribution repository.

## One-time owner setup

1. Create the public `WarMusic-Releases` repository with a default branch, then copy the contents of `distribution-repository/` into it.
2. Enable Issues and private vulnerability reporting in that public repository.
3. In the private source repository, add a fine-grained `RELEASES_REPOSITORY_TOKEN` secret with **Contents: write** access only to `WarMusic-Releases`.
4. Optionally set `RELEASES_REPOSITORY` to override the default destination.
5. Review the [WiX 7 OSMF terms](https://docs.firegiant.com/wix/osmf/), confirm any fee obligation, and only then set the source repository variable `WIX_EULA_ID` to `wix7`. The workflow will not build an MSI without this explicit owner action.
6. For signed releases, store a base64-encoded PFX as `WINDOWS_SIGNING_CERTIFICATE_BASE64` and its password as `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`.
7. Apply the `main` protection settings in `docs/REPOSITORY-SETTINGS.md`.

## Build behavior

`build/Build-Release.ps1` restores locked packages and emits:

- `WarMusic-<version>-win-x64-setup.msi`
- `WarMusic-<version>-win-x64-portable.zip`
- `SHA256SUMS.txt`
- `provenance.json`

Both application packages are Windows x64 self-contained deployments. The script signs `WarMusic.exe` before creating either package and signs the final MSI when certificate variables are present. It also embeds privacy, setup, NAudio, .NET license, and third-party-notice files.

For a local unsigned prerelease ZIP only:

```powershell
./build/Build-Release.ps1 -Version 1.0.1-rc.1 -SkipInstaller
```

Building the MSI requires `WARMUSIC_WIX_EULA_ID=wix7`. Stable versions without a signing certificate are rejected by the release workflow.

## Release gate

1. CI is green with zero Release warnings and locked restore.
2. The MSI and ZIP launch on a clean Windows 11 x64 VM without a separately installed .NET runtime.
3. Install, upgrade, uninstall, prior-version rollback, and both storage modes pass.
4. Valid, corrupt, interrupted, and already-migrated data scenarios preserve recoverable data.
5. The eight-hour soak and recovery matrix in `docs/HARDWARE-TESTING.md` pass.
6. `SHA256SUMS.txt` matches every package and Authenticode is valid when configured.
7. Unsigned builds use a prerelease SemVer suffix. Only signed, fully qualified builds receive a stable version.

Push a `v<version>` tag in the private source repository after the gates pass. The Release workflow rebuilds and tests from that tag, creates checksums and provenance, and publishes all four assets to the public distribution repository.
