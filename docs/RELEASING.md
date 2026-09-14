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

- `WarMusic-<version>-win-x64.exe`
- `WarMusic-<version>-win-x64-setup.msi`
- `WarMusic-<version>-win-x64-portable.zip`
- `SHA256SUMS.txt`
- `provenance.json`

All three application packages are Windows x64 self-contained deployments. The standalone EXE is a .NET single-file publish; the ZIP adds documentation and a portable-mode marker around that same executable. The script signs `WarMusic.exe` before creating the standalone and portable artifacts and signs the final MSI when certificate variables are present. The ZIP and MSI also include privacy, setup, NAudio, .NET license, and third-party-notice files.

For local unsigned EXE and ZIP packages:

```powershell
./build/Build-Release.ps1 -Version 1.0.0 -SkipInstaller
```

Building the MSI requires `WARMUSIC_WIX_EULA_ID=wix7`. Code signing is optional for both stable and prerelease versions. Unsigned downloads can trigger Windows reputation warnings; the ZIP and MSI payload include `UNSIGNED-BUILD.txt`.

## Release gate

1. CI is green with zero Release warnings and locked restore.
2. The EXE, MSI, and ZIP launch on a clean Windows 11 x64 VM without a separately installed .NET runtime.
3. Install, upgrade, uninstall, prior-version rollback, and both storage modes pass.
4. Valid, corrupt, interrupted, and already-migrated data scenarios preserve recoverable data.
5. The eight-hour soak and recovery matrix in `docs/HARDWARE-TESTING.md` pass.
6. `SHA256SUMS.txt` matches every package and Authenticode is valid when configured.
7. The release notes clearly identify unsigned packages so users know to verify `SHA256SUMS.txt` and expect Windows reputation warnings.

Push a `v<version>` tag in the private source repository after the gates pass. The Release workflow rebuilds and tests from that tag, creates checksums and provenance, and publishes all five assets to the public distribution repository.
