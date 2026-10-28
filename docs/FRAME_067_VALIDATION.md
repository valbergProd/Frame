# FRAME 0.6.7 validation — GitHub account rename

Date: 2026-10-28. The user changed their GitHub username to `valbergProd` and requested updated Git/update addresses and a new build.

## Changes

- Canonical repository: https://github.com/valbergProd/Frame. Verified authenticated account identity remains user ID 177798418 and the repository remains public.
- Updated app repository constant, updater diagnostics/test fixtures, release publisher and operational documentation. Local origin and repository-local commit identity use the new username; existing commits are unchanged.
- Version 0.6.7-beta. No recording, audio, settings, installer or update-policy changes. Old validation reports retain dated evidence under the former username.

## Validation

- Release solution build: passed, zero warnings/errors.
- Core regression: 159 passed, zero failed, including updater cases using `valbergProd/Frame`.
- Self-contained publish: passed, no warnings/errors reported.
- Final EXE update UI diagnostics: 6/6; Dark-mode render visually checked, including the 0.6.7-beta version label.
- Built Core updater queried `valbergProd/Frame` without credentials: success, no newer installer available (the release feed is currently empty).
- Release publisher PowerShell syntax check: passed. No old owner remains in active source, tests, scripts or operational update links.

Evidence directory: `work/rename-067` (local, ignored). All diagnostic settings and output use that directory; user data and the running app are untouched.

## Build and limits

New standalone build: `C:\dev\Frame\builds\FRAME-0.6.7-beta-valbergprod-20261028\FRAME.exe`. Self-contained Windows x64 with .NET 8.0.32. Previous builds are retained.

Size: 182,146,569 bytes. SHA-256: `BC4429EE729353CD099B1443352A349471E83F97B25CAF357136E9388FA36FD0`. Product version: `0.6.7-beta+94a77ab89dd59e46f70eb9c713727a1fad77270a`; the suffix identifies the pre-change checkout HEAD because the build was produced before committing these changes.

No installer, tag or GitHub release is created. A real newer-installer upgrade remains unqualified until setup is prepared. This address-only change does not repeat recording/media acceptance tests; see 0.6.5 and 0.6.6 reports for their earlier evidence.
