**AnimePlayer build 37** - the build number is in `BUILD.txt`, in the About window, in the exe's file properties and shown for a moment when the player starts. Current behaviour and verification notes: `STATUS.md`. Build history: `ROUND25-FIXES.md` and `ROUND26-FIXES.md`.

# AnimePlayer + AnimeLibrary (revision 7)

**Revision 7 - the uosc edits finally load, and a clean install**
- Your rev6 log still printed one skipped edit named `'bar-speed-reset bar-frame-step ...'`: Windows PowerShell 5.1 kept handing the JSON
  list back as one object, whatever I did with it. So there is no JSON list any more: every edit is its own small file in `tools\patches\`
  and every overlay entry its own file in `tools\overlay\manifest\` (one object per file loads correctly). The merge now prints
  `loaded 9 edit definitions and 3 overlay entries` and `overlay installed: ...` - if you do not see those lines, send me the output.
- **Clean install (your request)**, on by default in `build.bat`: `CLEAN_INSTALL=1` and `USE_BUILD_FOLDER=1`. The old config is backed up
  to `mpv.backup-<date>` first, then a fresh copy of your `mpv-anime-build` folder (plus the AnimePlayer additions) is installed and files
  that are NOT part of it are deleted (old clashing scripts and leftovers). `resume.json`, `queue.json`, `watched.log`,
  `anime-settings.json`, `uosc_history.json`, `watch_later` and `cache` are kept. A safety check refuses to install, and never cleans,
  when the staged config is incomplete. Set either option to 0 in `build.bat` to go back to patching what is installed.

# (revision 6 notes)

**Revision 6 - the real cause of the merge errors**
Your build log showed it: Windows PowerShell 5.1 returns a top-level JSON array as ONE object whose fields are all the entries joined
together (`SKIPPED patch 'bar-speed-reset bar-frame-step ...'`). So the patch list AND the overlay manifest were read wrongly, nothing matched,
and the uosc edits were never applied (the earlier "Specified method is not supported" was the same thing: `$p.file` was an array of paths).
Both files are now read through `Read-JsonArray`, which flattens the array correctly on PowerShell 5.1 and 7. Your installed config from the
last run is fine and untouched by this: run `build.bat` again and the three uosc files get patched (`overlay installed: ...` lines).
Also fixed: my old `scripts\anime-detect.lua` (from an older build) is now recognised by its contents and removed, because it clashes with the
build's own `anime_profile_controller.lua`.

# (revision 5 notes)

**Revision 5 - the merge error and Windows snap**
- **`ERROR: Specified method is not supported ... Apply-Patch`**: the config merge stopped on its first edit of your `uosc.conf`
  (nothing of your config was changed, as designed). I can not run PowerShell here, so instead of guessing at that one statement:
  1. The three uosc files (`script-opts\uosc.conf`, `scripts\uosc\main.lua`, `scripts\uosc\lib\menus.lua`) are now installed from
     tested, already-patched copies in `tools\overlay\` - but ONLY when your file is byte-for-byte the original I tested (SHA-256 check;
     your uploaded files match). The merge prints `overlay installed: ...` for each.
  2. If your file differs (a newer build), the anchored edit is used as before, now with plain .NET calls and a step label, so an error
     names the exact step (`error at step "read"`, ...).
  3. Every section (repair / uosc files / input.conf / thumbfast) runs on its own: one failing section prints its error and the others
     still run. All output is also in `dist\config-merge.log`.
- **Windows snap**: Settings > "Windows snap (drag to a screen edge, Win+Arrows)", on by default. It switches the player to `border=yes`
  + `title-bar=no`: a real window frame (native snap, Win+Arrows, shadow, normal resizing) without the title bar. uosc shows its own top
  bar with minimize / maximize / close in exactly this mode. Turning it off restores what your config had. (Forcing snap styles onto
  mpv.net's borderless window would have risked breaking fullscreen, so I used mpv.net's own supported mode.)

# (revision 4 notes)

**Revision 4 - fixes after your report (hover bar gone, DLLs not copied)**
1. **DLLs**: `build.bat` used to install the config first and stop on any error there, so the libmpv / ffmpeg copy never ran. It now copies
   `libmpv-2.dll`, ffmpeg and the preview `mpv.exe` FIRST. The config step runs as a separate part: if it fails you get the exact error
   (also saved in `dist\config-merge.log`), your config stays untouched, and the player files are still complete.
2. **Hover bar / right-click gone = your config had been damaged by revision 2**, not by the uosc buttons (I re-checked uosc's own parser: all
   9 added buttons are valid, `<video>` is a legal condition). Revision 2 copied MY basic `mpv.conf` and `input.conf` over the anime build's
   files when it did not find the build folder, and revision 3 then used that damaged config as its base. The merge now recognises exactly
   those two files (by my header text), restores the originals from your newest `mpv.backup-*` folder (or from `mpv-anime-build\`), and
   removes my old clashing scripts (`anime-detect.lua`, `watch-log.lua`, `midclick-ff.lua`). `key-conflicts.txt` and the verify step at the end
   of `build.bat` say what was repaired. If no backup and no build folder exist, it tells you so: then put the anime build into
   `mpv-anime-build\` (it must contain `mpv.conf`) and run `build.bat` again.
3. **Double-click on an empty player (no video loaded) plays the last video** again (where it stopped). With a video loaded it is still
   fullscreen. Settings has a checkbox ("Double-click on an empty player...") or set `idle_dblclick=no` in `script-opts\ap-resume.conf`.
4. **Library animations**: cards fade and rise in one after another, smooth hover zoom with an animated accent ring, progress bars fill up,
   the "continue watching" banner slides in, the page title fades on view changes, and a spinner turns while scanning / syncing. Entrance
   animations play on view changes, scans and start only - not on every live update.

# (revision 3 notes)

**Read this first - what went wrong in revision 2 and what changed**
- `build.bat` could install my small basic config over the config you actually use (when it did not find `mpv-anime-build\mpv.conf` -
  e.g. because the folder was extracted one level deeper). That replaced your `input.conf` / `mpv.conf` and is why the right-click menu,
  the hover bar and the anime profiles stopped working. **Now the config you already have is always the base.** Order: installed config
  (`%APPDATA%\mpv`) -> `mpv-anime-build` folder (nested folders are found) -> basic config only on a PC with no config at all.
  Before anything is written the old config is copied to `mpv.backup-<date>`, and each file that gets edited also gets `<file>.ap-backup`.
- Nothing of the anime build is replaced any more. I only (a) add new files and (b) make small **anchored edits** to three of its files
  (`script-opts\uosc.conf` control bar, `scripts\uosc\main.lua` playlist + Subtitles menus, `scripts\uosc\lib\menus.lua`). An edit is
  applied only if the text it attaches to exists exactly once; otherwise it is skipped and reported. I tested every edit against the real
  `uosc.conf` and `scripts` you uploaded: all 33 original hover-bar buttons stay in the same order, 9 are added, only 1 line of
  `uosc.conf` changes. Running `build.bat` again changes nothing the second time.
- `build.bat` now ends with a check ("verifying the installed config") that lists what is present; look for any `[!!]`.
- hwdec / mpv.conf are no longer touched in the anime build (the old hwdec override replaced your decoder choice).
- I removed my overrides of keys that your build's own scripts use: `C` (autocrop) and PgUp/PgDn (betterchapters) keep working.

Built on your mpv_net-main source. **Not compiled by me** (no .NET, PowerShell or Lua in my sandbox): the Lua scripts were
syntax-balance-checked, the key-merge logic and the file-name parser were tested in Python mirrors, and all C# was reviewed by hand.
Expect a compile error or two on the first build - send me the output and I'll patch it.

## Build
1. Install the **.NET 10 SDK**.
2. Fill the drop-in folders (each has a PUT_..._HERE.txt):
   - `dll\` - `libmpv-2.dll` (shinchiro mpv-dev x86_64, not -v3: the i5-3470 has no AVX2)
   - `mpv-exe\` - a **standalone `mpv.exe`** (shinchiro mpv-x86_64) - NEW, needed for the seek-bar preview (see "Preview fix")
   - `ffmpeg-9.0.2-essentials_build\` - gyan.dev essentials zip (used by Subtitle Sync)
   - `mpv-anime-build\` - extracted contents of Chinna95P/mpv-anime-build
3. Double-click `build.bat` -> `dist\mpvnet.exe` + `dist\AnimeLibrary.exe`.
   After the merge read `key-conflicts.txt` in the config folder: it lists every key decision.

## What changed in this revision
**Bugs found and fixed**
- `tools\merge-config.ps1` was an EMPTY file, so the anime-build merge never ran (no key-conflict handling, no hwdec patch, no
  `include=~~/overrides.conf`, anime-detect.lua never removed). It is now written and does all of that.
- `Shift+c`, `Shift+x`, `Shift+s` bindings could never fire (libmpv reports Shift+letter as the capital letter) -> `C`, `X`.
- Duplicate / clashing keys: `Ctrl+t` twice, `i` and `Tab` (stats / uosc UI), `Ctrl+g/h/j/k` (clash with the build's shader toggles),
  MBTN_RIGHT (would replace uosc's menu). Removed or moved; see the table below.
- Library: "completed" was set when you had watched every file ON DISK even if the series has more episodes; folder matching treated
  `C:\Anime\Show` as a prefix of `C:\Anime\Show 2`; episodes were ordered `1, 10, 2`; the progress was applied from the whole log on
  every launch; MAL login could hang forever; MAL could be overwritten with a LOWER episode count; a damaged `entries.json` was silently
  replaced by an empty list.

**Player (everything below is Lua + config, so it works with the anime build's uosc UI)**
- Removed the ☰ quick menu (image 2), the Queue window and the Speed window (the build has better versions). Kept Subtitle Sync (it
  has no replacement), Library and Settings (keys F3 / F8 / F9).
- **Resume** is now a button in the control bar (hover bar, image 8) and the logic is rewritten in `scripts\ap-resume.lua`:
  one atomic read of path + position (the old C# code could save a position under the wrong file), saved every 2 s, on unload and on
  shutdown; the button seeks back, or - with nothing open - re-opens the last video *and its saved queue* at that position.
- **Queue** (`scripts\ap-queue.lua`): the queue is mpv's playlist, so uosc's "27/37" counter and the "Playlist (Type to Search)" list
  show it. It is saved to `queue.json` on every change. A **Queue** button in the control bar (and F2) opens a menu: add files / folder /
  clipboard, sort (name, date, size), reverse, shuffle, remove played, clear, restore saved queue.
- **Auto stretch** button (`scripts\ap-video.lua`): Off -> Stretch -> Fill -> Off. Detects black bars baked into the picture (cropdetect),
  removes them, then stretches to the window (Stretch) or zooms without distortion (Fill). Stays on for the following files.
- Hover bar also got: **Screenshot without subtitles**, **Change aspect ratio**, **Rotate video**, **Subtitle sync (visual)**, and a
  **Reset speed** button right next to the speed dial.
- **Speed is kept when the file changes** (`ap-speed.lua`); it only goes back to normal when mpv is closed.
- **Auto-start**: a newly opened file always starts playing (keep-open used to leave the player paused after a video ended).
- **Middle mouse button = jump forward** (85 s, the old `Enter` skip; change it in Settings or `script-opts\midclick-jump.conf`).
- **Preview fix** for `thumbfast: ERROR! install standalone mpv`: see below.
- `ap-watch.lua` replaces watch-log.lua: detects anime playback (fansub tags, `- 05`, `S01E02`, "anime" in the path, Japanese audio) and logs
  start / stop / finished + position to `watched.log`, fully offline.

**Library (AnimeLibrary)** - offline first, covers and tracking improved (see below).

## Preview fix (thumbfast)
mpv.net only embeds libmpv. thumbfast starts a *second, standalone mpv process* to draw the preview, so without an `mpv.exe` you get the
error and blank previews; the "sometimes works" behaviour is that helper being started and killed while you move the mouse.
1. Put a standalone `mpv.exe` in `mpv-exe\` and run `build.bat`: it copies it to `dist\mpv-thumb\mpv.exe`.
2. `scripts\00-thumbfast-path.lua` tells thumbfast where it is (works even if you move `dist`), and `thumbfast.conf` gets
   `spawn_first=yes` (helper is started once, not per mouse-move), `hwdec=no` and `network=no` for the helper.
I could not run it here, so if previews are still blank send me the console output (`mpvnet.exe --log-file=log.txt`).

## Keys (PotPlayer style)
`input.conf` has three sections: **CORE** (PotPlayer keys, win over the anime build), **EXTRA** (added only when the key is free),
**STANDALONE** (only without the build). Decisions with the real anime-build keys (tested):
| PotPlayer key | what the build had there | result |
|---|---|---|
| Enter = fullscreen | `seek 85` | the 85 s jump moved to the **middle mouse button** |
| M = mute | 7.1 upmix | moved to `Ctrl+M` |
| Z = reset speed | next chapter playlist | moved to `Ctrl+Z` |
| F = next frame | fullscreen | removed (Enter / double-click already do it) |
Other CORE keys (Space, Esc, Alt+Enter, double-click, PgUp/PgDn, Home/End, C/X/Shift = speed, D/F frames, Ctrl+E screenshot, Ctrl+O, Ctrl+U)
had no clash. EXTRA keys skipped because the build already uses them (arrows, Ctrl/Shift+arrows, Up/Down, wheel, Ctrl+T) are listed in
`key-conflicts.txt`; the build also binds `MOUSE_BTN3/4` and `WHEEL_UP/DOWN` - those are the SAME keys, mpv keeps the later one.
Set `POTPLAYER_WINS=0` in build.bat to let the build win everywhere instead.
Other keys: F2 Queue menu, F3 Subtitle Sync, F8 Library, F9 Settings, Ctrl+Alt+A aspect, Ctrl+R rotate, Ctrl+Alt+S auto stretch, Ctrl+Alt+R resume.

## (old note, now done) things that needed the anime build files
The anime build's menus (images 3, 4, 5: the playlist menu and the Controls > Subtitles menu) are Lua files that are not in your zip
(`mpv-anime-build\` was empty and GitHub blocks me from reading its script folder). So:
- Queue features are in a **Queue button/menu** that sits next to the playlist button, and the visual subtitle adjuster is a **button**
  that opens Subtitle Sync - not literally inside those two menus. If you send me the build's `scripts\` folder and `script-opts\uosc.conf`
  I can splice them into the menus themselves.
- The control-bar buttons are added by editing the `controls=` line of the build's `uosc.conf` (merge-config.ps1). The icon names are
  Material icons; if one shows empty, replace it in `uosc.conf` with another Material icon name.

## AnimeLibrary
Keeps: fast local scanning with no naming/folder rules, custom sources (non-AniList anime and manga), desktop client, extension marketplace,
external players (AnimePlayer, mpv, VLC, MPC-HC, any exe), UI customisation (themes, background image, tile size), MyAnimeList, cover download.
Removed: IMDb/OMDb and movies.
- **Scan** (offline, one pass): files are grouped by the title in their *name*, so flat dumps, `Show\Season 1\ep`, batches and mixed folders work.
  Titles, seasons and episodes are parsed from names like `[Judas] TenKen - S02E01.mkv` or `[AWHT] Juukishi - 07 [720p].mkv`.
- **Tracking**: the player writes `watched.log` (also while offline). The library reads it every 2 s, marks finished episodes (>= 85 %),
  remembers what you played last, and creates "detected" entries for anime that are not in a library folder. Everything is saved
  immediately (atomic write + `.bak`).
- **Update list**: every change is queued (`pending`). When the internet returns the library matches new titles, downloads covers and uploads
  to MyAnimeList automatically (Settings: switch off to do it by hand) - or press **⇅ Update list (n)**. It never lowers an episode count
  that MAL already has, sets `completed` only from MAL's real episode count, and fills start/finish dates (approach taken from Taiga).
- **Covers**: downloaded once into the anime folder as `cover.jpg` (+ an app cache), set as the Explorer **folder icon**, never overwritten
  after you pick your own via right-click > Change cover image. Folders shared by several shows use the cache only.
- **MAL setup**: create an app at myanimelist.net/apiconfig (type "other", redirect `http://localhost:8765/cb`), paste the Client ID in
  Settings, press MAL Login. I did not reuse Taiga's Client ID: it belongs to Taiga's own registered app and redirect.
- **Extensions**: declarative metadata providers (a JSON manifest with a search URL and field paths, format in `Extensions.cs`). The
  marketplace is any URL that returns a JSON list of them - I did not invent a repository, set your own in Settings. Install / update /
  disable / remove work in the Extensions window. Seanime's own Go/JS plugins are not compatible.

## Resume, speed, cache
- Resume data: `resume.json` (same format as before, existing data is kept). Queue: `queue.json`. Watch log: `watched.log`.
- `overrides.conf` sets `idle=yes` so the player stays open after the last video (Resume and the saved queue stay reachable).
- Cache cleanup (subtitle-sync temp files, `*.tmp`, optional shader cache) is unchanged; Resume/queue data are never deleted by it.

## Not verified (please test first)
Build and launch; Resume button after closing mid-episode; Queue menu; Auto stretch on a video with black bars (needs `hwdec=d3d11va-copy`,
which `overrides.conf` sets); the thumbnail preview; `key-conflicts.txt` content; library scan, cover download, folder icons and a MAL update.

## New in revision 3
- **Free window resizing**: the window can be dragged to any shape (mpv.net forced the video's aspect ratio through `keepaspect-window`).
  Settings > "Free window resizing" (on by default).
- **Screenshots** go to the Windows Pictures folder (for you `C:\Users\Mahdi\Pictures`, also correct when Pictures is moved to OneDrive).
  Settings > Screenshot folder changes it. It is applied after your config loads, so mpv.conf cannot override it.
- **Frame back / frame forward** buttons in the hover bar, on both sides of play/pause (also `d` / `f`).
- **Live taskbar progress**: the bar under the taskbar icon follows the playback position (it only started after the first pause before and
  then stayed yellow). Green = playing, yellow = paused, hidden when nothing plays.
- **Auto stretch** now drives your build's own `autocrop.lua` for the black-bar detection (no second detector); my own detection is only a
  fallback when autocrop is not installed.
- **Resume** also understands your build's History (`uosc_history.json`) when nothing of mine is saved.
- **Queue inside the playlist menu** (pic 4): hover a row and use the new buttons next to move up/down/remove - *Queue tools* (add files /
  folder / clipboard, sort, shuffle, reverse, remove played, clear, restore saved queue), *Play next*, *Move to end*. The "27/37" box
  (pic 3) opens that same menu. The queue is saved in `queue.json`.
- **Visual subtitle adjuster** inside Controls > Subtitles (pic 5): Position, Delay, Scale and "Subtitle Sync (visual)...", added at the END of
  the list so the existing rows keep their numbers.
- A small fix to your uosc: `create_self_updating_menu_opener` appended its buttons to the caller's `actions` table every time a menu was opened.
- **AnimeLibrary looks like Seanime**: `#070707` canvas, icon rail, "Continue watching" hero + carousel, grids grouped by status
  (Currently watching / Not started / Completed) with `watched / total` badges, `#6152DF` brand color, 8 px corners, hover ring.
  Unmatched and Updates have their own pages; folders and extensions have their own windows. The app has an icon now (`app.ico`).

## Keys that were NOT overridden (your build's scripts own them)
`C` autocrop - `PgUp/PgDn` chapters - `q` quit-and-save - `V` vsr - `Ctrl+C` deinterlace - `Ctrl+/` sub timing. `key-conflicts.txt` (in the config
folder after the build) lists every key decision, including the build keys PotPlayer replaced.



## Volume and fullscreen memory
- `scripts/ap-volume.lua` keeps volume and mute between files and between sessions (`ap-volume.conf` in the config folder) and re-applies them after every file load.
- `scripts/ap-fullscreen.lua` keeps fullscreen when the file changes: a fullscreen drop within ~2 s of a file transition is undone; your own Enter / Esc / double-click is respected.


## rev8 - what changed
**Build:** the "read as an array" error in `tools\merge-config.ps1` is fixed (a nested-array bug in the JSON loader). The merge now prints
`loaded N edit definitions (N files)` and fails loudly if the counts differ, so the hover-bar buttons, the queue in the playlist menu and the
subtitle adjuster inside Controls > Subtitles are really applied.
**Player:** right-click opens mpv.net's own menu (File / Playback / Navigate ...); the uosc menu stays on the hover bar's first button and the MENU key.
AnimePlayer entries are in the menu's "Custom" submenu (never use a plain `#menu:` line - it would shrink mpv.net's menu to those lines).
Middle mouse = jump forward 5 s (Settings changes it). Pointless duplicate key lines inside the anime build are switched off by the merge and reported.
Windows-snap is OFF by default (it changed the border style and was the likely cause of the broken fullscreen); mpv.net's title-bar hack no longer
runs while fullscreen. `ap-fullscreen.lua` keeps fullscreen across file changes, `ap-volume.lua` keeps volume + mute across files and sessions.

## AnimeLibrary rev8
- **Series page:** click a series. It lists every folder of the series (any depth) as a tile with its own cover (cover.jpg / folder.jpg inside it, or one you
  pick, else a thumbnail of its first video), then the files with real video thumbnails (needs ffmpeg.exe next to the app). Views: Thumbnails / Detailed list / Compact list.
- **One series, many folders:** files are grouped by the TITLE in their names, not by folder. Seasons in subfolders, files spread over several folders or drives,
  spelling variants and names like "Show 01 Episode Title.mkv" are merged into one series. Merging by hand: right-click > Merge into another series.
- **Smart names:** right-click > Smart name... sets the name shown, an optional "search online as" name, and "Local only" (never looked up online - no cover search, no tracker).
  Rules survive rescans and are managed in Settings > Smart names.
- **OP / ED / music detection:** openings, endings (NCOP / NCED / creditless), soundtrack / music files and extras (PV, CM, trailers, specials, OVA) are recognised by name,
  extension and folder. They are listed under their own filters, never counted as episodes and never move your progress.
- **Covers:** pictures now load from bytes (folders with # or % in the name gave blank covers), every download is validated, the chain is AniList > Jikan > Kitsu > frame of the first episode.
- **Matching:** search results are scored (title similarity, season, format, episode count) instead of trusting the first hit; "unmatched" is shown when nothing is convincing.
- **UI:** own title bar and border (colour/thickness), styled scrollbars and sliders, vector icons, new app icon, sorting (6 modes), 3 library views, group on/off,
  background picture that really works (darkening, blur, fit), crisp banner instead of the smeared hero, many appearance options in Settings.
