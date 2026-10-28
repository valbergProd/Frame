# FRAME 0.6.6 — GitHub source and in-app updates (2026-10-28)

## Scope

The user requested a GitHub upload before preparing a new installer, explicitly selected in-app updates now, and authorized public source and public updates on `torsteinvalberg`. Source and releases use one repository: https://github.com/torsteinvalberg/Frame. GitHub authentication was renewed interactively by the user. No credential is stored in the app or repository.

The project now has Git exclusions for build outputs, installed beta copies, archived packages, capture fixtures, generated diagnostic screenshots/logs, private handover history and credentials. Required source images, Windows app code, tests and packaging tools remain included. The outdated README is preserved as dated history; the current README and update workflow describe this product only.

## Implementation

- Settings > General displays the current version, manual update check, optional once-per-startup check, download/cancel and install actions. A saved startup-check preference defaults on. Isolated `--data` diagnostics skip the startup network request.
- The updater reads the configured repository's public Releases API. Stable installations exclude beta releases. Beta installations can move to newer betas or stable; stable supersedes beta at the same numeric version. Drafts, old/current versions and releases without installers are ignored. Versions are compared numerically, independently of release dates.
- Only an installer with the expected filename, exact repository/tag download URL, bounded size and GitHub SHA-256 digest is accepted. Download uses a new partial file, checks actual byte count and hash, and commits it only after validation. Cancel/failure removes its partial file, without replacing existing downloads. Progress is throttled. No automatic download or silent installation.
- Install requires capture/actions/exports to be idle and editors closed. After user confirmation, a separate FRAME helper waits for the old process to exit, revalidates size/hash/product/version, then opens ordinary Inno Setup. Parent process identity includes its start timestamp. The helper does not load settings, acquire the main app mutex, capture, or register hotkeys. The existing setup AppId and settings/recording preservation behavior are unchanged.
- `scripts/Publish-GitHubRelease.ps1` validates local installer metadata, requires a clean checkout and a matching pushed source tag, creates a draft release, verifies GitHub's uploaded digest/size, and publishes only with explicit `-Publish`. It never replaces an existing release. `-Preview` performs only local validation.
- `.github/workflows/build.yml` builds on Windows and runs Core tests on pushes/PRs; official actions are pinned to commit IDs. Publishing a source commit alone never distributes an installer.

## Validation

- Release builds: zero warnings/errors. **159/159 Core tests**, including 18 new update cases: channel/version ordering, malformed versions, drafts, missing installers, invalid metadata, wrong host, hash tampering, truncation, cancellation, existing partial files, public API targeting, unavailable/rate-limited feeds and saved settings.
- Final self-contained executable: **7/7 installer handoff checks**. Uses an existing 0.5.4 installer copied into an isolated folder, but intercepts the final launch boundary: **no installer ran and no installation changed**. Verifies accepted metadata, wrong digest/length/version rejection, path mismatch rejection, waiting while the real parent is alive, and successful validation after parent exit. Inno Setup's padded product-name resource was found and correctly trimmed before final publish.
- Final executable UI diagnostics: **6/6 in Dark and 6/6 in Light**. Verify idle/available/downloading/downloaded states and action-gate installation guard. Both final renders were inspected: labels, checkbox and buttons fit and remain readable. No actual network or capture in these renders.
- Release-script parser and `-Preview` passed against the existing 0.5.4 installer, including its SHA-256/version check. No release was created by the preview.
- Total local validation: **178/178 checks**. Evidence: `work/github-066/core-tests.txt`, `installer-test/result.json`, `installer-test/handoff.json`, `ui-dark/updates.png.checks.json`, `ui-light/updates.png.checks.json`, and `verify-build.ps1`.
- Public source upload verified: `main` at initial code commit `610b888dea346767fb75389359b83ce5b67d2f7e`, repository visibility PUBLIC. [GitHub Actions run 40759539656](https://github.com/torsteinvalberg/Frame/actions/runs/40759539656) **passed** its clean Windows build and Core regression tests. Final documentation-only follow-up uses `[skip ci]`; executable source remains the tested commit's source.
- The built `AppUpdates` client successfully queried the actual public GitHub feed without credentials and returned no newer installer, as expected before setup publication. Evidence: `work/github-066/live-feed.json`.

## Build and limits

`C:\dev\Frame\builds\FRAME-0.6.6-beta-github-updates-20261028\FRAME.exe` — self-contained win-x64, product version `0.6.6-beta`, **182,146,569 bytes**, SHA-256 **00CBB60CECB8556BC841EF6A73E741FC687497C8D1D420D8DF71CA300C676378**. Bundled runtime remains .NET 8.0.32. No previous build was overwritten. No C# source changes followed this publish.

No new setup package or public installer release was produced. Full end-to-end installation of a newer official release remains to be qualified when that package is prepared. The helper tests reach the real validated launch boundary but deliberately substitute a diagnostic marker for starting Setup. Portable users who choose an update installer enter the normal installed-app flow; the source build is not overwritten in place. SHA-256 integrity checking is not publisher code signing; Authenticode remains separate release work.

Capture, audio and export pipelines are unchanged. Their 0.6.4/0.6.5 suites were not repeated for this update-only change. The user's reboot confirmation is recorded in the 0.6.5 report; sleep/lock/hardware qualification remains distinct.

API contract: [GitHub releases](https://docs.github.com/en/rest/releases/releases) and [release assets](https://docs.github.com/en/rest/releases/assets), including public unauthenticated reads and SHA-256 asset digests.
