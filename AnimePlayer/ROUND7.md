# Round 7 - what changed and why

**Install:** run `build.bat` as usual. The Lua files are now patched ONLY by the anchored edits in `tools\patches` (the stale
pre-patched copies of `main.lua`, `menus.lua` and `anime_profile_controller.lua` were removed from `tools\overlay`). That stale copy was the
reason the Queue tab never showed up: it contained the tab code but not the line in `menus.lua` that hands the tabs to the menu.

| Request | Fix |
|---|---|
| Pic 1 + 2: centre cluster always centred, never squashed | `Controls.lua`: the buttons between the 2nd and 3rd `space` in `controls=` (prev, frame back, play, frame forward, next) are laid out first, centred on the bar, with the normal spacing. Other buttons give way (hidden from the middle of their group). Same in window, fullscreen and PiP. |
| Pic 3: accent did not reach the hover bar / tooltips | `Button.lua` copied the colours once when it was created. It now reads them live. |
| Real themes | 9 themes (`Amber Glass, Ocean Deep, Violet Haze, Emerald Solid, Sakura Soft, Crimson Cinema, Midnight Minimal, Mono High-Contrast, Light Paper`): each sets palette + corner radius + text outline + panel transparency + timeline style. Accent colour is separate and works on top of any theme; text on the accent auto-picks black/white. |
| Close button | `TopBar.lua`: always red with a white x, brighter on hover; `-` and the square keep the theme colours. |
| Pic 4 volume | New `ap-volume-osd.lua`: wheel / Up / Down / mute show a PotPlayer-style horizontal bar (speaker, accent fill, notch at 100, part above 100 in red). `input.conf` WHEEL_UP/DOWN, UP/DOWN call `ap-vol-step`. The uosc slider is still there for dragging. |
| Pic 5 red | One red everywhere: `e5484d` (uosc `error`, close button, over-100 part, Crimson theme, accent "Red"). |
| Pic 6 tabs | Playlist tab = mpv playlist (auto-recognised episodes). Queue tab = your own list (`ap-queue.lua`, saved in `queue-list.json`): the first queue item is placed right after the playing video, so the queue plays one after another and then the playlist carries on. Row buttons: up / down / remove; click = play now; Del removes. Add: files, folder, clipboard (icons in the tab row), or the new "Add to queue" button on any Playlist row. |
| Pic 7 shuffle | Removed from the control bar and PiP. Shuffle icon is at the right end of the tab row in both Playlist and Queue (each shuffles its own list). |
| PiP / resize reset the accent | uosc re-reads its options whenever `script-opts` changes (PiP and `ap-responsive` do that on every resize) and that wiped the theme. The theme is now re-applied after every options reload. |
| Theme not remembered | Saved in `uosc_theme.json` (as before), but `build.bat` deleted it on every clean install. It, `ap-volume.conf` and `queue-list.json` are now in the keep list. |
| Speed across files | `ap-speed.lua` rewritten: while a file is loading, anything that changes the speed (mpv's per-file reset, profiles, watch-later) is undone immediately; it is not saved, so a new session starts at the configured speed. |
| mpv logo | `mpv-icon.ico` (exe icon) and the window icon inside `MainForm.resx` recoloured from purple to the amber accent. Needs a rebuild of the exe. |

Untested here (no Windows / mpv in the sandbox): everything was checked by applying all patches to your original scripts and checking the
Lua for balanced blocks, but not run. If anything errors, `uosc-errors.log` / the mpv console will name the line.

# Round 8 additions

| Request | Fix |
|---|---|
| Close button follows the theme, but is not the accent | Each theme has its own red (`close` in the theme table in `main.lua`), always different from the accent (Crimson uses a dark maroon because its accent is red). Hover = lighter shade. |
| Drag and drop with the picture | `src\MpvNet.Windows\WinForms\DropOverlay.cs` + `MainForm.cs`: while files are dragged over the player a click-through overlay shows **Play later** (top 20 %, adds to the Queue tab, folders are expanded) and **Play now** (rest, as before; Shift = append). The zone under the cursor is highlighted in the accent/theme colour. Needs the exe to be rebuilt. |
| Drag to reorder in Playlist and Queue tabs | `Menu.lua`: press and hold a row for a quarter of a second, then move the mouse: the row is picked up, a line shows where it will land, release to drop. A quick drag still scrolls the list. Works in both tabs (not while a search filter is active). The up / down buttons and Ctrl+Up/Down still work. |

# Round 9
Volume by wheel / Up / Down / M now shows uosc's tall slider (the one with the number) instead of the horizontal bar: `input.conf` does `add volume` + `uosc/flash-volume`. `ap-volume-osd.lua` was removed.

# Round 10
- Progress bar: smaller (`timeline_size` 30 -> 20), rounded pill with glow, a shine that slides over the played part while playing, and a round knob that grows on hover (`Timeline.lua`).
- Control bar: the left and right button groups now sit right next to the centre block (no big gaps).
- Red is darker everywhere (`c8202c`). New theme **Red & Black** (pure black, bold red).
- Close button looks like the other window buttons until the mouse is on it; then it lights up in the theme's red (never the accent).
- Queue tab: the tool icons no longer overflow the tabs (Add / Shuffle / Clear; "Add" opens a small menu: files, folder, clipboard, sort). The old "palette" search mode is gone - it was what blocked dragging and Ctrl+Up/Down in the Queue.
- Drag to reorder: the carried row floats under the cursor, its slot is tinted, an accent bar with a dot shows the landing spot, the hover highlight and row buttons are hidden while dragging.
- Drop on "Play later": mpv script names turn `-` into `_` (`ap_queue`), so the message was going nowhere. It is now broadcast. Rebuild the exe.

# Round 11
All 10 themes are now transparent (glass): menus ~40-55 % opaque, control buttons almost clear, slider / speed / title / timeline panels ~30 %. Text keeps its outline so it stays readable.

# Round 12
- **Taskbar bar**: the bar under the taskbar icon is drawn by Windows (green, yellow when paused) and cannot be coloured, so it is now drawn onto the icon itself in the theme / accent colour (`TaskbarIcon.cs`). Windows' own bar is switched off. (`taskbar-progress=no` in mpvnet.conf turns the whole thing off.)
- **Play later**: the dropped paths are written to `queue-inbox.txt` and `ap-queue.lua` reads them (no more arguments passed to a script name).
- **Sorting**: Queue (icon `+` -> "Add / sort"): name A-Z / Z-A, newest, oldest, largest, smallest, reverse. Playlist tab: new sort icon with the same choices. Sorting by video *length* is not possible without opening every file, so it is not offered.
- **Watched**: files you finished (played to the end, or left after 92 %) are greyed out with a check mark in Playlist and Queue. Saved in `watched-files.json`; `script-message ap-watched-clear` forgets them.
- **Drag to reorder**: no hold needed, press a row and move; the list no longer scrolls by dragging and the player window no longer moves with the mouse. The list auto-scrolls when the carried row nears its top / bottom edge.
- **Progress bar**: glassy track, gradient fill with highlight, shine while playing, knob with a pulsing halo.

# Round 13
New **Remove watched** button (double-tick icon) in the Playlist and Queue tabs: removes the finished files from that list (the files on disk stay). The Queue also has it inside the + menu in case the menu is too narrow to show every icon.

# Round 14
- **Durations**: `ap-queue.lua` reads the length of every file in the Playlist and Queue in the background (ffprobe.exe next to the player, else the preview mpv.exe) and caches it in `durations.json`. "Read all durations" is in both sort menus.
- **Sort by length** (longest / shortest first) in the Queue and Playlist sort menus.
- **Totals** in the bottom right corner of the Playlist and Queue menus: `Total 5:12:30 | Left 4:40:00 | 13 files` (Left = not watched yet; `+` and "not read yet" while some lengths are still unknown).
- **Show in Explorer**: control-bar button (folder with a star) opens Explorer with the playing file selected; every Playlist and Queue row has the same button.
- **Button press indicator**: every control-bar button shows a ripple and the icon dips when clicked.
- **Progress bar**: the dark strip behind it is gone; seeks glide with an ease-out; the gradient flows while playing.
- **Up Next card**: not shown for videos shorter than 10 minutes (`Up_Next.lua`).
- **Resize**: PiP no longer locks the window aspect ratio (after PiP it used to stay locked); the normal window already had it off.
- **Windows snapping**: borderless windows now get WS_MAXIMIZEBOX + WS_THICKFRAME (frame removed in WM_NCCALCSIZE), so drag-to-edge / Win+Arrow snapping works. Needs an exe rebuild.

# Round 15
**What I found when I investigated next / shuffle / queue**
- A queue video was put into mpv's playlist as a real extra entry. If the same file was already in the playlist (same folder) it was a *copy*: pressing Next played it, then the original played again later. And the extra entry was listed in the Playlist tab - "the queue jumps to the playlist".
- Shuffle / sort / drag in the Playlist tab moved that extra entry around, so "next" was not what the Queue said.
- The Playlist menu uses a palette search bar; that blocked drag and Ctrl+Up/Down reordering in the Playlist tab (only a *typed* search blocks it now).

**What changed (`ap-queue.lua`, `main.lua`, `Menu.lua`)**
- The next queue video is staged right behind the playing one (so end of file and the Next button play it) but the Playlist tab never lists it, and its copy is removed from the playlist as soon as the next file starts.
- If the queue video is already in the playlist, that entry is moved (not copied) and moved back if you edit the queue, so nothing plays twice.
- Shuffle (new `ap-playlist-shuffle`), sort, remove-watched, drag, "Play next" all keep the staged video right behind the current one.
- The Queue tab keeps showing the video as "up next" until it really starts, then it leaves the queue.

**Progress bar**: the light that ran from the start is gone. A soft comet tail now follows the knob (it breathes while playing), the bar lights up between the knob and the mouse while hovering, plus the flowing gradient, glow and eased seeks from before.

**input.conf**: right click is the original build's player menu (`script-binding uosc/menu`) again, with Key bindings / Controls inside it, instead of the plain Windows menu. Run build.bat again so input.conf is rebuilt.

# Round 16
**Player**
- Right click is mpv.net's own menu again (`script-message-to mpvnet show-menu`). The **Input Editor** (right-click menu > Settings > Input Editor) needs a plain-format `input.conf` (no `#menu:` lines) to show the named rows - `File > Open Files...` etc. - with their keys; the file the build produces stays in that format (every line is `KEY command`), so the editor works as in the original.
- Progress bar trail: the banded white blocks are replaced by a smooth, blurred glow that fades out behind the knob; the gradient has finer steps.

**AnimeLibrary** (rebuild the library: `build.bat`)
- Back chip goes one level up and says where it goes (folder -> series -> folder collection -> library). Mouse back button also works inside a folder collection.
- Header banner fills the header in a small window (it was centred at its own height: black bars).
- Two buttons: **Scan folders** (folders + titles + covers) and **Update MAL** (progress upload).
- `+` / `-` are two round buttons with hover / press animation.
- Watched: the player writes "finished" at 85 % or end of file immediately (and for non-anime-looking files too); the library marks that exact file watched, also OVAs / files without an episode number.
- OVA / OAD / SP files (and episode numbers just above the provider total) are added to the total: `Ep 0 / 26`.
- Titles the provider calls a MOVIE are shown as movies (Psycho-Pass: Sinners of the System).
- Unmatched titles get an AniList-first fallback with a softer threshold; the "sequel chain" no longer overrides a good direct match (wrong season / poster).
- **Group...** (right-click and series page): tick series, name the folder; they show as one folder card whatever the disk layout is (use it for Monogatari). Empty name = ungroup.

# Round 17
- **Key binding editor inside the player** (`ap-keybinds.lua`): F9, the keyboard button in the control bar, or right-click > Custom > Key bindings. Lists every binding of input.conf (with the `#` description); click a row (or its keyboard button), press the new key -> written to input.conf (a backup `input.conf.keyeditor.bak` is made once) and active immediately. Row buttons: change key / unbind (comment out) / delete line. Tools: add a binding (type the command, then press the key; needs mpv 0.38+) and open mpv.net's own Input Editor (Ctrl+i). Taking a key that is in use comments the old line out. input.conf stays in plain `KEY command # description` format so the mpv.net Input Editor still works with it.
- **Library only looks inside the folders you added**: files played from anywhere else are ignored (no more "detected" series), and old detected entries are dropped on the next scan.

# Round 18 (AnimeLibrary)
- **MyAnimeList "400 Bad Request" after Allow**: the login values are now letters/digits only; a new option in Settings > Tracking, "Do not send a redirect URL", lets MAL use the redirect registered for your app (tick it if the 400 page appears; your app on myanimelist.net/apiconfig needs the redirect URL `http://localhost:8765/cb`, app type "other"). If nothing reaches the app within 3 minutes you can now paste the address of the page you ended up on instead of just failing.
- **"Completed" on a series whose total is unknown** (e.g. "Ep 1 / ?" + Unmatched) is gone: Completed needs a known total.
- The round and animated `+` / `-` buttons from round 16 replace the old joined boxes shown in your screenshot (rebuild the library to see them).

# Round 19
- **input.conf**: `portable_config\input.conf` is now YOUR file (input_1.conf, unchanged, first) followed by every AnimePlayer binding whose key your file does not use (67 lines, under "AnimePlayer additions"). Its first line is `#AP-FULL-INPUT`, which tells the build (`merge-config.ps1`) to install it exactly as it is (your old one is kept as `input.conf.ap-backup`) instead of merging key by key.
- **Windows snap layouts / Snap Assist**: the player's own maximize button (top bar) now tells Windows it is a real maximize button (`HTMAXBUTTON`), so Windows 11 shows the snap layouts flyout when you hover it and Snap Assist suggests windows for the other half after snapping. `TopBar.lua` publishes the button's position (`user-data/ap/max-button`), `MainForm.cs` answers WM_NCHITTEST. Clicking it still uses the player's maximize logic. (Dragging to an edge and Win+Arrow snapping came in round 14.) Needs an exe rebuild.
- **Scoring window** (AnimeLibrary): ten stars you can hover and click, big number, MAL's word for it (Masterpiece ... Appalling), keys 1-9 / 0 (=10) / Backspace (no score) / Enter / Esc. Open it from the series page (star button) or the card's right-click menu "Set score..."; saved scores upload to MAL with Update MAL.

# Bug check (round 19b)
Static review of everything written so far (nothing could be run here): fixed a leftover that still created temporary 'Detected' series for unscanned files inside library folders, and made the Snap-Layouts button rectangle integer-safe. Checked: Lua block balance in every script, load order of every local function in ap-queue.lua, variable scope in Button / Menu / Timeline / Controls, the patch pipeline against your original scripts, brace balance of all edited C# files, and that every C# helper / icon name used actually exists.

# Round 20
- **The window keeps its size, position and state after a file change** (`MainForm.cs` `KeepSize()`, `App.cs`): the first video sizes the window as before (autofit); after that the next file / episode / queue video / playlist jump never resizes or moves it, and windowed, maximized and fullscreen stay as they are (also when you leave and re-enter fullscreen). Only `start-size = video` in mpvnet.conf makes the window follow every video again. Needs an exe rebuild.

# Round 21
- **Reset speed button** (`ap-speed.lua`, `speed-osd.lua`): now a toggle. First press = default speed (the speed shown for what you had is remembered); press again while at the default = back to the speed you had. The old second handler in `speed-osd.lua` that also forced 1x is removed so the two cannot fight.
- Default speed is fixed at 1x (it was read from the player's start-up value before).

# Round 22 (playlist / queue menus)
**Investigation - "remove watched did nothing in the Playlist tab"**
- The code path is the same as for the Queue; the only differences are (1) the Playlist tab lives next to `autoload.lua`, which rebuilds the playlist from the folder whenever only one entry is left, and which was never told about a deliberate clear, and (2) the watched list was matched by the exact path string, so a path written with different case / slashes in the log and the playlist did not match. Both are fixed: watched keys are now case- and slash-insensitive in `ap-watched.lua`, `ap-queue.lua` and the playlist menu, and `autoload.lua` stops while a playlist you cleared is playing (`user-data/ap/no-autoload`, reset when that file ends).
- In a narrow window the first tool button (the double tick) was also the first one dropped when the tab row ran out of room - which is why it could be missing in one tab and present in the other. Tools that do not fit now go into a "more" button.

**Layout**
- Tab-row buttons: whatever does not fit moves into a `...` button that opens a small menu with the hidden tools.
- Row buttons: in a window narrower than 620 px a row shows only the essential buttons (Playlist: add to queue, show in Explorer, remove; Queue: show in Explorer, remove); reorder by dragging or ctrl+up/down.
- The totals text no longer draws over the "Show in Explorer" label of the hovered row button.

**New**
- Playlist tab: "Remove all videos (keeps the playing one)" button. Queue tab: the clear button is now "Remove all videos from the queue".

# Round 23
- **Resizable tab row** (`Menu.lua`): the Playlist | Queue row follows the size of the menu and of the player window - tab text, padding, gaps and the tool buttons scale between 70 % and 110 % and stay centred on the tabs; buttons that still do not fit go into the `...` button (round 22).

# Round 24
- **Queue**: a queue video no longer disappears the moment it starts. It stays in the Queue tab as the active row ("playing") and leaves the list when it ends (or when you move on). Clicking a row twice quickly used to start the next row too (the list had already moved up) - one start per 0.8 s now.
- **Key bindings button / F9**: any error inside the editor is now shown on screen instead of nothing happening, and an empty / missing input.conf says where it looked. (I ran the editor under a simulated player: it loads and builds its menu from your input.conf without errors.)
- **Row buttons follow the window size live** in both tabs (they were decided only when the menu opened for the Queue).
- **Length of each video** at the end of every row of the Playlist and Queue tabs (`24:10   5`), read by the background duration scan.
- **Window snapping**: the emulated snap now looks and behaves like Windows' own - a translucent preview while you drag to an edge (halves, quarters, top edge = whole work area) and the window lands there on release; dragging it away restores its old size. Menus can no longer leave "window dragging" switched off (that would also stop dragging / snapping). The native styles from round 14 (Win+Arrow, snap layouts flyout) are still in; they need a rebuilt exe and Windows 11 for the flyout.
- Every Lua file of the finished build (your scripts + all patches + mine, 71 files) now passes a real Lua 5.3 syntax check.

# Bug check (round 24b)
Found a real Lua interpreter in the sandbox (texlua) and used it:
- every Lua file of the finished build (71) parses; no undefined globals in the ap-*.lua scripts;
- simulated mpv playlist for ap-queue.lua (tools/tests): the hidden "next" queue video, no duplicate when the video is already in the playlist, shuffle / sort / clear keep it right behind the playing one, the playing row stays until the video ends, drag / remove / up-down map to the right rows, the double-click guard works;
- the centred control bar tested at 1920 / 1280 / 900 / 600 / 420 / 300 px: the centre block is always exactly centred and never hidden, the side groups hug it and give way;
- the key binding editor edits a scratch input.conf correctly (rebind, conflict -> old line commented out, unbind, backup).
Bug found and fixed: the double-click guard started at 0, so a click in the first 0.8 s of a session was ignored; it now starts at -100.
Not testable here (no Windows / .NET): all C# (player window, drop zones, snapping, library).
