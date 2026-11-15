-- AnimePlayer: queue (round 7).
--
--   Playlist tab = mpv's playlist: the episodes the player recognised by name (autoload) - uosc shows / reorders that.
--   Queue tab    = YOUR list: videos you add on purpose. They play one after another, right after the current video,
--                  and then the playlist carries on. Saved in queue-list.json, so it survives a restart.
--
-- How it plays: the first queue item is placed in the playlist right after the current video ("staged"), so mpv itself
-- plays it next and the episode playlist is never destroyed. When it starts playing, the next queue item is staged.
-- Everything you do in the Queue tab first takes the staged item back, edits the list, then stages again.
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

-- queue_mode (script-opts/ap-queue.conf):
--   tabs  = each tab has its OWN "next file": a video started from the Playlist tab goes on with the playlist (Next key / button and the end of
--           the video alike); a video started from the Queue tab goes on with the next queue video, and when the queue is empty the playlist
--           carries on where it was.
--   after = the old behaviour: the queue always plays right after the current video, whichever tab it was started from.
local opts = { queue_mode = 'tabs' }
require('mp.options').read_options(opts, 'ap-queue')

local cfg = mp.command_native({ 'expand-path', '~~/' })
local FILE = utils.join_path(cfg, 'queue-list.json')
local PART = FILE .. '.part'
local SELF = mp.get_script_name()
local MENU_TYPE = 'ap_queue'

local queue = {}      -- pending items (not in the playlist yet)
local staged = nil    -- queue item currently placed after the playing one
local pending_idle_item = nil   -- queue video that was started on an idle player (it becomes playing_item when it loads)
local playing_item = nil   -- queue item that is playing now: stays listed (as the active row) until it has ended
local last_activate = -100    -- double-click guard
local last_move_t = -100        -- time of the last reorder: a drop is never an "open" click
local sel_index = nil         -- menu row that stays selected after a drag / move (the moved video)
local click_row, click_time = nil, -100   -- first click of a double click (a row is opened by a DOUBLE click)
local staged_id, staged_temp, staged_prev_id = nil, false, nil   -- playlist entry id of the staged item / is it an extra copy (hidden in the Playlist tab) / entry it sat behind before
local consumed_temp_id = nil   -- extra copy that is playing now: removed from the playlist when the next file starts

local function is_url(p) return p:find('://', 1, true) ~= nil end
local function base_name(p) local _, n = utils.split_path(p); return (n and n ~= '') and n or p end
local function natural_key(s)
    return (s:lower():gsub('%d+', function(d) return string.rep('0', 12 - math.min(#d, 12)) .. d end))
end
local function has_uosc()
    return utils.file_info(utils.join_path(cfg, 'scripts/uosc')) ~= nil or utils.file_info(utils.join_path(cfg, 'scripts/uosc.lua')) ~= nil
end

-- ---------- storage ----------
local function full()
    local out = {}
    if playing_item then out[#out + 1] = playing_item end
    if staged then out[#out + 1] = staged end
    for _, p in ipairs(queue) do out[#out + 1] = p end
    return out
end

local auto_scan_hook
local function save()
    if auto_scan_hook then auto_scan_hook() end
    local json = utils.format_json(full())
    if not json then return end
    local f = io.open(PART, 'wb')
    if not f then return end
    f:write(json); f:close()
    os.remove(FILE)
    if not os.rename(PART, FILE) then msg.warn('could not write ' .. FILE) end
end

local function load_saved()
    for _, p in ipairs({ FILE, PART }) do
        local f = io.open(p, 'rb')
        if f then
            local s = f:read('*a'); f:close()
            local t = s and s ~= '' and utils.parse_json(s) or nil
            if type(t) == 'table' then
                local out = {}
                for _, v in ipairs(t) do
                    if type(v) == 'string' and (is_url(v) or utils.file_info(v)) then out[#out + 1] = v end
                end
                return out
            end
        end
    end
    return {}
end

-- ---------- menu ----------
local refresh_queue_menu
local update_footers, auto_scan
local watched = {}
local function wnorm(p) return (p:lower():gsub('\\', '/')) end
local function is_watched(p) return type(p) == 'string' and watched[wnorm(p)] == true end
mp.observe_property('user-data/ap/watched', 'native', function(_, v)
    watched = type(v) == 'table' and v or {}
    if refresh_queue_menu then refresh_queue_menu() end
    if update_footers then update_footers() end
end)

-- ---------- durations: read once with ffprobe (or the preview mpv.exe), cached in durations.json ----------
local DUR_FILE = utils.join_path(cfg, 'durations.json')
local durs = {}
-- one key per file: 'C:/a/b.mkv' (autoload) and 'C:\\a\\b.mkv' (Explorer) used to be two entries, so a read duration was not found for the other spelling
local function dk(p) return (p:gsub('\\', '/')) end
do
    local f = io.open(DUR_FILE, 'rb')
    if f then
        local t = utils.parse_json(f:read('*a') or '')
        f:close()
        if type(t) == 'table' then for k, v in pairs(t) do durs[dk(k)] = v end end
    end
end
local save_timer
local function save_durs()
    if save_timer then save_timer:kill() end
    save_timer = mp.add_timeout(2, function()
        local f = io.open(DUR_FILE, 'wb')
        if f then f:write(utils.format_json(durs) or '{}'); f:close() end
    end)
end
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

local pending, inflight, failed, waiters = {}, {}, {}, {}
local active, manual, manual_total, manual_done = 0, false, 0, 0
local changed_timer
local function durations_changed()
    if changed_timer then return end
    changed_timer = mp.add_timeout(0.4, function()
        changed_timer = nil
        if update_footers then update_footers() end
        if refresh_queue_menu then refresh_queue_menu() end
    end)
end

local function pump()
    while active < 3 and #pending > 0 do
        local p = table.remove(pending, 1)
        active = active + 1
        run_probe(p, function(sec)
            active = active - 1
            inflight[p] = nil
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
            durations_changed()
            if active == 0 and #pending == 0 then
                if manual then manual = false; mp.osd_message('Durations read', 2) end
                local w = waiters
                waiters = {}
                for _, cb in ipairs(w) do cb() end
            end
            pump()
        end)
    end
end

-- queue the files that have no cached duration; returns how many were queued
local function scan(paths, is_manual)
    local n = 0
    for _, p in ipairs(paths) do
        if type(p) == 'string' and p ~= '' and not is_url(p) and not durs[dk(p)] and not failed[p] and not inflight[p] then
            inflight[p] = true
            pending[#pending + 1] = p
            n = n + 1
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
    for _, e in ipairs(mp.get_property_native('playlist') or {}) do out[#out + 1] = e.filename end
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

local scan_timer
function auto_scan()
    if scan_timer then scan_timer:kill() end
    scan_timer = mp.add_timeout(1.2, function()
        local all = playlist_paths()
        for _, p in ipairs(full()) do all[#all + 1] = p end
        scan(all, false)
        update_footers()
    end)
end
auto_scan_hook = auto_scan
mp.observe_property('playlist', 'native', function() update_footers(); auto_scan() end)
mp.register_script_message('ap-durations-scan', function()
    local all = playlist_paths()
    for _, p in ipairs(full()) do all[#all + 1] = p end
    local n = scan(all, true)
    if n == 0 and active == 0 then mp.osd_message('All durations are already known', 2) end
end)
mp.register_event('file-loaded', function()
    local p, d = mp.get_property('path'), mp.get_property_number('duration')
    if p and d and not is_url(p) and not durs[dk(p)] then
        local fi = utils.file_info(p)
        durs[dk(p)] = { d = d, s = fi and fi.size or 0, m = fi and fi.mtime or 0 }
        save_durs(); durations_changed()
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

local function menu_data()
    local items = {}
    local waiting_n = 0   -- numbers count the WAITING videos in play order (1 = plays first); the playing row has none
    for i, p in ipairs(full()) do
        local done = is_watched(p)   -- finished before: greyed out with a check mark
        local playing_row = playing_item ~= nil and i == 1
        local base_hint
        if playing_row then base_hint = 'playing'
        else
            waiting_n = waiting_n + 1
            local label = tostring(waiting_n)
            if waiting_n == 1 and playing_item then label = label .. '  up next' end
            base_hint = done and ('\226\156\147 ' .. label) or label
        end
        local d = dur_of(p)   -- length of the video at the end of the row
        items[i] = {
            title = is_url(p) and p or base_name(p),
            hint = d and (fmt_time(d) .. '   ' .. base_hint) or base_hint,
            muted = (done and not playing_row) or nil,
            active = playing_row or nil,
            value = i,
        }
    end
    return {
        type = MENU_TYPE,
        title = 'Queue (Type to Search)',
        tabs = { { title = 'Playlist', value = 'script-binding uosc/playlist' }, { title = 'Queue', active = true } },
        tools = {
            { icon = 'done_all', title = 'Remove watched', value = 'script-message ap-queue-clear-watched' },
            { icon = 'add', title = 'Add / sort...', value = 'script-message ap-queue-add-menu' },
            { icon = 'shuffle', title = 'Shuffle queue', value = 'script-message ap-queue-shuffle' },
            { icon = 'delete_sweep', title = 'Remove all videos from the queue', value = 'script-message ap-queue-clear' },
        },
        footer = summarize(full()),
        footnote = 'Double-click a row to play it. Hold a row and drag to reorder. Drop files on the player (top strip = Play later) to add.',
        callback = { SELF, 'ap-queue-event' },
        on_move = 'callback',
        -- narrow window: only the two buttons that matter (dragging / ctrl+up/down reorder), so the buttons never cover the title
        item_actions = (mp.get_property_number('osd-width', 1280) or 1280) < 620 and {
            { name = 'show', icon = 'folder_special', label = 'Show in Explorer' },
            { name = 'remove', icon = 'delete', label = 'Remove from queue' },
        } or {
            { name = 'up', icon = 'arrow_upward', label = 'Move up' },
            { name = 'down', icon = 'arrow_downward', label = 'Move down' },
            { name = 'show', icon = 'folder_special', label = 'Show in Explorer' },
            { name = 'remove', icon = 'delete', label = 'Remove from queue' },
        },
        bind_keys = { 'del' },
        keep_open = true,   -- the first click only selects the row; the menu closes when the video is opened
        selected_index = sel_index,
        items = items,
    }
end

local function refresh()
    if has_uosc() then
        mp.commandv('script-message-to', 'uosc', 'update-menu', utils.format_json(menu_data()))
        -- uosc keeps the OLD selection when a menu is updated (selected_index in the data is ignored), so select the moved row explicitly
        if sel_index then mp.commandv('script-message-to', 'uosc', 'select-menu-item', MENU_TYPE, tostring(sel_index)) end
    end
end
refresh_queue_menu = refresh

local open_menu
local function add_menu()
    if not has_uosc() then return end
    mp.commandv('script-message-to', 'uosc', 'open-menu', utils.format_json({
        type = 'ap_queue_add',
        title = 'Add to queue / sort',
        items = {
            { title = 'Files...', icon = 'video_file', value = { 'script-message', 'ap-queue-add-files', 'menu' } },
            { title = 'Folder...', icon = 'folder_open', value = { 'script-message', 'ap-queue-add-folder', 'menu' } },
            { title = 'From clipboard', icon = 'content_paste', value = { 'script-message', 'ap-queue-add-clipboard', 'menu' } },
            { title = 'Sort by name (A-Z)', icon = 'sort_by_alpha', value = { 'script-message', 'ap-queue-sort', 'name', 'menu' } },
            { title = 'Sort by name (Z-A)', icon = 'sort_by_alpha', value = { 'script-message', 'ap-queue-sort', 'name_desc', 'menu' } },
            { title = 'Newest first', icon = 'schedule', value = { 'script-message', 'ap-queue-sort', 'date', 'menu' } },
            { title = 'Oldest first', icon = 'history', value = { 'script-message', 'ap-queue-sort', 'date_old', 'menu' } },
            { title = 'Largest first', icon = 'storage', value = { 'script-message', 'ap-queue-sort', 'size', 'menu' } },
            { title = 'Smallest first', icon = 'storage', value = { 'script-message', 'ap-queue-sort', 'size_small', 'menu' } },
            { title = 'Longest first', icon = 'timer', value = { 'script-message', 'ap-queue-sort', 'length', 'menu' } },
            { title = 'Shortest first', icon = 'timer', value = { 'script-message', 'ap-queue-sort', 'length_short', 'menu' } },
            { title = 'Read all durations', icon = 'hourglass_top', value = { 'script-message', 'ap-durations-scan' } },
            { title = 'Reverse order', icon = 'swap_vert', value = { 'script-message', 'ap-queue-sort', 'reverse', 'menu' } },
            { title = 'Remove watched', icon = 'done_all', value = { 'script-message', 'ap-queue-clear-watched', 'menu' } },
            { title = 'Back to the queue', icon = 'arrow_back', value = { 'script-message', 'ap-queue-open' } },
        },
    }))
end

function open_menu()
    sel_index = nil
    if has_uosc() then
        mp.commandv('script-message-to', 'uosc', 'open-menu', utils.format_json(menu_data()))
    else
        mp.osd_message('Queue: ' .. #full() .. ' item(s). The Queue menu needs the uosc interface (mpv-anime-build).', 4)
    end
end

-- ---------- staging ----------
-- The first queue item is placed right after the playing video so mpv itself plays it next (end of file, Next button) and the
-- episode playlist is never destroyed. It is an extra copy that the Playlist tab hides; once it has played the copy is removed again.
-- If the same file is already in the playlist, that entry is moved instead of copying it (no duplicate plays) and moved back if unstaged.
local function playlist_count() return mp.get_property_number('playlist-count', 0) end
local function playlist_pos() return mp.get_property_number('playlist-pos', -1) end
local function get_playlist() return mp.get_property_native('playlist') or {} end

local function find_by_id(id)
    if not id then return nil end
    for i, e in ipairs(get_playlist()) do
        if e.id == id then return i - 1, e end
    end
    return nil
end

local function publish_hidden()
    local m = {}
    if staged and staged_temp and staged_id then m[tostring(staged_id)] = true end
    mp.set_property_native('user-data/ap/hidden', m)
end

-- mpv's playlist-move puts the entry *before* the target, so moving forward needs +1
local function move_to_final(idx, final)
    if idx == final then return end
    mp.commandv('playlist-move', tostring(idx), tostring(idx < final and final + 1 or final))
end

-- take the staged item out of the playlist (or back to where it was) and put it at the head of the queue again
local function unstage()
    if not staged then return end
    local idx = find_by_id(staged_id)
    if idx and idx ~= playlist_pos() then
        if staged_temp then
            mp.commandv('playlist-remove', tostring(idx))
        else
            local pidx = find_by_id(staged_prev_id)
            move_to_final(idx, pidx and (pidx < idx and pidx + 1 or pidx) or 0)
        end
    end
    table.insert(queue, 1, staged)
    staged, staged_id, staged_temp, staged_prev_id = nil, nil, false, nil
    publish_hidden()
end

-- put `path` into the playlist right after the playing entry; sets the staged_* state; returns the 0-based index or nil
local function stage_item(path)
    local list = get_playlist()
    local cur = playlist_pos()
    local found
    for i, e in ipairs(list) do
        if e.filename == path and (i - 1) ~= cur then found = i - 1; break end
    end
    staged = path
    if found then
        staged_temp = false
        staged_id = list[found + 1].id
        staged_prev_id = list[found] and list[found].id or nil
        local final = found < cur and cur or cur + 1
        move_to_final(found, final)
        return final
    end
    staged_temp = true
    staged_prev_id = nil
    mp.commandv('loadfile', path, 'append')
    local l2 = get_playlist()
    local count = #l2
    staged_id = l2[count] and l2[count].id
    publish_hidden()   -- hide it before it is moved, so the Playlist tab never shows it
    local final = cur + 1
    move_to_final(count - 1, final)
    return final
end

local function stage()
    if staged or #queue == 0 then return end
    if playlist_count() == 0 then
        -- idle player: start the queue right away
        local head = table.remove(queue, 1)
        pending_idle_item = head
        mp.commandv('loadfile', head, 'replace')
        return
    end
    -- queue_mode=tabs: a Playlist video goes on with the playlist; the queue only runs after a video that was started from the Queue tab
    if opts.queue_mode ~= 'after' and not playing_item then return end
    stage_item(table.remove(queue, 1))
    publish_hidden()
end

-- keep the staged item right behind the playing one when the playlist is edited / sorted / shuffled
local ensure_timer
local function ensure_staged()
    if ensure_timer then ensure_timer:kill() end
    ensure_timer = mp.add_timeout(0.15, function()
        if not staged then return end
        local idx = find_by_id(staged_id)
        local cur = playlist_pos()
        if not idx then
            table.insert(queue, 1, staged)
            staged, staged_id, staged_temp, staged_prev_id = nil, nil, false, nil
            publish_hidden()
            stage()
        elseif idx ~= cur + 1 and idx ~= cur then
            unstage()
            stage()
        end
    end)
end
mp.observe_property('playlist', 'native', ensure_staged)
mp.observe_property('playlist-pos', 'number', ensure_staged)

local function edit(fn)
    unstage()
    fn(queue)
    stage()
    save()
    refresh()
end

-- ---------- actions ----------
local VIDEO_EXT = {}
for e in ('mkv mp4 avi webm mov m4v ts m2ts flv wmv mpg mpeg ogv mp3 flac m4a ogg opus wav'):gmatch('%S+') do VIDEO_EXT[e] = true end

-- a dropped / picked folder becomes its videos, in natural order
local function expand(list)
    local out = {}
    for _, p in ipairs(list) do
        local fi = (type(p) == 'string' and not is_url(p)) and utils.file_info(p) or nil
        if fi and fi.is_dir then
            local files = utils.readdir(p, 'files') or {}
            local found = {}
            for _, f in ipairs(files) do
                local ext = f:match('%.([^.]+)$')
                if ext and VIDEO_EXT[ext:lower()] then found[#found + 1] = utils.join_path(p, f) end
            end
            table.sort(found, function(a, b) return natural_key(base_name(a)) < natural_key(base_name(b)) end)
            for _, f in ipairs(found) do out[#out + 1] = f end
        else
            out[#out + 1] = p
        end
    end
    return out
end

local function add_paths(list)
    local n = 0
    list = expand(list)
    edit(function(q)
        for _, p in ipairs(list) do
            if type(p) == 'string' and p ~= '' then q[#q + 1] = p; n = n + 1 end
        end
    end)
    if n > 0 then mp.osd_message('Added ' .. n .. ' to the queue', 2) end
end

local function sort_queue_now(mode)
    edit(function(q)
        local items = {}
        for i, p in ipairs(q) do
            local it = { path = p, idx = i, name = natural_key(base_name(p)), size = 0, time = 0 }
            local len = dur_of(p)
            it.len = len or -1
            it.len_short = len or math.huge
            if not is_url(p) then
                local fi = utils.file_info(p)
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
        else return end
        table.sort(items, function(a, b)
            if cmp(a, b) then return true end
            if cmp(b, a) then return false end
            return a.idx < b.idx
        end)
        for i, it in ipairs(items) do q[i] = it.path end
    end)
end

local function sort_queue(mode)
    if mode == 'length' or mode == 'length_short' then
        mp.osd_message('Reading durations...', 2)
        with_durations(full(), function() sort_queue_now(mode) end)
    else
        sort_queue_now(mode)
    end
end

local function shuffle_queue()
    edit(function(q)
        for i = #q, 2, -1 do
            local j = math.random(i)
            q[i], q[j] = q[j], q[i]
        end
    end)
    mp.osd_message('Queue shuffled', 2)
end

local function clear_queue()
    edit(function(q) for i = #q, 1, -1 do q[i] = nil end end)
    mp.osd_message('Queue cleared', 2)
end

local function remove_at(i)
    edit(function(q) table.remove(q, i) end)
end

local function move_item(from, to)
    edit(function(q)
        if from < 1 or from > #q then return end
        to = math.max(1, math.min(to, #q))
        local p = table.remove(q, from)
        table.insert(q, to, p)
    end)
end

local function play_now(i)
    unstage()
    local path = table.remove(queue, i)
    if path then
        if playlist_count() == 0 then
            pending_idle_item = path
            mp.commandv('loadfile', path, 'replace')
        else
            local idx = stage_item(path)
            publish_hidden()
            mp.commandv('playlist-play-index', tostring(idx))   -- file-loaded sees the staged item playing and consumes it
        end
    end
    save()
    refresh()
end

-- ---------- file pickers (Windows PowerShell, no extra files needed) ----------
local EXT = "'.mkv','.mp4','.avi','.webm','.mov','.m4v','.ts','.m2ts','.flv','.wmv','.mpg','.mpeg','.ogv','.mp3','.flac','.m4a','.ogg','.opus','.wav'"
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

local function add_files(reopen)
    run_ps("$d=New-Object Windows.Forms.OpenFileDialog;$d.Multiselect=$true;$d.Title='Add to queue';" ..
        "$d.Filter='Video / audio|*.mkv;*.mp4;*.avi;*.webm;*.mov;*.m4v;*.ts;*.m2ts;*.flv;*.wmv;*.mpg;*.mpeg;*.mp3;*.flac;*.m4a;*.ogg;*.opus;*.wav|All files|*.*';" ..
        "if($d.ShowDialog($o) -eq 'OK'){$d.FileNames}", function(list)
            table.sort(list, function(a, b) return natural_key(base_name(a)) < natural_key(base_name(b)) end)
            add_paths(list)
            if reopen then open_menu() end
        end)
end

local function add_folder(reopen)
    run_ps("$d=New-Object Windows.Forms.FolderBrowserDialog;$d.Description='Add every video in this folder to the queue';" ..
        "if($d.ShowDialog($o) -eq 'OK'){Get-ChildItem -LiteralPath $d.SelectedPath -File | Where-Object { @(" .. EXT ..
        ") -contains $_.Extension.ToLower() } | ForEach-Object { $_.FullName }}", function(list)
            table.sort(list, function(a, b) return natural_key(base_name(a)) < natural_key(base_name(b)) end)
            add_paths(list)
            if reopen then open_menu() end
        end)
end

local function add_clipboard(reopen)
    run_ps("$c=Get-Clipboard -Format FileDropList;if($c){$c|ForEach-Object{$_.FullName}}else{Get-Clipboard}", function(list)
        if #list == 0 then mp.osd_message('Clipboard has no file or URL', 2) if reopen then open_menu() end return end
        add_paths(list)
        if reopen then open_menu() end
    end)
end

-- ---------- events ----------
local function on_event(json)
    local e = utils.parse_json(json or '')
    if type(e) ~= 'table' then return end
    local off = playing_item and 1 or 0
    if e.type == 'activate' then
        local i = tonumber(e.value)
        if not i then return end
        if playing_item and i == 1 then
            -- the video that is playing now: nothing to start, it can be shown in Explorer or taken out of the list
            if e.action == 'show' then show_in_explorer(playing_item)
            elseif e.action == 'remove' then playing_item = nil; save(); refresh() end
            return
        end
        local qi = i - off
        if e.action == 'up' or e.action == 'down' then last_move_t = mp.get_time() end
        if e.action == 'up' then sel_index = math.max(off + 1, i - 1); move_item(qi, qi - 1)
        elseif e.action == 'down' then sel_index = math.min(#full(), i + 1); move_item(qi, qi + 1)
        elseif e.action == 'show' then show_in_explorer(full()[i])
        elseif e.action == 'remove' then remove_at(qi)
        else
            -- a double click used to start a second row (the list had already moved up): one start per 0.8 s
            local now = mp.get_time()
            if now - last_move_t < 0.8 then sel_index = i; click_row = nil; return end   -- right after a drag / move: only select
            if click_row ~= i or now - click_time > 0.6 then
                click_row, click_time = i, now      -- first click: select only
                sel_index = i
                return
            end
            click_row = nil
            if now - last_activate < 0.8 then return end
            last_activate = now
            sel_index = nil
            mp.commandv('script-message-to', 'uosc', 'close-menu', MENU_TYPE)
            play_now(qi)
        end
    elseif e.type == 'move' then
        local from, to = tonumber(e.from_index), tonumber(e.to_index)
        if from and to and from > off and to > off then sel_index = to; last_move_t = mp.get_time(); move_item(from - off, to - off) end
    elseif e.type == 'key' and e.key == 'del' and e.selected_item then
        local i = tonumber(e.selected_item.value)
        if i and i > off then remove_at(i - off) end
    end
end

local was_narrow
mp.observe_property('osd-width', 'number', function(_, w)
    if not w or w <= 0 then return end
    local narrow = w < 620
    if was_narrow == nil then was_narrow = narrow return end
    if narrow ~= was_narrow then was_narrow = narrow; refresh() end   -- the row buttons follow the window size while the menu is open
end)
mp.register_script_message('ap-queue-event', on_event)
mp.register_script_message('ap-queue-open', open_menu)
mp.register_script_message('ap-queue-menu', open_menu)           -- old name (control bar button / F2)
mp.register_script_message('ap-queue-sort', function(mode, reopen) sort_queue(mode); if reopen then open_menu() end end)
mp.register_script_message('ap-queue-add-menu', add_menu)
mp.register_script_message('ap-queue-shuffle', shuffle_queue)
mp.register_script_message('ap-queue-clear', clear_queue)
mp.register_script_message('ap-queue-add-files', function(r) add_files(r == 'menu') end)
mp.register_script_message('ap-queue-add-folder', function(r) add_folder(r == 'menu') end)
mp.register_script_message('ap-queue-add-clipboard', function(r) add_clipboard(r == 'menu') end)
mp.register_script_message('ap-queue-add-many', function(...) add_paths({ ... }) end)   -- drag & drop "Play later"
mp.register_script_message('ap-queue-add', function(path) if path and path ~= '' then add_paths({ path }) end end)
mp.register_script_message('ap-queue-add-current', function()
    local p = mp.get_property('path')
    if p and p ~= '' then add_paths({ p }) end
end)
mp.register_script_message('ap-queue-attach', function() end)    -- the resume script still calls this; the queue no longer needs it
mp.register_script_message('ap-queue-remove-played', function() end)
mp.register_script_message('ap-queue-restore', function() end)

-- Play later (drag & drop): the player writes the dropped paths to queue-inbox.txt and sends this message
mp.register_script_message('ap-queue-inbox', function()
    local path = utils.join_path(cfg, 'queue-inbox.txt')
    local f = io.open(path, 'rb')
    if not f then return end
    local s = f:read('*a') or ''
    f:close()
    os.remove(path)
    local list = {}
    for line in s:gmatch('[^\r\n]+') do list[#list + 1] = line end
    add_paths(list)
end)

-- ---------- sorting the PLAYLIST (the Playlist tab) ----------
local function sort_playlist_now(mode)
    unstage()
    local pl = mp.get_property_native('playlist') or {}
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
    stage()
    save()
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
            { title = 'Back to the playlist', icon = 'arrow_back', value = { 'script-binding', 'uosc/playlist' } },
        },
    }))
end
mp.register_script_message('ap-playlist-sort-menu', playlist_sort_menu)
mp.register_script_message('ap-playlist-sort', function(mode)
    sort_playlist(mode)
    mp.add_timeout(0.15, function() mp.commandv('script-binding', 'uosc/playlist') end)
end)

-- ---------- remove watched entries from the lists (the files on disk are not touched) ----------
local function clear_watched_playlist()
    unstage()
    local pl = mp.get_property_native('playlist') or {}
    local n = 0
    for i = #pl, 1, -1 do
        local e = pl[i]
        if not e.current and is_watched(e.filename) then
            mp.commandv('playlist-remove', tostring(i - 1))
            n = n + 1
        end
    end
    stage()
    save()
    refresh()
    mp.osd_message(n > 0 and ('Removed ' .. n .. ' watched from the playlist') or 'No watched files in the playlist', 2)
end

local function clear_watched_queue(reopen)
    local n = 0
    edit(function(q)
        for i = #q, 1, -1 do
            if is_watched(q[i]) then table.remove(q, i); n = n + 1 end
        end
    end)
    mp.osd_message(n > 0 and ('Removed ' .. n .. ' watched from the queue') or 'No watched files in the queue', 2)
    if reopen then open_menu() end
end
-- remove every video of the Playlist tab except the playing one (queue items stay in the Queue tab)
local function clear_playlist()
    unstage()
    mp.commandv('playlist-clear')
    mp.set_property_native('user-data/ap/no-autoload', true)   -- autoload.lua would put the whole folder back when only one entry is left
    stage()
    save()
    refresh()
    mp.osd_message('Playlist cleared (the playing video stays)', 2)
end
mp.register_script_message('ap-playlist-clear', clear_playlist)
mp.register_event('end-file', function()
    mp.set_property_native('user-data/ap/no-autoload', false)
    if playing_item then playing_item = nil; save(); refresh() end   -- the queue video has ended (or you moved on): now it leaves the queue
end)
mp.register_script_message('ap-playlist-clear-watched', clear_watched_playlist)
mp.register_script_message('ap-queue-clear-watched', function(r) clear_watched_queue(r == 'menu') end)

-- a file started
mp.register_event('file-loaded', function()
    local cur_id
    for _, e in ipairs(get_playlist()) do if e.current then cur_id = e.id end end
    -- the extra copy of a queue video that played before this file is no longer needed
    if consumed_temp_id and consumed_temp_id ~= cur_id then
        local idx = find_by_id(consumed_temp_id)
        if idx and idx ~= playlist_pos() then mp.commandv('playlist-remove', tostring(idx)) end
        consumed_temp_id = nil
    end
    if staged and staged_id == cur_id then
        -- the staged queue item is playing now: it stays in the Queue tab (as the playing row) until it has ended
        if staged_temp then consumed_temp_id = staged_id end
        playing_item = staged
        staged, staged_id, staged_temp, staged_prev_id = nil, nil, false, nil
        publish_hidden()
    elseif staged then
        unstage()   -- the user went somewhere else: stage again behind the new current entry
    end
    if pending_idle_item then
        if mp.get_property('path') == pending_idle_item then playing_item = pending_idle_item end
        pending_idle_item = nil
    end
    stage()
    save()
    refresh()
end)

-- shuffle the Playlist tab without scattering the staged queue item
mp.register_script_message('ap-playlist-shuffle', function()
    unstage()
    mp.commandv('playlist-shuffle')
    stage()
    save()
    mp.osd_message('Playlist shuffled', 2)
end)

queue = load_saved()
mp.add_timeout(2, function()
    if playlist_count() == 0 and #queue > 0 then
        mp.osd_message('Queue: ' .. #queue .. ' video(s) waiting (open the Queue tab)', 4)
    end
end)
