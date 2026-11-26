<# verify-config.ps1 - run by build.bat after the config was installed. Checks that nothing of the anime build is missing
   and that every AnimePlayer addition is in place. Only reads files. #>
param([Parameter(Mandatory = $true)][string]$Cfg)
trap { Write-Host ('ERROR: ' + $_.Exception.Message); exit 0 }
$ok = 0; $bad = 0
function Check([string]$what, [bool]$cond) {
    if ($cond) { Write-Host ('  [ok]  ' + $what); $script:ok++ } else { Write-Host ('  [!!]  ' + $what); $script:bad++ }
}
function Has([string]$rel, [string]$needle) {
    $p = Join-Path $Cfg $rel
    return ((Test-Path -LiteralPath $p) -and ([System.IO.File]::ReadAllText($p).Contains($needle)))
}
Write-Host "Checking $Cfg"
Check 'mpv.conf exists' (Test-Path -LiteralPath (Join-Path $Cfg 'mpv.conf'))
Check 'input.conf exists' (Test-Path -LiteralPath (Join-Path $Cfg 'input.conf'))
$anime = Test-Path -LiteralPath (Join-Path $Cfg 'scripts\uosc\main.lua')
if ($anime) {
    Check 'anime build: uosc (right-click menu + hover bar) present' $true
    Check 'mpv.conf is the anime build''s own (not the AnimePlayer basic one)' (-not (Has 'mpv.conf' '===== AnimePlayer mpv.conf'))
    Check 'input.conf is the anime build''s own (not the AnimePlayer basic one)' (-not (Has 'input.conf' 'AnimePlayer input.conf - PotPlayer-style'))
    Check 'no clashing old scripts\anime-detect.lua' (-not (Test-Path -LiteralPath (Join-Path $Cfg 'scripts\anime-detect.lua')))
    # right-click = the mpv.net menu; the uosc menu is on the MENU key / hover bar
    Check 'uosc menu still reachable (MENU key / hover bar)' (Has 'input.conf' 'uosc/menu')
    Check 'input.conf is the full original mpv.net input.conf (#menu: entries: right-click menu and key editor list every shortcut)' (Has 'input.conf' '#menu: Navigate > Jump 5 sec backward')
    Check 'right-click opens the mpv.net menu' (Has 'input.conf' 'MBTN_RIGHT  script-message-to mpvnet show-menu')
    Check 'double-click is bound (ap-dblclick)' (Has 'input.conf' 'MBTN_LEFT_DBL  script-message ap-dblclick')
    Check 'anime build: Anime profile controller present' (Test-Path -LiteralPath (Join-Path $Cfg 'scripts\anime_profile_controller.lua'))
    Check 'hover bar: Resume / Start from beginning / zoom icons' ((Has 'script-opts\uosc.conf' 'ap-resume-now') -and (Has 'script-opts\uosc.conf' 'ap-restart-now') -and (Has 'script-opts\uosc.conf' 'ap-zoom-off'))
    Check 'hover bar: original buttons still there (speed, play-pause, fullscreen)' ((Has 'script-opts\uosc.conf' ',speed,') -and (Has 'script-opts\uosc.conf' 'play-pause') -and (Has 'script-opts\uosc.conf' 'fullscreen'))
    Check 'playlist menu: queue buttons' (Has 'scripts\uosc\main.lua' 'AnimePlayer: queue actions')
    if (Test-Path -LiteralPath (Join-Path $Cfg 'scripts\vsr_auto.lua')) { Check 'upscaler: AMD-friendly GPU Upscaler (no NVIDIA RTX VSR)' (Has 'scripts\vsr_auto.lua' 'GPU Upscaler') }
    Check 'progress bar: right-click jumps to the chapter under the pointer' (Has 'scripts\uosc\elements\Timeline.lua' 'right-click chapter')
    Check 'uosc: render errors are logged instead of blanking the UI' (Has 'scripts\uosc\lib\utils.lua' 'AnimePlayer: render isolation')
    Check 'anime menu: stays open after a click (keep_open) and has its own menu type' (Has 'scripts\anime_profile_controller.lua' 'anime menu stays open')
    Check 'uosc: duplicate menu ids no longer crash the menu / controls menu' (Has 'scripts\uosc\elements\Menu.lua' 'AnimePlayer: unique menu ids')
    Check 'uosc: live UI themes (script-message set-theme)' (Has 'scripts\uosc\main.lua' 'AnimePlayer: ui themes')
    Check 'uosc: Playlist | Queue tabs' (Has 'scripts\uosc\elements\Menu.lua' 'AnimePlayer: menu tabs draw')
    Check 'uosc: smooth progress bar' (Has 'scripts\uosc\elements\Timeline.lua' 'AnimePlayer: smooth progress')
    Check 'Controls > Subtitles: visual adjuster' (Has 'scripts\uosc\main.lua' 'AnimePlayer: visual subtitle adjuster')
}
else { Write-Host '  (no uosc here: basic AnimePlayer config)' }
foreach ($s in 'ap-smooth.lua', 'ap-resume.lua', 'ap-queue.lua', 'ap-video.lua', 'ap-speed.lua', 'ap-watch.lua', 'ap-volume.lua', 'ap-fullscreen.lua', 'ap-responsive.lua', 'midclick-jump.lua', '00-thumbfast-path.lua', 'ap-screenshot.lua', 'ap-eof-guard.lua') {
    Check ('script ' + $s) (Test-Path -LiteralPath (Join-Path $Cfg ('scripts\' + $s)))
}
Check 'middle mouse button = jump forward (bound inside scripts\midclick-jump.lua)' (Has 'scripts\midclick-jump.lua' "add_key_binding('MBTN_MID'")
if ($bad -eq 0) { Write-Host ("All $ok checks passed.") } else { Write-Host ("$bad check(s) FAILED - see [!!] above and key-conflicts.txt"); }
exit 0
