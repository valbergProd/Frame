# FRAME 1.1.0 release preparation

Date: 2026-11-27. Source commit `32490b5` (tag `v1.1.0`). Detailed feature notes: FRAME_UNEXPECTED_EXIT_POPUP_VALIDATION.md (sand, exit card, Settings note, update notes) and docs/release-notes/1.1.0.md (the user-facing text).

## Artifacts
- Installer: `C:\dev\Frame\builds\FRAME-1.1.0-20261127-152244\FRAME-1.1.0-Setup.exe`, 66,120,693 bytes, SHA-256 `503E5D4072B9E76A7458A427560BC9E5372D2634DC331B51A5691D4A89D22C68`, unsigned.
- Payload: `app\FRAME.exe`, 182,163,977 bytes, SHA-256 `DA6A4D85AACD3993D4BB021F60DAD770905CA78CE4BF804C8CDC7047AD6B7987`, file version 1.1.0.0, product version `1.1.0+32490b5`, self-contained .NET 8.0.32, Inno Setup 6.4.3.
- GitHub: tag `v1.1.0` pushed; **draft** release uploaded by `Publish-GitHubRelease.ps1` (size and digest compared with the local installer by the script). Not published: the in-app updater ignores drafts, so no user is offered it until the draft is published on GitHub.

## Verified
- Release build 0 warnings; Core tests 160/160 (run from the built `Frame.Tests.exe`).
- Build script smoke test: published payload starts, reports FRAME 1.1.0 on .NET 8.0.32.
- Off-screen renders of the Cinematic D sand (stone active/inactive), the exit-note card (Original, Pink, minimum size) and the update "What's new" area; animation benchmark (sand): 24-28 % of one core while visible.

## Not verified
- Installer install/upgrade/uninstall (`scripts/validate-installer.ps1` needs validation-AppId installers of 1.0.0 and 1.1.0; not run), wizard, diagnostics script, real-update path from 1.0.0 through the public feed, the in-app "What's new" against a real published release, live look of the sand and the exit card on screen, other monitors/DPI, weaker hardware.

## After publishing the draft
Update README ("Current release", Download setup link) and docs/QUICKSTART.md to 1.1.0, then check the public feed from a 1.0.0 install.
