# Build 26 (not compiled / not run on Windows; Lua syntax-checked, queue logic simulated)
1. Queue/playlist REVERSE bug. Root cause (reproduced in tools/tests/queue_order_test.lua): when a queued video was ALSO an entry of the playlist,
   ap-queue.lua MOVED that playlist entry behind the playing one and never moved it back. The playlist order got scrambled, and after the queue
   "next" / end of video went on with entries that had been pushed behind it = back to earlier episodes. Now the queue always plays an extra,
   hidden copy that is removed after it has played; the playlist is never reordered. Trade-off: a queued episode that is also later in the
   playlist plays once from the queue and once more when the playlist reaches it (it shows as watched).
2. Profiles: Anime Build Options > Profiles... (scripts/ap-profiles.lua, patch 93). Name + preset (any mpv profile from mpv.conf) + type (Any/Anime/Live) +
   resolution (Any/SD/HD/FHD). Auto-apply picks the most specific match 0.6 s after a video starts. Saved in ap-profiles.json. "Use now" applies it by hand.
3. Seek-bar preview without mpv.exe: portable_config/scripts-extra/thumbfast.lua is installed by build.bat as scripts\thumbfast.lua (the build's own copy is
   kept as thumbfast.lua.ap-backup). It cuts the frame with ffmpeg.exe (found next to ffprobe / below the player folder, searched again on every start and after
   an error) and shows it with overlay-add. No mpv-thumb folder, no mpv_path.  NEEDS ffmpeg.exe in the ffmpeg folder (the queue durations need ffprobe from there already).
4. Register file associations: the elevated helper registered the word "video" itself as a file extension (Skip(1) instead of Skip(2)); fixed, and a registry error now
   returns a failure code instead of silently "succeeding".
5. Queue videos are no longer deleted from the Queue tab after they were played: they move to greyed "played" rows at the bottom (saved in queue-done.json).
   Double-click one = play it again, trash button / Del = remove it, "Remove watched" and "Remove all videos from the queue" clear them. The video FILE is never touched.
6. Sorting / shuffling the Playlist tab while a queue video plays no longer breaks where the playlist goes on (the playing queue copy is put back behind the episode it came from).
