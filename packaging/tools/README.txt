FRAME looks for FFmpeg in this folder (after a path chosen in Settings).

Easiest: start FRAME. If FFmpeg is missing, the Capture page offers
"Install FFmpeg (winget)", which runs in a visible window:
  winget install --id Gyan.FFmpeg --exact --source winget

Or copy these two files from a Windows FFmpeg 8.1 or newer build into this folder:
  ffmpeg.exe
  ffprobe.exe
then click "Check again" in FRAME (or restart it).

FFmpeg is not included with FRAME. FFmpeg builds carry their own license
(LGPL or GPL depending on the build); keep the license file that comes with the
build you use. See ..\licenses\FFMPEG-NOTICE.txt.

Files you copy here are yours: uninstalling FRAME does not remove them.
