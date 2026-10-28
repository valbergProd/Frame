FRAME Setup automatically prepares ffmpeg.exe and ffprobe.exe here.
Do not move or delete them while FRAME is recording.

The files are downloaded directly from Gyan's FFmpeg release during setup,
with SHA-256 verification. FFMPEG-LICENSE.txt and FFMPEG-README.txt retain the
publisher's license and build information. See ../licenses/FFMPEG-NOTICE.txt.

An explicit FFmpeg path in FRAME Settings takes precedence. Otherwise this
folder is checked before WinGet and PATH. Other FFmpeg installations are
not modified. Uninstall removes files installed here by FRAME Setup.

For a standalone developer build, place a compatible FFmpeg 8.1+ pair and
its license here, or select a separate FFmpeg installation in Settings.
