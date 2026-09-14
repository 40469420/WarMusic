# Dependency and platform maintenance

Dependabot opens grouped NuGet and GitHub Actions updates monthly. Reviewers should merge patch updates only after the Windows CI gate passes; changes affecting NAudio or WiX also require the relevant hardware or packaging checks.

## Planned checkpoints

- **Monthly:** review .NET 10 servicing patches, NuGet updates, GitHub Actions updates, and security advisories.
- **Every prerelease:** run clean Windows 11 installation and migration tests plus a short hardware preflight.
- **Before stable release:** complete the eight-hour soak and full recovery matrix.
- **May 2028:** begin the move from .NET 10 to the then-supported LTS release, leaving at least six months before .NET 10 support ends on November 14, 2028.

NAudio 2.4 is the release baseline. A move to NAudio 3 is a separate compatibility project: establish a green hardware baseline, inventory API changes, compare process-loopback and WASAPI behavior, repeat the eight-hour soak, and avoid combining it with installer or storage migrations.

Persisted-data changes must increment `Settings.CurrentSchemaVersion`, add one ordered migration step, preserve the input file before mutation, and add valid, corrupt, interrupted, and already-migrated tests.
