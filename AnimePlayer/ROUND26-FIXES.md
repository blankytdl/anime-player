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

7. thumbfast: also publishes user-data/thumbfast (what newer uosc reads), writes thumbfast.log next to mpv.conf, script-message-to thumbfast debug shows its state on screen.
8. thumbfast: user-data/thumbfast now holds the JSON text exactly like the original thumbfast; first thumb requests are logged to thumbfast.log; BASIC install mode installs it too. NOTE: .github/workflows/build-windows.yml still checks for dist/mpv-thumb/mpv.exe and will need that check removed.

# Build 27
9. Thumbnails smoother: one low-priority sprite pass (ffmpeg, 1 thread, started 3 s after a video starts) writes a rough frame every few seconds of the whole
   video into one temp file, so hovering shows a frame instantly; when the mouse rests 0.25 s the exact frame swaps in. Switch off: background=no in thumbfast.conf.
10. Profiles now SAVE the current Anime Build Options result (active shaders, scalers, deband, HDR, audio...) as a snapshot; a base preset is optional.
    After applying, every saved setting is read back from mpv and compared (OSD says "all applied" or lists what was NOT applied).
    Profiles... > "Check active settings" lists the shaders that are really loaded and flags MISSING shader files.
11. Anime Build Options > "Compare shaders OFF / ON": Split view (frozen frame, left OFF / right ON, mouse moves the divider, Esc ends) and Flip. scripts/ap-compare.lua.
    The shader list is always put back (stop, new file, end of file, shutdown).

# Build 28
12. Seeking / start freezes: the thumbnail SPRITE pass is OFF by default (it read the whole file in the background = disk contention while seeking / starting);
    thumbfast no longer scans folders, queries the track list or creates a timer on every mouse move; the queue's "played" list no longer checks files at startup
    (a missing network drive could freeze the start).
13. Anime Build Options rows: "Profiles..." and "Compare Shaders" now look like the other rows (no icons) - patches 95 / 96 convert the earlier rows, fresh or already patched.
    The compare also lives in the Profiles menu and on keys Ctrl+Alt+c (split) / Ctrl+Alt+x (flip). ap-menu-check.log (next to mpv.conf) says which menu patches are in the controller.
14. Forced Anime4K / detection modes: profiles never touch glsl-shaders on an automatic apply unless the profile has "Auto-apply shaders" ON ("Use now" always applies all);
    compare gives way if the Anime Build reloads shaders while they are switched off, and always restores them.

# Build 29
15. Playlist key / button opens the QUEUE tab while a video that was started from the Queue plays (the Playlist tab otherwise). Tab buttons still switch freely
    (ap-playlist-direct). uosc patch 97 + user-data/ap/queue-playing. The playing video is marked on BOTH tabs when the same file is in both (patches 99 / 99b, Queue rows).
16. Greying (watched): ONLY when a video really plays to its end. Next, opening, resuming and "left at 92 %" no longer grey a video; a late eof event of the previous file
    cannot mark the new one. Played rows in the Queue tab are greyed only if watched ("opened, not finished" otherwise).
17. Library: ap-watch.lua logs the end of a video from the end-file event (path and position remembered, so it works mid-playlist and with keep-open); a late eof-reached
    can no longer log a freshly opened file as finished.
18. Pin (always on top): saved state is applied again shortly after start and once the window is shown (C# OnShown), unless you toggled it yourself.
19. Forced zoom / crop: manual zoom / pan / alignment / unscaled video are reset when the forced mode is applied and a crop rectangle that reaches outside the picture is removed.

# Build 31
22. Explorer right-click on SEVERAL selected videos: "Play all with AnimePlayer" and "Add all to AnimePlayer queue" (classic menu: Windows 11 = "Show more options" / Shift+F10).
    One player start with every selected file (MultiSelectModel=Player). Run Setup > Register video file associations ONCE (needs admin) to install them.
    "--queue" / Queue instance mode now puts the files into the Queue TAB (queue-inbox.txt), not into the plain mpv playlist.
23. Playlist tab: the four toolbar buttons of the Queue tab (remove watched, sort, shuffle, remove all) are guaranteed by patches 101 / 102 even if an earlier toolbar patch did not apply.
24. ap-menu-check.log now lists the Playlist/Queue patches (tabs, tools, redirect, playing mark) so a missing one is visible.

# Build 32
25. REGRESSION FIX (found from config-merge.log of build 30): my patch "100-playlist-opens-on-queue" sorts BEFORE "11-..." (and before 42), so it changed the text patch 42 (ui-3) needs.
    42 was SKIPPED: the Playlist tab lost its toolbar (remove watched / sort / shuffle / remove all), the hidden staged entries, the greying and (via patch 99) the playing mark.
    The new patches are renamed 98-, 98b-, 98c- so they run after 42 / 43 and never change text an older patch needs. A static check of every anchor and its order is done.

# Build 33
26. Right-click bulk open, BOTH menus: (a) compact Windows 11 menu: "Open" / "Open with > AnimePlayer" on many selected files now starts the player ONCE with all of them
    (MultiSelectModel=Player on the open verbs; run Setup > Register video file associations once, as admin). (b) full / classic menu (Shift+F10 or "Show more options"):
    "Play all with AnimePlayer" and "Add all to AnimePlayer queue". Windows 11 does not let a normal program add its own entries to the compact menu (that needs a signed
    MSIX shell extension), so Windows11-full-right-click-menu-ON.bat makes the full menu the normal right-click menu (OFF.bat undoes it).
27. Queue tab on the playlist key: the "playing from the Queue" flag no longer depends only on playlist ids; the file started from the Queue tab counts too, and the flag is refreshed on every
    path change. Decisions of the uosc gate are written to ap-gate.log; script-message ap-queue-debug shows the flag on screen.
