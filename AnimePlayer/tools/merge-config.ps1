<#
  merge-config.ps1 - adds AnimePlayer to an EXISTING mpv-anime-build config WITHOUT replacing any of it.
  Everything is additive and idempotent (running build.bat twice changes nothing the second time); every file that is edited gets a
  one-time backup next to it (<file>.ap-backup). Every section runs on its own: if one fails its exact error is printed and the others
  still run, so one problem can never block the rest.

    0. repair      mpv.conf / input.conf that an EARLIER AnimePlayer build had replaced are restored from your backup / the build folder
    1. uosc files  control-bar buttons (uosc.conf), queue buttons in the playlist menu and the subtitle adjuster (uosc/main.lua),
                   a duplicate-buttons fix (uosc/lib/menus.lua):
                     a) if your file is byte-identical to the original I tested, the tested patched copy from tools\overlay is installed;
                     b) otherwise small anchored edits (tools\patches.json) are applied, each only when its anchor exists exactly once.
    2. input.conf  PotPlayer keys (CORE) win over the build's input.conf, EXTRA keys only fill free keys; keys that the build's SCRIPTS
                   bind by default (autocrop C, betterchapters PgUp/PgDn, ...) are never taken over.
    3. thumbfast   path of the standalone mpv.exe for the seek-bar preview.
  mpv.conf, shaders, profiles and every other script are not touched. A report is written to key-conflicts.txt.
#>
param(
    [Parameter(Mandatory = $true)][string]$Cfg,
    [Parameter(Mandatory = $true)][string]$PatchFile,
    [int]$PotWins = 1,
    [string]$MpvExe = '',
    [string]$TargetDir = '',     # the config folder that is installed now (used to find your backups)
    [string]$BuildSrc = ''       # the mpv-anime-build folder, when there is one
)
$ErrorActionPreference = 'Stop'
trap { Write-Host ('ERROR: ' + $_.Exception.GetType().FullName + ': ' + $_.Exception.Message); Write-Host $_.ScriptStackTrace; exit 1 }

$utf8 = New-Object System.Text.UTF8Encoding($false)
$report = New-Object System.Collections.Generic.List[string]

function Read-Lines([string]$p) {
    $t = [System.IO.File]::ReadAllText($p)
    if ($t.Length -gt 0 -and $t[0] -eq [char]0xFEFF) { $t = $t.Substring(1) }
    return [string[]]($t -split "`r?`n")      # NO leading comma: callers wrap the call in @(...), a comma would nest the array one level deeper
}
function Write-Lines([string]$p, $lines) {
    [System.IO.File]::WriteAllText($p, (($lines -join "`r`n") + "`r`n"), $utf8)
}
function Backup-Once([string]$p) {
    $b = $p + '.ap-backup'
    if ([System.IO.File]::Exists($p) -and -not [System.IO.File]::Exists($b)) { [System.IO.File]::Copy($p, $b) }
}
function Get-Sha([string]$p) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($p)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
# Every edit / overlay entry is its own small JSON file holding ONE object. (A top-level JSON array is returned by Windows PowerShell 5.1
# as a single object whose properties are arrays - every id joined together - which broke the earlier versions of this script.)
function Read-JsonDir([string]$dir) {
    # returns the objects one by one (NO leading comma): every caller wraps the call in @(...) which re-collects them into a flat array.
    # (rev7 returned ",(array)" AND the callers used @(), so the result was an array that contained one array: "loaded 1 edit definitions",
    # and every entry then looked like "an array" - exactly the message in the build log.)
    if ([System.IO.Directory]::Exists($dir)) {
        foreach ($f in (Get-ChildItem -LiteralPath $dir -Filter '*.json' -File | Sort-Object Name)) {
            ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText($f.FullName))
        }
    }
}
function Fail-Note([string]$section, $err) {
    $msg = 'ERROR in section "' + $section + '": ' + $err.Exception.GetType().Name + ': ' + $err.Exception.Message
    $script:report.Add($msg)
    Write-Host ('[!] ' + $msg)
    Write-Host $err.ScriptStackTrace
}

# ================================================================ 0. repair what an earlier AnimePlayer build replaced
# Builds before revision 3 could copy MY small mpv.conf / input.conf over the anime build's own ones when they did not find the build
# folder. That removes the right-click menu, the hover bar and the anime profiles. Those two files are recognised by the text of my header
# and restored from your newest backup (or from the mpv-anime-build folder). Nothing else is replaced.
try {
    $mineConf = '===== AnimePlayer mpv.conf'
    $mineInput = 'AnimePlayer input.conf - PotPlayer-style'
    function Is-Mine([string]$path, [string]$needle) {
        return ([System.IO.File]::Exists($path) -and ([System.IO.File]::ReadAllText($path).Contains($needle)))
    }
    function Find-GoodConfig {
        if ($TargetDir -ne '') {
            $parent = [System.IO.Path]::GetDirectoryName($TargetDir)
            $leaf = [System.IO.Path]::GetFileName($TargetDir)
            if ($parent -and [System.IO.Directory]::Exists($parent)) {
                $dirs = @(Get-ChildItem -LiteralPath $parent -Directory -Filter ($leaf + '.backup-*') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
                foreach ($d in $dirs) {
                    $mc = [System.IO.Path]::Combine($d.FullName, 'mpv.conf')
                    if ([System.IO.File]::Exists($mc) -and -not (Is-Mine $mc $mineConf) -and [System.IO.File]::Exists([System.IO.Path]::Combine($d.FullName, 'scripts\uosc\main.lua'))) { return $d.FullName }
                }
            }
        }
        if ($BuildSrc -ne '') {
            $bc = [System.IO.Path]::Combine($BuildSrc, 'mpv.conf')
            if ([System.IO.File]::Exists($bc) -and -not (Is-Mine $bc $mineConf)) { return $BuildSrc }
        }
        return $null
    }
    $stageConf = [System.IO.Path]::Combine($Cfg, 'mpv.conf')
    $stageInput = [System.IO.Path]::Combine($Cfg, 'input.conf')
    $needConf = Is-Mine $stageConf $mineConf
    $needInput = Is-Mine $stageInput $mineInput
    if ($needConf -or $needInput) {
        $good = Find-GoodConfig
        if ($good) {
            if ($needConf) {
                [System.IO.File]::Copy([System.IO.Path]::Combine($good, 'mpv.conf'), $stageConf, $true)
                $report.Add('REPAIRED mpv.conf: an earlier AnimePlayer build had replaced yours with its basic one; restored from ' + $good)
            }
            if ($needInput) {
                $gi = [System.IO.Path]::Combine($good, 'input.conf')
                if ([System.IO.File]::Exists($gi)) {
                    [System.IO.File]::Copy($gi, $stageInput, $true)
                    $report.Add("REPAIRED input.conf (right-click menu and the build's keys): restored from " + $good)
                }
                else { $report.Add('WARNING: input.conf was replaced by an earlier AnimePlayer build and ' + $good + ' has no input.conf to restore') }
            }
        }
        else {
            $report.Add('WARNING: your mpv.conf / input.conf were replaced by an earlier AnimePlayer build and I found NO copy of the originals. Put the anime build into the mpv-anime-build folder (it must contain mpv.conf) and run build.bat again - until then the right-click menu and the anime modes stay broken.')
        }
    }
    # my own old files that clash with the anime build's features
    $apc = [System.IO.Path]::Combine($Cfg, 'scripts\anime_profile_controller.lua')
    $det = [System.IO.Path]::Combine($Cfg, 'scripts\anime-detect.lua')
    $isMyDetect = (Is-Mine $det 'toggle-lite') -or (Is-Mine $det 'looks_like_anime') -or (Is-Mine $det 'Anime-aware auto-detection')
    if ([System.IO.File]::Exists($apc) -and $isMyDetect) {
        [System.IO.File]::Delete($det)
        $report.Add("removed scripts\anime-detect.lua (my old anime detection; the build's anime_profile_controller.lua does that job)")
    }
    foreach ($old in 'scripts\midclick-ff.lua', 'scripts\watch-log.lua', 'script-opts\midclick-ff.conf') {
        $op = [System.IO.Path]::Combine($Cfg, $old)
        if ([System.IO.File]::Exists($op)) { [System.IO.File]::Delete($op); $report.Add('removed obsolete ' + $old + ' (replaced by midclick-jump / ap-watch)') }
    }
}
catch { Fail-Note 'repair' $_ }

# ================================================================ 1. uosc files: tested overlay first, anchored patches as the fallback
try {
    $done = @{}      # files that the overlay already handled
    $toolsDir = [System.IO.Path]::GetDirectoryName($PatchFile)
    $overlayDir = [System.IO.Path]::Combine($toolsDir, 'overlay')
    $manifestDir = [System.IO.Path]::Combine($overlayDir, 'manifest')
    $manifest = @(Read-JsonDir $manifestDir)
    if ($manifest.Count -gt 0) {
        foreach ($it in $manifest) {
            try {
                if ($it.file -is [System.Array]) { throw 'overlay entry was read as an array (PowerShell JSON quirk)' }
                $fileKey = [string]$it.file
                $rel = $fileKey.Replace('/', '\')
                $target = [System.IO.Path]::Combine($Cfg, $rel)
                if (-not [System.IO.File]::Exists($target)) { continue }
                $cur = Get-Sha $target
                $wantPatched = [string]$it.patchedSha
                $wantOrig = [string]$it.origSha
                if ($cur -eq $wantPatched) { $done[$fileKey] = 'already'; continue }
                if ($cur -eq $wantOrig) {
                    Backup-Once $target
                    [System.IO.File]::Copy([System.IO.Path]::Combine($overlayDir, $rel), $target, $true)
                    $done[$fileKey] = 'overlay'
                    Write-Host ('  overlay installed: ' + $rel)
                }
            }
            catch { Fail-Note 'overlay' $_ }
        }
    }

    function Apply-Patch($p) {
        $step = 'start'
        try {
            $rel = ([string]$p.file).Replace('/', '\')
            $step = 'path'
            $path = [System.IO.Path]::Combine($Cfg, $rel)
            if (-not [System.IO.File]::Exists($path)) { return 'missing' }
            $step = 'read'
            $raw = [System.IO.File]::ReadAllText($path)
            $bom = ($raw.Length -gt 0 -and $raw[0] -eq [char]0xFEFF)
            if ($bom) { $raw = $raw.Substring(1) }
            $crlf = $raw.Contains("`r`n")
            $t = $raw.Replace("`r`n", "`n")
            $step = 'marker'
            if ($t.Contains([string]$p.marker)) { return 'already' }
            $step = 'find'
            $find = [string]$p.find
            $first = $t.IndexOf($find, [System.StringComparison]::Ordinal)
            if ($first -lt 0) { return 'anchor-missing' }
            if ($t.IndexOf($find, $first + 1, [System.StringComparison]::Ordinal) -ge 0) { return 'anchor-ambiguous' }
            $step = 'backup'
            Backup-Once $path
            $step = 'replace'
            $t = $t.Substring(0, $first) + [string]$p.replace + $t.Substring($first + $find.Length)
            if ($crlf) { $t = $t.Replace("`n", "`r`n") }
            $step = 'write'
            [System.IO.File]::WriteAllText($path, $t, (New-Object System.Text.UTF8Encoding($bom)))
            return 'applied'
        }
        catch { return ('error at step "' + $step + '": ' + $_.Exception.GetType().Name + ': ' + $_.Exception.Message) }
    }

    $patches = @(Read-JsonDir $PatchFile)       # -PatchFile is the tools\patches folder
    $nPatchFiles = @(Get-ChildItem -LiteralPath $PatchFile -Filter '*.json' -File -ErrorAction SilentlyContinue).Count
    $nManifestFiles = @(Get-ChildItem -LiteralPath $manifestDir -Filter '*.json' -File -ErrorAction SilentlyContinue).Count
    Write-Host ('  loaded ' + $patches.Count + ' edit definitions (' + $nPatchFiles + ' files) and ' + $manifest.Count + ' overlay entries (' + $nManifestFiles + ' files)')
    if ($patches.Count -ne $nPatchFiles -or $manifest.Count -ne $nManifestFiles) { $report.Add('ERROR: the number of loaded definitions does not match the number of files in tools\patches / tools\overlay\manifest') }
    $nApplied = 0; $nAlready = 0; $nSkipped = 0
    foreach ($p in $patches) {
        if ($p.file -is [System.Array]) { $report.Add('SKIPPED: an edit definition was read as an array (PowerShell JSON quirk)'); $nSkipped++; continue }
        $pFile = [string]$p.file
        if ($done.ContainsKey($pFile)) {
            if ($done[$pFile] -eq 'already') { $nAlready++ } else { $nApplied++ }
            continue
        }
        $r = Apply-Patch $p
        if ($r -eq 'applied') { $nApplied++ }
        elseif ($r -eq 'already') { $nAlready++ }
        else {
            $nSkipped++
            if ($r -eq 'missing') { $report.Add("SKIPPED patch '" + $p.id + "': " + $p.file + ' does not exist in this config') }
            elseif ($r -eq 'anchor-missing') { $report.Add("SKIPPED patch '" + $p.id + "': the text it attaches to was not found in " + $p.file + ' (a different version of the build?) - that button / menu entry was NOT added') }
            elseif ($r -eq 'anchor-ambiguous') { $report.Add("SKIPPED patch '" + $p.id + "': the anchor appears more than once in " + $p.file) }
            else { $report.Add("SKIPPED patch '" + $p.id + "': " + $r) }
        }
    }
    Write-Host ('uosc edits: ' + $nApplied + ' applied, ' + $nAlready + ' already there, ' + $nSkipped + ' skipped')
}
catch { Fail-Note 'uosc files' $_ }

# ================================================================ 2. input.conf
try {
    $alias = @{
        'MOUSE_BTN0' = 'MBTN_LEFT'; 'MOUSE_BTN1' = 'MBTN_MID'; 'MOUSE_BTN2' = 'MBTN_RIGHT'
        'MOUSE_BTN3' = 'WHEEL_UP'; 'MOUSE_BTN4' = 'WHEEL_DOWN'; 'MOUSE_BTN5' = 'WHEEL_LEFT'; 'MOUSE_BTN6' = 'WHEEL_RIGHT'
        'MOUSE_BTN0_DBL' = 'MBTN_LEFT_DBL'; 'MOUSE_BTN1_DBL' = 'MBTN_MID_DBL'; 'MOUSE_BTN2_DBL' = 'MBTN_RIGHT_DBL'
        'PGDOWN' = 'PGDWN'; 'RETURN' = 'ENTER'; 'ESCAPE' = 'ESC'
    }
    function Norm-Key([string]$k) {
        $m = [regex]::Match($k, '^((?:(?:ctrl|shift|alt|meta)\+)*)(.+)$', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $mods = @()
        if ($m.Groups[1].Value.Length -gt 0) {
            $mods = @($m.Groups[1].Value.TrimEnd('+').ToLowerInvariant().Split('+') | Sort-Object -Unique)
        }
        $key = $m.Groups[2].Value
        if ($key.Length -gt 1) {
            $key = $key.ToUpperInvariant()
            if ($alias.ContainsKey($key)) { $key = $alias[$key] }
        }
        elseif ($mods.Count -eq 1 -and $mods[0] -eq 'shift' -and $key -match '^[a-zA-Z]$') {
            $key = $key.ToUpperInvariant(); $mods = @()    # libmpv reports Shift+letter as the capital letter
        }
        return ($mods -join '+') + '|' + $key
    }
    function Parse-Binding([string]$line) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { return $null }
        $m = [regex]::Match($t, '^(\S+)\s+(.*)$')
        if (-not $m.Success) { return $null }
        return [pscustomobject]@{ Key = $m.Groups[1].Value; Cmd = $m.Groups[2].Value; Norm = (Norm-Key $m.Groups[1].Value); Line = $line }
    }

    $apFile = [System.IO.Path]::Combine($Cfg, 'animeplayer-input.conf')
    $inFile = [System.IO.Path]::Combine($Cfg, 'input.conf')
    $blockMark = '# ===== AnimePlayer additions'
    $winMark = '# [AnimePlayer: PotPlayer key wins] '
    $dupMark = '# [AnimePlayer: duplicate off] '

    if ([System.IO.File]::Exists($apFile)) {
        $ap = @{ CORE = @(); EXTRA = @(); STANDALONE = @() }
        $section = $null
        foreach ($l in (Read-Lines $apFile)) {
            $sm = [regex]::Match($l, '^\s*###\s*(CORE|EXTRA|STANDALONE)\s*$')
            if ($sm.Success) { $section = $sm.Groups[1].Value; continue }
            if ($null -eq $section) { continue }
            $b = Parse-Binding $l
            if ($b) { $ap[$section] += $b }
        }

        # a complete input.conf (marked #AP-FULL-INPUT on its first line: the user's file + every extra AnimePlayer binding) is installed as it is
        $apAll = @(Read-Lines $apFile)
        $fullInput = @($apAll | Where-Object { $_ -like '#AP-FULL-INPUT*' }).Count -gt 0
        if ($fullInput) {
            if ([System.IO.File]::Exists($inFile)) { Backup-Once $inFile }
            Write-Lines $inFile $apAll
            $report.Add('input.conf: the complete AnimePlayer input.conf was installed as it is (your keys + every extra binding; the original is kept as input.conf.ap-backup)')
        }
        elseif (-not [System.IO.File]::Exists($inFile)) {
            $out = @('# AnimePlayer input.conf (the build has none)')
            foreach ($s in 'CORE', 'EXTRA', 'STANDALONE') { foreach ($b in $ap[$s]) { $out += $b.Line } }
            Write-Lines $inFile $out
        }
        else {
            Backup-Once $inFile
            # undo an earlier run first, so running build.bat again never stacks or loses anything
            $buildLines = New-Object System.Collections.Generic.List[string]
            foreach ($l in (Read-Lines $inFile)) {
                if ($l.StartsWith($blockMark)) { break }                            # everything after this marker is ours
                if ($l.StartsWith($winMark)) { $l = $l.Substring($winMark.Length) }  # restore a line a previous run commented out
                if ($l.StartsWith($dupMark)) { $l = $l.Substring($dupMark.Length) }
                $buildLines.Add($l)
            }
            while ($buildLines.Count -gt 0 -and $buildLines[$buildLines.Count - 1].Trim() -eq '') { $buildLines.RemoveAt($buildLines.Count - 1) }

            $built = @{}; $firstSeen = @{}
            for ($i = 0; $i -lt $buildLines.Count; $i++) {
                $b = Parse-Binding $buildLines[$i]
                if (-not $b) { continue }
                if ($firstSeen.ContainsKey($b.Norm) -and $firstSeen[$b.Norm].Cmd -ne $b.Cmd) {
                    $prev = $firstSeen[$b.Norm]
                    if ($prev.Cmd.Trim() -eq 'ignore') {
                        # an earlier "ignore" for the same key is dead weight (mpv keeps the later line): switch it off so there is no duplicate
                        $buildLines[$prev.Index] = $dupMark + $prev.Line
                        $report.Add("Removed a pointless duplicate inside the anime build: '" + $prev.Key + " ignore' was overridden by the later '" + $b.Key + "' line anyway")
                    }
                    else {
                        $report.Add("DUPLICATE inside the anime build: '" + $prev.Key + "' and '" + $b.Key + "' are the same key; mpv keeps the later one: " + $b.Cmd)
                    }
                }
                if (-not $firstSeen.ContainsKey($b.Norm)) { $firstSeen[$b.Norm] = $b }
                $b | Add-Member -NotePropertyName Index -NotePropertyValue $i
                $built[$b.Norm] = $b
            }

            # keys the build's own SCRIPTS bind by default (an input.conf line for the same key would silently disable that script feature)
            $scriptKeys = @{}
            $sdir = [System.IO.Path]::Combine($Cfg, 'scripts')
            if ([System.IO.Directory]::Exists($sdir)) {
                foreach ($f in (Get-ChildItem -LiteralPath $sdir -Filter '*.lua' -File)) {
                    if ($f.Name -like 'ap-*' -or $f.Name -like '00-*' -or $f.Name -eq 'midclick-jump.lua' -or $f.Name -eq 'speed-osd.lua') { continue }
                    $src = [System.IO.File]::ReadAllText($f.FullName)
                    foreach ($m in [regex]::Matches($src, 'mp\.add_key_binding\(\s*"([^"]+)"\s*,\s*(?:"([^"]+)")?')) {
                        $nk = Norm-Key $m.Groups[1].Value
                        if (-not $scriptKeys.ContainsKey($nk)) {
                            $scriptKeys[$nk] = @{ Key = $m.Groups[1].Value; File = $f.Name; Script = [System.IO.Path]::GetFileNameWithoutExtension($f.Name); Name = $m.Groups[2].Value }
                        }
                    }
                }
            }

            $used = @{}
            foreach ($k in $built.Keys) { $used[$k] = $true }
            foreach ($k in $scriptKeys.Keys) { $used[$k] = $true }

            $moved = New-Object System.Collections.Generic.List[string]
            $apply = New-Object System.Collections.Generic.List[string]

            # keys that PotPlayer / AnimePlayer will bind themselves are reserved: a moved action must never land on one of them
            $reserved = @{}
            foreach ($r in @($ap.CORE) + @($ap.EXTRA)) { $reserved[$r.Norm] = $true }
            # a displaced action keeps working on Alt+<key> (else Ctrl+Alt+<key>); mouse / wheel keys are never moved
            function Pick-Free([string]$key) {
                if ($key -match '^(MBTN|MOUSE|WHEEL)') { return $null }
                foreach ($cand in @(('Alt+' + $key), ('Ctrl+Alt+' + $key))) {
                    $nc = Norm-Key $cand
                    if (-not $used.ContainsKey($nc) -and -not $reserved.ContainsKey($nc)) { return $cand }
                }
                return $null
            }
            # displaced actions that PotPlayer's key already does (seeking, volume steps) are not worth a second key
            function Is-Redundant([string]$cmd) { return ($cmd -match '^(no-osd\s+)?(seek|add volume|frame-step|frame-back-step|cycle fullscreen|cycle pause)\b') }

            foreach ($c in $ap.CORE) {
                if ($scriptKeys.ContainsKey($c.Norm)) {
                    $sk = $scriptKeys[$c.Norm]
                    if ($PotWins -eq 1) {
                        $apply.Add($c.Line)
                        $note = "PotPlayer key '" + $c.Key + "' (" + $c.Cmd + ") takes the key of the build script " + $sk.File
                        $nk = Pick-Free $sk.Key
                        if ($nk -and $sk.Name) {
                            $moved.Add($nk + ' script-binding ' + $sk.Script + '/' + $sk.Name); $used[(Norm-Key $nk)] = $true
                            $note += '  -> the script feature moved to ' + $nk
                        }
                        else { $note += '  -> the script feature is now only reachable from its menu (no free key / unnamed binding)' }
                        $report.Add($note)
                    }
                    else { $report.Add("kept the build script's key '" + $sk.Key + "' (" + $sk.File + "); PotPlayer's '" + $c.Key + "' (" + $c.Cmd + ') was NOT added') }
                    continue
                }
                if ($built.ContainsKey($c.Norm) -and $built[$c.Norm].Cmd -ne $c.Cmd) {
                    $old = $built[$c.Norm]
                    if ($PotWins -eq 1) {
                        $buildLines[$old.Index] = $winMark + $old.Line
                        $note = "PotPlayer key '" + $c.Key + "' replaces the build's " + $old.Cmd
                        if ($old.Cmd -eq 'seek 85') { $note += '  -> the 85 s opening skip is gone from Enter; the middle mouse button jumps 5 s (change it in AnimePlayer Settings, e.g. 85)' }
                        elseif ($c.Cmd -eq 'script-message ap-dblclick') { $note += '  (no fullscreen on double-click any more; plays the last video when the player is empty)' }
                        elseif ($old.Cmd -eq 'script-binding uosc/menu') { $note += '  (the uosc menu is still on the hover bar and the MENU key)' }
                        elseif (Is-Redundant $old.Cmd) { $note += '  (the same job is done by the PotPlayer key, nothing lost)' }
                        else {
                            $nk = Pick-Free $old.Key
                            if ($nk) { $moved.Add($nk + ' ' + $old.Cmd); $used[(Norm-Key $nk)] = $true; $note += '  -> moved to ' + $nk }
                            else { $note += '  -> disabled (no free key to move it to)' }
                        }
                        $report.Add($note)
                        $apply.Add($c.Line)
                    }
                    else { $report.Add("kept the build's '" + $old.Key + "' (" + $old.Cmd + "); PotPlayer's " + $c.Cmd + ' not added') }
                }
                elseif (-not $built.ContainsKey($c.Norm)) { $apply.Add($c.Line); $used[$c.Norm] = $true }
                # else: the build already binds this key to the very same command - nothing to add
            }
            foreach ($e in $ap.EXTRA) {
                if ($used.ContainsKey($e.Norm)) {
                    if ($built.ContainsKey($e.Norm) -and $built[$e.Norm].Cmd -ne $e.Cmd) {
                        $report.Add("skipped '" + $e.Key + "' (" + $e.Cmd + '): the anime build already uses it for ' + $built[$e.Norm].Cmd)
                    }
                    elseif ($scriptKeys.ContainsKey($e.Norm)) {
                        $report.Add("skipped '" + $e.Key + "' (" + $e.Cmd + '): the build script ' + $scriptKeys[$e.Norm].File + ' uses that key')
                    }
                    continue
                }
                $apply.Add($e.Line); $used[$e.Norm] = $true
            }
            $out = New-Object System.Collections.Generic.List[string]
            foreach ($l in $buildLines) { $out.Add($l) }
            $out.Add('')
            $out.Add($blockMark + ' (see key-conflicts.txt) =====')
            foreach ($l in $apply) { $out.Add($l) }
            if ($moved.Count -gt 0) {
                $out.Add('# build actions moved here because PotPlayer owns their old key')
                foreach ($l in $moved) { $out.Add($l) }
            }
            Write-Lines $inFile $out
        }
        [System.IO.File]::Delete($apFile)
    }
}
catch { Fail-Note 'input.conf' $_ }

# ================================================================ 3. thumbfast
try {
    $tf = [System.IO.Path]::Combine($Cfg, 'script-opts\thumbfast.conf')
    $tfWanted = [ordered]@{ 'spawn_first' = 'yes'; 'network' = 'no'; 'hwdec' = 'no' }
    if ($MpvExe -ne '') { $tfWanted['mpv_path'] = $MpvExe }
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($tf)) | Out-Null
    $tfLines = @()
    if ([System.IO.File]::Exists($tf)) { Backup-Once $tf; $tfLines = @(Read-Lines $tf) }
    foreach ($k in $tfWanted.Keys) {
        $found = $false
        for ($i = 0; $i -lt $tfLines.Count; $i++) {
            if ($tfLines[$i] -match ('^\s*' + [regex]::Escape($k) + '\s*=')) { $tfLines[$i] = $k + '=' + $tfWanted[$k]; $found = $true }
        }
        if (-not $found) { $tfLines += ($k + '=' + $tfWanted[$k]) }
    }
    Write-Lines $tf $tfLines
    if ($MpvExe -eq '') { $report.Add('WARNING: no standalone mpv.exe - seek-bar previews (thumbfast) will stay blank. Put mpv.exe into the mpv-exe folder and run build.bat again.') }
}
catch { Fail-Note 'thumbfast' $_ }

# ================================================================ report
$txt = @('AnimePlayer config merge report', '================================', '')
if ($report.Count -eq 0) { $txt += 'No conflicts.' } else { $txt += $report }
Write-Lines ([System.IO.Path]::Combine($Cfg, 'key-conflicts.txt')) $txt
Write-Host ('Config merged - ' + $report.Count + ' note(s) in key-conflicts.txt')
foreach ($r in $report) { if ($r.StartsWith('WARNING') -or $r.StartsWith('SKIPPED') -or $r.StartsWith('ERROR')) { Write-Host ('[!] ' + $r) } }
exit 0
