# FRAME 1.0.0 release validation

Date: 2026-10-28. User explicitly requested the first stable 1.0 release, public Setup publication and a Download setup link in README's opening paragraph. They also requested personal handover files be removed from GitHub.

## Released artifact

- [Stable release v1.0.0](https://github.com/valbergProd/Frame/releases/tag/v1.0.0), release ID 427193932. Verified public, not draft/prerelease, and GitHub's latest release.
- [Download setup](https://github.com/valbergProd/Frame/releases/download/v1.0.0/FRAME-1.0.0-Setup.exe). Size 66,114,885 bytes; SHA-256 `E67E0BF31CA8FD8EB69990EA465434E45491CFC598EEDE9452C7340AB3064237`.
- Local folder: `C:\dev\Frame\builds\FRAME-1.0.0-20261028-220641`.
- Runnable payload: `app\FRAME.exe`, 182,146,569 bytes; SHA-256 `46E93F348C13D5E8CF84DEA44BB0997CCF4B9AED59E989446F95CFF854CCCF0B`.
- File version 1.0.0.0; app informational version `1.0.0+4331a1629ca553700de9c19f0763a0756bc28d9a`. The app was built at 4331a16; release tag points to 48a6bbf, adding only local-agent-file untracking and the packaged PowerShell helper fix. App sources are identical. Self-contained Windows x64, .NET 8.0.32; Inno Setup 6.4.3; unsigned.

## Changes and verification

- Stable metadata/naming; removed hardcoded beta labels in startup/export diagnostics. Builder follows InformationalVersion or explicit `-Channel`. Installed guide is README.md. Capture, audio and FFmpeg manifest unchanged from 0.6.8.
- GitHub CI at release tag source [48a6bbf](https://github.com/valbergProd/Frame/actions/runs/40769072771): zero build warnings/errors, 159/159 Core tests. Self-contained publish and final installer compilation succeeded; smoke confirms .NET 8.0.32 and stable startup text.
- Isolated beta 0.6.8 → stable 1.0.0 upgrade: 9/9 checks, including previous-folder discovery, offline reuse, payload/version identity, repair under invalid inherited PSModulePath, settings/recording fixture preservation, startup text, uninstall and untouched real user state. Update UI: 6/6 and version 1.0.0 rendered. Uses separate validation AppId; does not alter the user's production install.
- Final production installer accepted by updater handoff diagnostics: 7/7. Actual setup launch was intercepted; parent-exit, metadata, size/hash and invalid-path behavior verified.
- Public feed checked without credentials: 0.6.8-beta and 0.6.8 stable both select 1.0.0; 1.0.0 offers no newer update. Actual installer downloaded through the built Core updater, exact bytes/hash verified against the local package. README direct-download URL matches the verified asset.
- Earlier 0.6.8 evidence remains applicable to unchanged capture/media and prerequisite behavior: 36/36 media tests and 13/13 setup checks (see FRAME_068_VALIDATION.md). Not rerun solely for the release-version promotion.

## Issues found and resolved before publication

A Windows PowerShell helper can inherit PowerShell 7's module search path when launched indirectly. This caused Get-FileHash lookup failure before app files changed. The helper now uses only Windows PowerShell's inbox module directory. Actual installer repair under a deliberately invalid inherited path passed. The preliminary 22:01 build was not published; the 22:06 build supersedes it.

Test-wrapper issues (Windows PowerShell JSON array enumeration and a positional Select-String argument) were corrected; app diagnostics themselves passed. The complete corrected run is retained under `work/release-100/upgrade-verified`.

GitHub initially exposed the uploaded draft only through the authenticated release list/ID, returning 404 from the tag endpoint. The existing draft was located, its asset hash/size reverified, and that same release was published by ID. No duplicate was created. The release script now resolves drafts through the authenticated list for future releases; this tooling-only correction follows the immutable release tag.

## Private files and limits

AGENTS.md and CLAUDE.md were removed from tracking and ignored; their local copies remain. Handover_files was already ignored and was never included in this repository's tracked source. At the user's subsequent request, all published history was rewritten to remove the agent files; v1.0.0 now points to d4efa4b7aef99187a2f92bacfb0d43c12376cdc2 with the same file tree and installer. Original commit identifiers above describe build provenance before rewriting. See HISTORY_CLEANUP.md for verification and the remaining GitHub cache limitation. Product docs and validation reports remain public.

Evidence is local under `work/release-100`, plus the artifact's build-info.json. Not a clean-VM/Windows 10 or broader hardware qualification. The real user's production installation and normal update UI were not driven end to end: public download, helper handoff and isolated installer upgrade were verified separately. Signing and wider hardware acceptance remain outstanding as disclosed in release notes.
