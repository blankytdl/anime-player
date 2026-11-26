-- AnimePlayer: playlist tools (build 36).
--
-- The Queue is gone. This script (the file keeps its old name so nothing that points at it breaks) now only looks after the PLAYLIST:
--   * "Play later" (drag & drop onto the player, Explorer "Add all to AnimePlayer playlist", the Add videos buttons) = put videos at the END of the playlist
--   * the playlist only holds video, gif and audio files, every file once (the playing entry is never touched, files on disk are never touched)
--   * lengths of the videos (Total / Left in the footer, the time at the end of every row, "sort by length"), read with ffprobe WITHOUT disturbing playback:
--     automatic reading happens only while nothing plays (paused / no video), one file at a time; "Read all durations" is the manual way
--   * sort / shuffle / remove watched / remove all (keeps the playing one), show in Explorer
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

local opts = {
    video_only = true,       -- only video, gif and audio formats go into the playlist (no pictures, subtitles, other files)
    dedupe = true,           -- the same file is listed once
    allow_urls = true,       -- links (http, rtmp ...) cannot be judged by an extension: kept (no = only local files)
    probe_while_playing = false,   -- yes = also read durations in the background while a video plays (can cause stutter)
}
require('mp.options').read_options(opts, 'ap-queue')

local cfg = mp.command_native({ 'expand-path', '~~/' })

local function is_url(p) return p:find('://', 1, true) ~= nil end

-- video formats + gif + audio formats. No pictures, subtitles or other files.
local VIDEO_EXT = {}
for e in ('mkv mp4 avi webm mov m4v ts m2ts mts flv f4v wmv asf mpg mpeg mpe vob ogv 3gp 3g2 divx rm rmvb mxf gif'):gmatch('%S+') do VIDEO_EXT[e] = true end
for e in ('mp3 flac m4a mka mp2 ogg opus aac ac3 dts wav wma ape alac wv'):gmatch('%S+') do VIDEO_EXT[e] = true end
local function is_video_path(p)
    if type(p) ~= 'string' or p == '' then return false end
    if not opts.video_only then return true end
    if is_url(p) then return opts.allow_urls end
    local ext = p:match('%.([^./\\]+)$')
    return ext ~= nil and VIDEO_EXT[ext:lower()] == true
end
-- one key per file: case, slashes and a space / quote at the ends never make two spellings of the same file
local function path_key(p) return ((p:lower():gsub('\\', '/')):gsub('^%s+', ''):gsub('%s+$', '')) end
local function base_name(p) local _, n = utils.split_path(p); return (n and n ~= '') and n or p end
local function natural_key(s)
    return (s:lower():gsub('%d+', function(d) return string.rep('0', 12 - math.min(#d, 12)) .. d end))
end
local function has_uosc()
    return utils.file_info(utils.join_path(cfg, 'scripts/uosc')) ~= nil or utils.file_info(utils.join_path(cfg, 'scripts/uosc.lua')) ~= nil
end
local function get_playlist() return mp.get_property_native('playlist') or {} end

-- the Queue no longer exists: tell the rest of the player (uosc patches, scripts) so nothing waits for it
mp.set_property('user-data/ap/queue-playing', 'no')
mp.set_property_native('user-data/ap/hidden', {})

-- ---------- watched (read-only here: ap-watched.lua publishes it) ----------
local update_footers
local watched = {}
local function wnorm(p) return (p:lower():gsub('\\', '/')) end
local function is_watched(p) return type(p) == 'string' and watched[wnorm(p)] == true end
mp.observe_property('user-data/ap/watched', 'native', function(_, v)
    watched = type(v) == 'table' and v or {}
    if update_footers then update_footers() end
end)

-- ---------- durations: read once with ffprobe (or the preview mpv.exe), cached in durations.json ----------
local DUR_FILE = utils.join_path(cfg, 'durations.json')
local durs = {}
-- one key per file: 'C:/a/b.mkv' (autoload) and 'C:\\a\\b.mkv' (Explorer) are the same file
local function dk(p) return (p:gsub('\\', '/')) end
do
    local f = io.open(DUR_FILE, 'rb')
    if f then
        local t = utils.parse_json(f:read('*a') or '')
        f:close()
        if type(t) == 'table' then for k, v in pairs(t) do durs[dk(k)] = v end end
    end
end
-- the cache is written at most every 10 s while durations are being read (it used to be rewritten after every file) and when the player closes
local save_timer, durs_dirty = nil, false
local function write_durs()
    durs_dirty = false
    local f = io.open(DUR_FILE, 'wb')
    if f then f:write(utils.format_json(durs) or '{}'); f:close() end
end
local function save_durs()
    durs_dirty = true
    if save_timer then return end
    save_timer = mp.add_timeout(10, function() save_timer = nil; if durs_dirty then write_durs() end end)
end
mp.register_event('shutdown', function() if durs_dirty then write_durs() end end)
local function dur_of(p) local e = durs[dk(p)]; return e and e.d or nil end
local function fmt_time(sec)
    sec = math.floor(sec + 0.5)
    local h, m, s = math.floor(sec / 3600), math.floor(sec % 3600 / 60), sec % 60
    if h > 0 then return string.format('%d:%02d:%02d', h, m, s) end
    return string.format('%d:%02d', m, s)
end

-- "Total 5:12:30 | Left 4:40:00 | 13 files"
local function summarize(paths)
    local total, left, unknown, n, nwatched = 0, 0, 0, 0, 0
    for _, p in ipairs(paths) do
        n = n + 1
        local d = dur_of(p)
        local w = is_watched(p)
        if w then nwatched = nwatched + 1 end
        if d then
            total = total + d
            if not w then left = left + d end
        else
            unknown = unknown + 1
        end
    end
    if n == 0 then return '' end
    local parts = { 'Total ' .. fmt_time(total) .. (unknown > 0 and '+' or '') }
    if nwatched > 0 then parts[#parts + 1] = 'Left ' .. fmt_time(left) end
    parts[#parts + 1] = n .. (n == 1 and ' file' or ' files')
    if unknown > 0 then parts[#parts + 1] = unknown .. ' not read yet' end
    return table.concat(parts, '  |  ')
end

local probe_tool
local function find_probe()
    if probe_tool ~= nil then return probe_tool end
    local function is_file(p) local fi = utils.file_info(p); return fi and fi.is_file end
    local exe_dir = mp.command_native({ 'expand-path', '~~exe_dir/' }) or ''
    for _, c in ipairs({ exe_dir .. 'ffprobe.exe', exe_dir .. 'ffmpeg/ffprobe.exe', cfg .. 'ffprobe.exe' }) do
        if is_file(c) then probe_tool = { kind = 'ffprobe', path = c }; return probe_tool end
    end
    for _, c in ipairs({ exe_dir .. 'mpv-thumb/mpv.exe', exe_dir .. 'mpv.exe' }) do
        if is_file(c) then probe_tool = { kind = 'mpv', path = c }; return probe_tool end
    end
    probe_tool = { kind = 'ffprobe', path = 'ffprobe' }   -- last try: PATH
    return probe_tool
end

local function run_probe(path, cb)
    local tool = find_probe()
    local args
    if tool.kind == 'ffprobe' then
        args = { tool.path, '-v', 'error', '-show_entries', 'format=duration', '-of', 'default=noprint_wrappers=1:nokey=1', path }
    else
        args = { tool.path, '--no-config', '--vo=null', '--ao=null', '--no-audio', '--frames=1', '--msg-level=all=no',
                 '--term-playing-msg=DUR=${=duration}', path }
    end
    mp.command_native_async({ name = 'subprocess', playback_only = false, capture_stdout = true, args = args }, function(ok, res)
        local sec
        if ok and res and res.stdout then
            sec = tonumber(res.stdout:match('DUR=([%d%.]+)') or res.stdout:match('^%s*([%d%.]+)'))
        end
        cb(sec)
    end)
end

-- Nothing competes with the reading when no video plays or the video is paused. While a video plays it is only read when you ask for it
-- ("Read all durations", "sort by length") and then one file at a time.
local function quiet()
    return mp.get_property('path') == nil or mp.get_property_bool('idle-active', false) or mp.get_property_bool('pause', false)
        or mp.get_property_bool('eof-reached', false)
end

local pending, queued, running, failed, waiters = {}, {}, {}, {}, {}
local active, manual, manual_total, manual_done = 0, false, 0, 0
local changed_timer, pump_timer

local function footers_soon()
    if changed_timer then return end
    changed_timer = mp.add_timeout(1.5, function()
        changed_timer = nil
        if update_footers then update_footers() end
    end)
end

local pump
local function pump_later(delay)
    if pump_timer then return end
    pump_timer = mp.add_timeout(delay, function() pump_timer = nil; pump() end)
end

pump = function()
    if #pending == 0 then return end
    local limit
    if manual then limit = quiet() and 3 or 1
    elseif opts.probe_while_playing or quiet() then limit = 1
    else return end   -- automatic reading waits until nothing plays (the pause / idle observers below wake it up)
    while active < limit and #pending > 0 do
        local p = table.remove(pending, 1)
        queued[p] = nil
        running[p] = true
        active = active + 1
        run_probe(p, function(sec)
            active = active - 1
            running[p] = nil
            if sec then
                local fi = utils.file_info(p)
                durs[dk(p)] = { d = sec, s = fi and fi.size or 0, m = fi and fi.mtime or 0 }
                save_durs()
            else
                failed[p] = true
            end
            if manual then
                manual_done = manual_done + 1
                mp.osd_message('Reading durations ' .. manual_done .. '/' .. manual_total, 1.5)
            end
            footers_soon()
            if active == 0 and #pending == 0 then
                if manual then manual = false; mp.osd_message('Durations read', 2) end
                local w = waiters
                waiters = {}
                for _, cb in ipairs(w) do cb() end
                if update_footers then update_footers() end
            else
                pump_later(manual and 0.05 or 0.4)   -- a short pause between two files: the disk and the CPU are never kept busy
            end
        end)
    end
end

-- queue the files that have no cached duration; returns how many were queued
local function scan(paths, is_manual)
    local n = 0
    for _, p in ipairs(paths) do
        if type(p) == 'string' and p ~= '' and not is_url(p) and not durs[dk(p)] and not failed[p] and not running[p] then
            if not queued[p] then
                queued[p] = true
                pending[#pending + 1] = p
                n = n + 1
            elseif is_manual then
                n = n + 1   -- already waiting for the automatic reading: "Read all durations" takes it over (and counts it)
            end
        end
    end
    if n > 0 and is_manual then manual = true; manual_total = manual_total + n; if active == 0 then manual_done = 0; manual_total = n end end
    pump()
    return n
end

-- run cb when every duration of `paths` is known (used by "sort by length")
local function with_durations(paths, cb)
    local n = scan(paths, true)
    if n == 0 and active == 0 and #pending == 0 then cb() else waiters[#waiters + 1] = cb end
end

local function playlist_paths()
    local out = {}
    for _, e in ipairs(get_playlist()) do out[#out + 1] = e.filename end
    return out
end

function update_footers()
    local paths = playlist_paths()
    mp.set_property('user-data/ap/playlist-footer', summarize(paths))
    -- lengths of the listed files (normalised path -> seconds): shown at the end of every row of the Playlist tab
    local map = {}
    for _, p in ipairs(paths) do
        local d = dur_of(p)
        if d then map[wnorm(p)] = d end
    end
    mp.set_property_native('user-data/ap/durations', map)
end

-- automatic: the files of the playlist that have no duration yet. The list is rebuilt every time, so files that left the playlist are never read.
local function auto_scan_now()
    if manual then return end
    for i = #pending, 1, -1 do queued[pending[i]] = nil; pending[i] = nil end
    scan(playlist_paths(), false)
end
mp.register_script_message('ap-durations-scan', function()
    local n = scan(playlist_paths(), true)
    if n == 0 and active == 0 then mp.osd_message('All durations are already known', 2) end
end)
-- wake the automatic reading up when playback stops / pauses
mp.observe_property('pause', 'bool', function(_, v) if v then pump_later(1.0) end end)
mp.observe_property('idle-active', 'bool', function(_, v) if v then pump_later(1.0) end end)
mp.register_event('file-loaded', function()
    local p, d = mp.get_property('path'), mp.get_property_number('duration')
    if p and d and not is_url(p) and not durs[dk(p)] then
        -- the length of the video that plays is known for free: no file is opened for it
        durs[dk(p)] = { d = d, s = 0, m = 0 }
        save_durs(); footers_soon()
    end
end)

-- ---------- show the file in Windows Explorer (selected) ----------
local function show_in_explorer(path)
    path = (path and path ~= '') and path or mp.get_property('path')
    if not path or path == '' or is_url(path) then mp.osd_message('Not a local file', 2) return end
    if not path:match('^%a:[\\/]') and not path:match('^[\\/][\\/]') then
        path = utils.join_path(mp.get_property('working-directory') or '', path)
    end
    path = path:gsub('/', '\\')
    mp.command_native_async({ name = 'subprocess', playback_only = false, args = { 'explorer.exe', '/select,' .. path } }, function() end)
end
mp.register_script_message('ap-show-in-explorer', show_in_explorer)

-- ---------- the playlist: video / gif / audio only, every file once ----------
-- autoload / "open" / "add to playlist" may put pictures, subtitles or a second copy of the same file into mpv's playlist. They are removed again
-- a moment after the playlist changed. The playing entry is never touched, files on disk are never touched.
local function clean_playlist_now()
    if not (opts.dedupe or opts.video_only) then return end
    local pl = get_playlist()
    if #pl < 2 then return end
    local function is_now(e) return e.current or e.playing end
    local seen, drop, dups, bad = {}, {}, 0, 0
    for _, e in ipairs(pl) do
        if is_now(e) and type(e.filename) == 'string' then seen[path_key(e.filename)] = true end
    end
    for i, e in ipairs(pl) do
        if not is_now(e) and type(e.filename) == 'string' then
            local k = path_key(e.filename)
            if not is_video_path(e.filename) then drop[#drop + 1] = i - 1; bad = bad + 1
            elseif opts.dedupe and seen[k] then drop[#drop + 1] = i - 1; dups = dups + 1
            else seen[k] = true end
        end
    end
    for j = #drop, 1, -1 do mp.commandv('playlist-remove', tostring(drop[j])) end   -- highest index first: the other indexes stay valid
    if #drop > 0 then msg.info(string.format('playlist cleaned: %d duplicate, %d not a video', dups, bad)) end
end
mp.register_script_message('ap-playlist-clean', clean_playlist_now)

-- ONE debounced reaction to a changed playlist (the playlist itself is only read when the timer fires: reading a long playlist on every
-- change used to cost time while a folder was being loaded)
local changed
local function playlist_changed()
    if changed then changed:kill() end
    changed = mp.add_timeout(0.5, function()
        changed = nil
        clean_playlist_now()
        update_footers()
        auto_scan_now()   -- while a video plays the missing durations only wait in the list: they are read when playback pauses or stops
    end)
end
mp.observe_property('playlist-count', 'number', playlist_changed)
mp.observe_property('path', 'string', playlist_changed)

-- ---------- Play later / Add videos: put videos at the END of the playlist ----------
local function expand(list)
    local out = {}
    for _, p in ipairs(list) do
        local fi = (type(p) == 'string' and not is_url(p)) and utils.file_info(p) or nil
        if fi and fi.is_dir then
            local files = utils.readdir(p, 'files') or {}
            local found = {}
            for _, f in ipairs(files) do
                if is_video_path(f) then found[#found + 1] = utils.join_path(p, f) end
            end
            table.sort(found, function(a, b) return natural_key(a) < natural_key(b) end)
            for _, f in ipairs(found) do out[#out + 1] = f end
        else
            out[#out + 1] = p
        end
    end
    return out
end

local function add_to_playlist(list)
    list = expand(list)
    local pl = get_playlist()
    local count_before = #pl
    local seen = {}
    for _, e in ipairs(pl) do if type(e.filename) == 'string' then seen[path_key(e.filename)] = true end end
    local idle = mp.get_property('path') == nil or mp.get_property_bool('idle-active', false)
    local added, dups, skipped = 0, 0, 0
    for _, p in ipairs(list) do
        if type(p) == 'string' then
            p = (p:gsub('^%s+', ''):gsub('%s+$', ''))
            if p ~= '' then
                local k = path_key(p)
                if not is_video_path(p) then skipped = skipped + 1
                elseif opts.dedupe and seen[k] then dups = dups + 1
                else
                    seen[k] = true
                    -- nothing plays: the first one starts, the others follow it; something plays: it is never interrupted
                    mp.commandv('loadfile', p, (idle and added == 0) and 'append-play' or 'append')
                    added = added + 1
                end
            end
        end
    end
    -- the last video stopped on its final frame (keep-open): the first new one starts now
    if added > 0 and not idle and mp.get_property_bool('eof-reached', false) then
        mp.commandv('playlist-play-index', tostring(count_before))
    end
    local note = {}
    if dups > 0 then note[#note + 1] = dups .. ' already in the playlist' end
    if skipped > 0 then note[#note + 1] = skipped .. ' not a video' end
    if added > 0 then
        mp.osd_message('Added ' .. added .. ' to the playlist' .. (#note > 0 and (' (' .. table.concat(note, ', ') .. ' skipped)') or ''), 2)
    elseif #note > 0 then
        mp.osd_message('Nothing added: ' .. table.concat(note, ', '), 2)
    end
end

-- file pickers (Windows PowerShell, no extra files needed)
local EXT = "'.mkv','.mp4','.avi','.webm','.mov','.m4v','.ts','.m2ts','.mts','.flv','.f4v','.wmv','.asf','.mpg','.mpeg','.mpe','.vob','.ogv','.3gp','.3g2','.divx','.rm','.rmvb','.mxf','.gif','.mp3','.flac','.m4a','.mka','.mp2','.ogg','.opus','.aac','.ac3','.dts','.wav','.wma','.ape','.alac','.wv'"
local HEAD = "Add-Type -AssemblyName System.Windows.Forms;[Console]::OutputEncoding=[Text.Encoding]::UTF8;" ..
    "$o=New-Object Windows.Forms.Form;$o.TopMost=$true;$o.ShowInTaskbar=$false;$o.Opacity=0;$o.StartPosition='CenterScreen';$o.Show();"

local function run_ps(script, cb)
    mp.command_native_async({
        name = 'subprocess', playback_only = false, capture_stdout = true,
        args = { 'powershell', '-NoProfile', '-STA', '-WindowStyle', 'Hidden', '-Command', HEAD .. script },
    }, function(ok, res)
        if not ok or not res or res.status ~= 0 then cb({}) return end
        local out = (res.stdout or ''):gsub('^\239\187\191', '')
        local list = {}
        for line in out:gmatch('[^\r\n]+') do
            line = line:gsub('^%s*"(.-)"%s*$', '%1')
            if line ~= '' then list[#list + 1] = line end
        end
        cb(list)
    end)
end

local function add_files()
    run_ps("$d=New-Object Windows.Forms.OpenFileDialog;$d.Multiselect=$true;$d.Title='Add to the playlist';" ..
        "$d.Filter='Video / gif / audio|*.mkv;*.mp4;*.avi;*.webm;*.mov;*.m4v;*.ts;*.m2ts;*.mts;*.flv;*.f4v;*.wmv;*.asf;*.mpg;*.mpeg;*.mpe;*.vob;*.ogv;*.3gp;*.3g2;*.divx;*.rm;*.rmvb;*.mxf;*.gif;*.mp3;*.flac;*.m4a;*.mka;*.mp2;*.ogg;*.opus;*.aac;*.ac3;*.dts;*.wav;*.wma;*.ape;*.alac;*.wv|All files|*.*';" ..
        "if($d.ShowDialog($o) -eq 'OK'){$d.FileNames|ForEach-Object{$_}};$o.Close()",
        function(list) if #list > 0 then add_to_playlist(list) end end)
end
local function add_folder()
    run_ps("$d=New-Object Windows.Forms.FolderBrowserDialog;$d.Description='Add every video in this folder to the playlist';" ..
        "if($d.ShowDialog($o) -eq 'OK'){$d.SelectedPath};$o.Close()",
        function(list) if #list > 0 then add_to_playlist(list) end end)
end
local function add_clipboard()
    run_ps("$c=Get-Clipboard -Format FileDropList;if($c){$c|ForEach-Object{$_.FullName}}else{Get-Clipboard};$o.Close()",
        function(list) if #list > 0 then add_to_playlist(list) else mp.osd_message('Nothing to add on the clipboard', 2) end end)
end
mp.register_script_message('ap-playlist-add-files', add_files)
mp.register_script_message('ap-playlist-add-folder', add_folder)
mp.register_script_message('ap-playlist-add-clipboard', add_clipboard)
mp.register_script_message('ap-playlist-add', function(...) add_to_playlist({ ... }) end)

local function add_menu()
    if not has_uosc() then return end
    mp.commandv('script-message-to', 'uosc', 'open-menu', utils.format_json({
        type = 'ap_playlist_add',
        title = 'Add videos to the playlist',
        items = {
            { title = 'Add files...', icon = 'video_file', value = { 'script-message', 'ap-playlist-add-files' } },
            { title = 'Add folder...', icon = 'folder_open', value = { 'script-message', 'ap-playlist-add-folder' } },
            { title = 'Add from the clipboard', icon = 'content_paste', value = { 'script-message', 'ap-playlist-add-clipboard' } },
            { title = 'Back to the playlist', icon = 'arrow_back', value = { 'script-message', 'ap-playlist-direct' } },
        },
    }))
end
mp.register_script_message('ap-playlist-add-menu', add_menu)

-- Play later (drag & drop / Explorer): the player writes the paths to an inbox file and sends this message
local function read_inbox()
    local list = {}
    for _, name in ipairs({ 'playlist-inbox.txt', 'queue-inbox.txt' }) do   -- the second name is the one older player builds write
        local path = utils.join_path(cfg, name)
        local f = io.open(path, 'rb')
        if f then
            local s = f:read('*a') or ''
            f:close()
            os.remove(path)
            for line in s:gmatch('[^\r\n]+') do list[#list + 1] = line end
        end
    end
    if #list > 0 then add_to_playlist(list) end
end
mp.register_script_message('ap-playlist-inbox', read_inbox)
mp.register_script_message('ap-queue-inbox', read_inbox)

-- the Queue was removed: anything that still sends one of its messages (an old button, an old key) gets one clear line instead of silence
local function queue_gone() mp.osd_message('The Queue was removed. Play later / Add videos put videos into the Playlist.', 3) end
for _, name in ipairs({ 'ap-queue-open', 'ap-queue-menu', 'ap-queue-add', 'ap-queue-add-menu', 'ap-queue-add-files', 'ap-queue-add-folder',
                        'ap-queue-add-clipboard', 'ap-queue-add-many', 'ap-queue-add-current', 'ap-queue-sort', 'ap-queue-shuffle', 'ap-queue-clear',
                        'ap-queue-clear-watched', 'ap-queue-event' }) do
    mp.register_script_message(name, queue_gone)
end
mp.register_script_message('ap-queue-attach', function() end)    -- the resume script of older builds still sends this
mp.register_script_message('ap-queue-remove-played', function() end)
mp.register_script_message('ap-queue-restore', function() end)

-- open the Playlist menu on purpose (the Playlist tools / sort menu go back to it)
mp.register_script_message('ap-playlist-direct', function()
    mp.set_property('user-data/ap/playlist-direct', 'yes')
    mp.commandv('script-binding', 'uosc/playlist')
    mp.add_timeout(0.4, function() mp.set_property('user-data/ap/playlist-direct', 'no') end)
end)

-- ---------- sorting the PLAYLIST ----------
local function sort_playlist_now(mode)
    local pl = get_playlist()
    local items = {}
    for i, e in ipairs(pl) do
        local it = { id = e.id, idx = i, name = natural_key(base_name(e.filename)), size = 0, time = 0 }
        local len = dur_of(e.filename)
        it.len = len or -1
        it.len_short = len or math.huge
        if not is_url(e.filename) then
            local fi = utils.file_info(e.filename)
            if fi then it.size = fi.size or 0; it.time = fi.mtime or 0 end
        end
        items[i] = it
    end
    local cmp
    if mode == 'name' then cmp = function(a, b) return a.name < b.name end
    elseif mode == 'name_desc' then cmp = function(a, b) return a.name > b.name end
    elseif mode == 'date' then cmp = function(a, b) return a.time > b.time end
    elseif mode == 'date_old' then cmp = function(a, b) return a.time < b.time end
    elseif mode == 'size' then cmp = function(a, b) return a.size > b.size end
    elseif mode == 'size_small' then cmp = function(a, b) return a.size < b.size end
    elseif mode == 'length' then cmp = function(a, b) return a.len > b.len end
    elseif mode == 'length_short' then cmp = function(a, b) return a.len_short < b.len_short end
    elseif mode == 'reverse' then cmp = function(a, b) return a.idx > b.idx end
    end
    if cmp then
        table.sort(items, function(a, b)
            if cmp(a, b) then return true end
            if cmp(b, a) then return false end
            return a.idx < b.idx
        end)
        local current_order = {}
        for i, e in ipairs(pl) do current_order[i] = e.id end
        for target = 1, #items do
            local want = items[target].id
            local from
            for i, id in ipairs(current_order) do if id == want then from = i; break end end
            if from and from ~= target then
                mp.commandv('playlist-move', tostring(from - 1), tostring(target - 1))
                table.remove(current_order, from)
                table.insert(current_order, target, want)
            end
        end
    end
end

local function sort_playlist(mode)
    if mode == 'length' or mode == 'length_short' then
        mp.osd_message('Reading durations...', 2)
        with_durations(playlist_paths(), function() sort_playlist_now(mode) end)
    else
        sort_playlist_now(mode)
    end
end

local function playlist_sort_menu()
    if not has_uosc() then return end
    local function item(title, icon, mode)
        return { title = title, icon = icon, value = { 'script-message', 'ap-playlist-sort', mode } }
    end
    mp.commandv('script-message-to', 'uosc', 'open-menu', utils.format_json({
        type = 'ap_playlist_sort',
        title = 'Sort playlist',
        items = {
            item('Name (A-Z)', 'sort_by_alpha', 'name'),
            item('Name (Z-A)', 'sort_by_alpha', 'name_desc'),
            item('Newest first', 'schedule', 'date'),
            item('Oldest first', 'history', 'date_old'),
            item('Largest first', 'storage', 'size'),
            item('Smallest first', 'storage', 'size_small'),
            item('Longest first', 'timer', 'length'),
            item('Shortest first', 'timer', 'length_short'),
            { title = 'Read all durations', icon = 'hourglass_top', value = { 'script-message', 'ap-durations-scan' } },
            item('Reverse order', 'swap_vert', 'reverse'),
            { title = 'Back to the playlist', icon = 'arrow_back', value = { 'script-message', 'ap-playlist-direct' } },
        },
    }))
end
mp.register_script_message('ap-playlist-sort-menu', playlist_sort_menu)
mp.register_script_message('ap-playlist-sort', function(mode)
    sort_playlist(mode)
    mp.add_timeout(0.15, function() mp.commandv('script-message', 'ap-playlist-direct') end)
end)

-- ---------- remove watched / remove all / shuffle (the files on disk are not touched) ----------
local function clear_watched_playlist()
    local pl = get_playlist()
    local n = 0
    for i = #pl, 1, -1 do
        local e = pl[i]
        if not e.current and is_watched(e.filename) then
            mp.commandv('playlist-remove', tostring(i - 1))
            n = n + 1
        end
    end
    mp.osd_message(n > 0 and ('Removed ' .. n .. ' watched from the playlist') or 'No watched files in the playlist', 2)
end
mp.register_script_message('ap-playlist-clear-watched', clear_watched_playlist)

-- remove every video of the playlist except the playing one
mp.register_script_message('ap-playlist-clear', function()
    mp.commandv('playlist-clear')
    mp.set_property_native('user-data/ap/no-autoload', true)   -- autoload.lua would put the whole folder back when only one entry is left
    mp.osd_message('Playlist cleared (the playing video stays)', 2)
end)
mp.register_event('end-file', function() mp.set_property_native('user-data/ap/no-autoload', false) end)

mp.register_script_message('ap-playlist-shuffle', function()
    mp.commandv('playlist-shuffle')
    mp.osd_message('Playlist shuffled', 2)
end)
