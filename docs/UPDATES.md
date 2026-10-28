# Source and app updates

Source and public installer releases live at https://github.com/valbergProd/Frame.

## In FRAME

Settings > General shows the installed version and **Check for updates**. By default FRAME also checks once at startup; this can be disabled with the adjacent setting. Isolated diagnostic launches never make the startup request. Nothing is downloaded or installed automatically.

A beta installation can receive newer betas and stable releases. A stable installation receives stable releases only. Version comparison uses the version number and beta sequence, not publication dates; stable supersedes beta at the same number. Draft releases, older versions, and releases without an installer are excluded.

**Download update** downloads from this repository's GitHub Releases and verifies the exact published length and SHA-256 digest before making the installer available. Cancellation and failed verification discard the partial download. **Install update** requires capture and exports to be idle and trim/share editors to be closed, then asks to close FRAME. A helper waits for the old process to exit, verifies the installer again (including FRAME product/version metadata), and opens the normal installer. The installer preserves user settings and recordings. Cancelling Setup leaves the old installation in place; reopen FRAME normally.

The app contains no GitHub credentials. Checks use GitHub's public releases API; GitHub receives the normal connection metadata. Unavailable/rate-limited checks are reported as failures, never as proof that the app is current. SHA-256 verifies integrity against GitHub metadata; it is not Authenticode signing. Installer signing remains separate release work.

## Publish an update

1. Update the application version and release notes. Build and run the appropriate local tests. Push the reviewed source to `main`; GitHub Actions builds the solution and runs the deterministic Core tests. Desktop/capture validation still runs locally.
2. Build a new installer with `scripts/Build-BetaInstaller.ps1`. Keep the existing installer AppId. Use `FRAME-<numeric version>-Beta-Setup.exe` for beta releases, or `FRAME-<numeric version>-Setup.exe` for stable releases. The current builder defaults to beta naming; adapt its explicit output naming before the first stable release.
3. Tag the matching reviewed commit, for example `git tag v0.6.7-beta`, and push the tag with `git push origin v0.6.7-beta`.
4. Run `scripts/Publish-GitHubRelease.ps1 -Installer <path> -Version 0.6.7-beta -NotesFile <notes.txt>`. It requires a clean checkout, verifies the source tag on the remote, uploads a **draft**, and compares GitHub's size/digest with the local installer. `-Preview` only validates local inputs. Existing releases are never replaced.
5. Review the draft and publish it on GitHub. Alternatively, add `-Publish` when deliberately creating and publishing a new release in one operation. Only a published release with a matching installer reaches the updater.

Pushing code alone does not ship an app update. The initial repository upload does not publish an installer. End-to-end installation of a newer official release must be qualified when that installer is prepared. Keep version numbers increasing; do not replace an already published installer with different bytes under the same version.

## Recording tools in Setup (0.6.8+)

Setup downloads the pinned Gyan FFmpeg package directly from its publisher, with a progress page, before modifying the installed app. `packaging/ffmpeg.ini` specifies the HTTPS URL, archive SHA-256 and hashes of the two executables and retained license/README. The helper extracts only those four entries and checks the capture filter and probe. Inno Setup then installs them into `{app}\tools` with normal uninstall tracking. No winget, PATH or registry changes are needed for FFmpeg. Explicit user-selected FFmpeg paths retain precedence.

If the same four files are already present and verified, no download is required. Corruption or missing files trigger preparation again. A network/checksum/preparation error stops before copying app files, with retry/cancel guidance. First install requires internet and about 1 GB temporary space, plus app space. Administrators can provide the exact pre-downloaded package with `/FFMPEGARCHIVE="C:\path\ffmpeg-8.1.2-full_build.zip"`; the pinned checksum is still mandatory.

Build an isolated installer with `Build-BetaInstaller.ps1 -ValidationBuild -SourceExe <published exe> -OutputRoot <work folder>`. This uses a separate AppId, skips shortcuts/app launch/user-data cleanup and compresses differently, while retaining the same prerequisite and file-install logic. Never distribute this validation installer. `Test-SetupFfmpeg.ps1` exercises real online setup, failed downloads, offline reuse/repair, installed-app media tests and uninstall under a fresh `work` directory. Change the manifest only after checking the publisher/package-manager metadata and validating the new media tools.

Local handovers, builds, installed beta copies, capture fixtures and credentials are excluded from Git. Necessary source assets are included. Clone the repository and use `Build.ps1` for a local build/test, or `Publish.ps1 -SelfContained -OutputDirectory <new folder>` for a runnable local build.
