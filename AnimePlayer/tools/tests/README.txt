They fake mpv (playlist, events, properties) and print what the scripts do. Paths inside are the author's sandbox paths (/tmp, /home/claude): edit them first.
  controls_layout_test.lua    the centred control-bar layout at 1920 ... 300 px wide (needs controls_layout_fn.lua, cut from Controls.lua)
  keybinds_editor_test.lua    ap-keybinds.lua: change key / conflict / unbind on a scratch input.conf

tf_test.lua <path to thumbfast.lua>   - mock-mpv test of the seek-bar preview (long / short video, stale frames, watchdog, clear, background prefetch)
shot_test.lua <path to ap-screenshot.lua> - mock-mpv test (build 37): subtitles off -> screenshot-to-file -> restored, ffmpeg fallback, errors, stuck key
Run with any Lua 5.3+ (e.g. texlua tf_test.lua ../../portable_config/scripts-extra/thumbfast.lua).
  playlist_tools_test.lua      build 36: Play later -> end of the playlist, video/gif/audio only, no duplicates, durations never compete with playback (AP_ROOT=<project root> texlua playlist_tools_test.lua)
  watched_test.lua            build 36: which finished videos are greyed out (eof / Next in the credits / resume near the end / old watched.log)
