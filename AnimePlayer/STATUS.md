**AnimePlayer build 37** - current behaviour and verification status are recorded below; the build number is stored in BUILD.txt.

# AnimePlayer rev9 - round 6 (see round 5 below for the earlier list)

## Round 6 - menu bug PROVEN, plus the new requests
ROOT CAUSE of "the Menu / Controls buttons do nothing and the UI disappears" and "Advanced Controls does not work" (reproduced by running the real, patched
uosc under a mock mpv - tools are not shipped, results are):
- The build's menus reuse menu ids ('sub_delay_menu' and 'sub_pos_menu' twice, two 'Utils'). uosc looks menus up by id; the second one wins, the first one has no
  scroll_height yet and clamp(nil) throws WHILE THE MENU IS BEING CREATED. The half-built menu element stays behind -> blank UI. Patch 20 gives every repeated id a suffix.
  (The round-5 keep_open / own-type fix for the anime menu stays: that was a second, separate problem.)
Player:
- Double click: no fullscreen any more (it also toggles pause back, so it changes nothing). Fullscreen = F / Enter / button.
- Control bar: tools left, playback cluster (prev, frame back, play/pause, frame forward, next) in the centre, tools right; buttons 40 px with more spacing.
  The centre is only as exact as the two sides are equally wide (video-only buttons appear/disappear).
- UI themes: Anime Build Options > UI Theme (Amber, Violet, Ocean, Emerald, Sakura, Crimson, Midnight, Mono, Light) and Accent Color; live, remembered in uosc_theme.json.
- Playlist | Queue are real tabs now (Queue row removed from the playlist); the Queue menu has the same tabs. Progress bar glides instead of ticking.
Library:
- Now Playing removed. - / + is one segmented control with drawn icons, both halves the same size.
- Franchise folders: several series in one folder (Monogatari/Bakemonogatari, Nisemonogatari ...) = ONE card with the cover of the first series; click = the series in it; click = episodes. Esc goes back.
- Movies: found by name ("Movie 2 - ...", "... the Movie", a year in brackets and no episode number); unrelated films in one folder stay separate; matched as MOVIE.
- Sequels: Second/Third... Season, Final Season, Part N, "Part 2" is never merged into Part 1; Final Season gets the number after the highest season found.
- Fixed names: fullwidth ／ ： ！ etc, unbracketed fansub names (Commie_...), "Death Parade Special" (a lone special is kept and matched as a special), Movie 2 - Kino no Tabi...
- Indicators: Completed / Airing / Ended / Movie / Special / "Ep N watched - today" chips under the poster; green check on watched episodes in the series page;
  "Finished episode N of ..." message when the player finishes one.
- Settings: more colour themes, Surface (Dark / AMOLED / Midnight / Slate / Warm, needs restart), indicators on/off.
Not done / honest: a light surface theme (text colours are hard-coded white in places), exact centring of the control bar, "Arata naru Sekai" is only improved (the title keeps "World's"; the fallback search drops it).
Verification: C# against hand-made WPF stubs + a real scan of a fake library (names above); Lua = real uosc run under a mock mpv (menu, controls, tabs, themes, no render errors). Not run on Windows.

# AnimePlayer rev9 - round 5 (compile-checked only against WPF stubs; not run on Windows)

## Round 5 - what changed
ROOT CAUSE of the anime menu vanishing (found by reading your real scripts.zip, not guessed):
- uosc closes a menu after an item is activated unless the menu / item has `keep_open` (lib/menus.lua). The build's anime menu
  (anime_profile_controller.lua, "Anime Build Options") never set it, so EVERY click on an option closed the menu before the controller's
  update-menu could refresh it. Patch 19 adds `keep_open = true`.
- Same menu used type "menu", the type of uosc's OWN main menu, so each update-menu from the controller overwrote the main menu whenever that was open.
  It is now type "anime_menu".
- Applied as patch 19 AND as a tested overlay of the whole controller (manifest 05); the overlay + patches were checked against your exact scripts.zip files
  (all patches apply, hashes match, Lua parses). Earlier render-isolation / stable-id patches stay (they were guards, not the cause).

MyAnimeList authentication (MalClient.cs rewritten):
- Refresh tokens are single-use: the profile load and the sync both refreshed at once, the second got invalid_grant and the stored login was lost. Refresh is now serialised.
- The expiry time is stored and the token is refreshed before it runs out. A dead login is detected, cleared and shown ("Login expired" on the profile card).
- OAuth `state` is checked; after login the user name is read from MAL itself ("connected" really means the token works); Client ID format check (32 hex chars);
  429/5xx retry; Log out; Test connection (Settings > Tracking and the profile card menu). The paste-the-address fallback is now a real dialog (it used to put the instructions in the window title).
- Still needed from you: your own MAL app (myanimelist.net/apiconfig, redirect http://localhost:8765/cb). Taiga's baked-in client id cannot be reused.

Library redesign (TORA-inspired, not copied):
- Sidebar: Now Playing, Library (books icon), Unmatched, Updates, Folders, Extensions, Settings, Open player, MyAnimeList profile card (avatar, name, menu: profile / test / log in / log out). Hide button top-left (Ctrl+B).
- Status tabs with counts: Watching / Plan to Watch / Completed / All; search (Ctrl+K), sort, random title, scan, 3 view modes.
- Posters carry NOTHING: no text, symbols, badges or bars. Under the poster: progress bar, title, "Ep x / y" with -  / +1, then chips: Completed, Airing / Ended, score, Unmatched, Waiting for upload.
- Cards are built in batches while scrolling (big libraries no longer freeze); live updates keep the scroll position.
- New library-style icon (books) for the app and the sidebar. Settings: obsolete "group by status" removed.
Bugs fixed on the way: search matched the display title twice and never the real title; the login prompt hid its instructions in the title bar; list totals/badges.

## NOT verified (be honest)
- Nothing was run on Windows. The C# was compiled in a sandbox against hand-made WPF stubs: names, scoping and non-WPF types are checked, WPF member usage is not.
  Paste the first build errors from build.bat and they get fixed.
- Lua: syntax-checked and patch-applied against your files; the behaviour of the menu fix is from reading uosc's code, not from running mpv.

## DONE in this round (player side)
- input.conf rewritten from the real PotPlayer 1.7 key list (defkey.com); merge now moves any displaced anime-build feature to Alt+<key> /
  Ctrl+Alt+<key> instead of losing it, also for keys the build's own Lua scripts bind (c, PgUp, PgDn ...); duplicates inside the build are removed.
- uosc hover bar: the Queue button is gone; "Resume where you stopped" + new "Start from the beginning" buttons; the old auto-stretch icon is
  replaced by three icons (Auto/Fit, Forced/Fill, Smart/Crop) that send the build's own `zoom-mode-fit|fill|crop` messages, so they stay in sync
  with its "UltraWide Zoom" menu. The build's "Next Chapter" / "Prev Chapter" buttons are untouched.
- Playlist menu: a "Queue" row as the first entry (opens the queue tools); move / remove / copy handlers made safe for that extra row.
- uosc error guard: Lua errors in script-message handlers and timers are logged to `<config>\uosc-errors.log` instead of silently stopping a redraw.
- ap-speed.lua / ap-volume.lua: speed and volume now survive file changes (the build resets speed on the next file; the old script mistook that
  for your choice). ap-fullscreen.lua keeps fullscreen after a file change.
- Screenshots go to the Windows Pictures folder (the build's template contained its own folder, which overrode the directory).
- Window snapping: drag to a screen edge = half, corner = quarter, drag away restores the size (WindowSnap.cs; no border hacks, fullscreen untouched).
- (earlier rounds, still included) merge-script JSON fix, middle mouse 5 s, queue/resume/subtitle-adjuster patches, library rev8 (series page,
  smart names, OP/ED/music detection, covers from bytes, matching scores, custom chrome/theme).

## Round 3 (not compiled / not run)
DONE
- uosc menu disappearing: render isolation (a throwing element is logged to uosc-errors.log and skipped, a broken menu closes instead of blanking everything),
  stable submenu ids (state text in a title no longer changes the id), guarded + de-duplicated anime-state broadcast / update-menu. Overlay main.lua + patches 12-15.
- verify-config.ps1: stale checks fixed (right-click belongs to uosc; hover bar checks the new Resume/Restart/zoom buttons; new render-guard check).
- Library: matching fixes (season from brackets, noise removal, truncated/misspelled names, sequel chain S2-S4, Monogatari colours), reason per unmatched
  series, Retry matching, "Ep x to Ep y" folder tiles, first-subfolder cover, scroll position restored when leaving the series page.
- Round 3b: library cards have -/+1 buttons, a green "done" chip and a score chip; right-click on the progress bar now opens the uosc menu (timeline_mbtn_right, was empty);
  ap-responsive.lua written (hover-bar icons scale with the window height).
- Round 3c: airing / ended chip on cards (status stored from AniList / Jikan, 40 older series back-filled per sync); MAL profile avatar in the rail (hidden until logged in).
- Round 4: right-click on the progress bar jumps to the chapter under the pointer (patch 16; a custom timeline_mbtn_right still wins).
  MAL login rewritten (favicon request no longer aborts it, Client Secret for "web" apps, configurable redirect, paste-address fallback, readable error text).
  Taiga uses its own baked-in client id + taiga.moe relay, which cannot be reused here, so you still need your own MAL app (apiconfig).
  RTX VSR replaced by a GPU shader upscaler for AMD (Anime4K Fast + EWA scalers) in vsr_auto.lua; same V key / toggle-vsr / menu entry, relabelled.
- MyAnimeList: Episode +1 / -1 (progress CAN go down now), score 0-10, "Mark as completed" (right-click menu and series page); uploaded automatically.
NOT DONE
- Right-click on progress bar: only worked around (see 3b); the real cause is not confirmed.
- Library UI redesign (TORA style: status tabs with counts, clean cards, sidebar / Now Playing), books icon, "series ended" indicator (needs the AniList status stored per series).
- Taiga (taiga-develop.zip) was only used as the reference for the MAL PATCH fields; no code taken from it.
## DEPENDENCIES
To build (on your PC)
- Windows 10/11 x64, .NET 10 SDK (with the Windows Desktop / WPF + WinForms support), PowerShell 5.1+, robocopy, internet for NuGet.
- NuGet (restored automatically): CommunityToolkit.Mvvm 8.4.0, NGettext 0.6.7, Microsoft.Xaml.Behaviors.Wpf 1.1.135.
- Files you add: libmpv-2.dll (shinchiro mpv-dev x86_64) in dll\, ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe, the mpv-anime-build contents in mpv-anime-build\.
- Library online features: AniList + Jikan + Kitsu (no key), MyAnimeList Client ID (myanimelist.net/apiconfig, redirect http://localhost:8765/cb).
What I need to keep writing code
- You running build.bat and pasting the first error lines (I cannot compile).
- For the two uosc bugs: `uosc-errors.log` and, ideally, the anime build's `scripts\` folder (main anime menu script, Timeline.lua if present).
- A real screenshot of any UI you want changed (the library reference image you sent is enough for the redesign).
