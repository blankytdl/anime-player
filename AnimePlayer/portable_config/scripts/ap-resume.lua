-- AnimePlayer: resume positions + auto-start.
--  * the position of every video you watched for >= 10 s is saved every 2 s, when the file is unloaded
--    (next / stop / open another) and on shutdown, so a crash or power cut loses at most 2 s;
--  * the uosc "Resume" button (script-message ap-resume-now) jumps back to it - or, with nothing loaded,
--    re-opens the last video (and its saved queue) at that position;
--  * a new file always starts playing (keep-open leaves the player paused after the previous video ended).
-- Data file: resume.json  { "Last": "<path>", "Entries": { "<path>": { "Pos": s, "Dur": s, "Updated": unix } } }
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

local opts = { hint = true, interval = 2, min_pos = 10, end_margin = 20, autostart = true, idle_dblclick = true, auto_resume = true }
require('mp.options').read_options(opts, 'ap-resume')

local cfg = mp.command_native({ 'expand-path', '~~/' })
local FILE = utils.join_path(cfg, 'resume.json')
local PART = FILE .. '.part'

local data = { Entries = {} }
local last_path, last_pos = nil, -100
local pending_seek = nil
local skip_start_for = nil    -- the next load already carries its own start= (Resume button)

local function is_url(p) return p:find('://', 1, true) ~= nil end

-- one spelling per file: 'C:/a/b.mkv' (autoload / playlist) and 'C:\\a\\b.mkv' (Explorer / drag-drop) used to be two different keys,
-- so a position saved from one was never found when the file was opened the other way
local function key_of(path)
    if is_url(path) then return path end
    if not (path:match('^%a:[/\\]') or path:match('^[/\\][/\\]')) then
        local cwd = utils.getcwd()
        if cwd then path = utils.join_path(cwd, path) end
    end
    return (path:gsub('/', '\\'))
end

local function fmt(t)
    t = math.max(0, math.floor(t))
    local h, m, s = math.floor(t / 3600), math.floor(t % 3600 / 60), t % 60
    if h > 0 then return string.format('%d:%02d:%02d', h, m, s) end
    return string.format('%d:%02d', m, s)
end

local function read_json(p)
    local f = io.open(p, 'rb')
    if not f then return nil end
    local s = f:read('*a')
    f:close()
    if not s or s == '' then return nil end
    local t = utils.parse_json(s)
    return type(t) == 'table' and t or nil
end

local function save()
    local json = utils.format_json(data)
    if not json then return end
    local f = io.open(PART, 'wb')
    if not f then return end
    f:write(json)
    f:close()
    os.remove(FILE) -- Windows: rename() fails when the target exists
    if not os.rename(PART, FILE) then msg.warn('could not write ' .. FILE) end
end

local function load()
    local t = read_json(FILE) or read_json(PART) -- .part survives a crash between remove and rename
    if t then
        data.Last = t.Last and key_of(t.Last) or nil
        data.LastOpened = t.LastOpened and key_of(t.LastOpened) or nil
        data.Entries = {}
        if type(t.Entries) == 'table' then
            for k, v in pairs(t.Entries) do data.Entries[key_of(k)] = v end
        end
    end
end

local function prune()
    local changed = false
    for k in pairs(data.Entries) do
        if not is_url(k) and not utils.file_info(k) then
            local dir = utils.split_path(k)
            if #dir > 3 then dir = dir:gsub('[/\\]+$', '') end
            -- only forget when the folder is reachable but the file is gone (an unplugged drive keeps its entries)
            if dir ~= '' and utils.file_info(dir) then
                data.Entries[k] = nil
                if data.Last == k then data.Last = nil end
                changed = true
            end
        end
    end
    if changed then save() end
end

local function snapshot()
    local raw = mp.get_property('path')
    if not raw then return end
    local pos = mp.get_property_number('time-pos')
    if not pos then return end
    local path = key_of(raw)
    local dur = mp.get_property_number('duration') or 0
    if pos < opts.min_pos then return end
    if dur > 0 and pos > dur - opts.end_margin then
        -- finished (or seeked to the very end): forget it
        if data.Entries[path] then
            data.Entries[path] = nil
            if data.Last == path then data.Last = nil end
            save()
        end
        return
    end
    if path == last_path and math.abs(pos - last_pos) < 1 then return end
    data.Entries[path] = { Pos = pos, Dur = dur, Updated = os.time() }
    data.Last = path
    last_path, last_pos = path, pos
    save()
end

local function current_entry()
    local raw = mp.get_property('path')
    return raw and data.Entries[key_of(raw)] or nil
end

local function resume_now()
    local raw = mp.get_property('path')
    if raw and mp.get_property_bool('eof-reached', false) then raw = nil end
    if raw then
        local e = current_entry()
        if not e then
            mp.osd_message('Nothing to resume for this video', 2)
            return
        end
        local pos = mp.get_property_number('time-pos') or 0
        if pos >= e.Pos - 5 then
            mp.osd_message('Already at the saved position (' .. fmt(e.Pos) .. ')', 2)
            return
        end
        mp.commandv('seek', string.format('%.3f', e.Pos), 'absolute')
        mp.osd_message('Resumed at ' .. fmt(e.Pos), 2)
        return
    end

    -- nothing loaded: re-open the last video where it stopped
    local last = data.Last
    local e = last and data.Entries[last]
    local opened = data.LastOpened
    if opened and (is_url(opened) or utils.file_info(opened)) and (not last or opened ~= last) then
        -- the newest video you opened wins, also when it was watched to the end (no saved position: it starts from the beginning)
        last, e = opened, data.Entries[opened]
    end
    if not last or (not is_url(last) and not utils.file_info(last)) then last, e = nil, nil end
    if not e and last then
        mp.commandv('loadfile', last)
        mp.osd_message('Opening the last video', 2)
        return
    end
    if not e or (not is_url(last) and not utils.file_info(last)) then
        -- the anime build keeps its own watch history (uosc_history.json, newest first). Re-open its newest unfinished video:
        -- mpv's watch-later (saved every 30 s by the build's autosave.lua) puts it back at the right position by itself.
        local h = read_json(utils.join_path(cfg, 'uosc_history.json'))
        if type(h) == 'table' then
            for _, item in ipairs(h) do
                if type(item) == 'table' and type(item.path) == 'string' and (tonumber(item.percent) or 100) < 95
                    and (is_url(item.path) or utils.file_info(item.path)) then
                    mp.commandv('loadfile', item.path)
                    mp.osd_message('Resuming ' .. (item.title or item.path), 2)
                    return
                end
            end
        end
        mp.osd_message('Nothing to resume yet', 2)
        return
    end
    skip_start_for = true
    local ok = mp.command_native({ name = 'loadfile', url = last, flags = 'replace', options = 'start=' .. string.format('%.3f', e.Pos) })
    if ok == nil then
        pending_seek = { path = last, pos = e.Pos } -- very old mpv: seek after the file is loaded
        mp.commandv('loadfile', last)
    end
    mp.osd_message('Resuming ' .. fmt(e.Pos), 2)
    mp.commandv('script-message', 'ap-queue-attach', last) -- re-attach the saved queue around it
end

local function forget()
    local raw = mp.get_property('path')
    if not raw then return end
    local k = key_of(raw)
    data.Entries[k] = nil
    if data.Last == k then data.Last = nil end
    save()
    mp.osd_message('Saved position forgotten', 2)
end

-- Double-click: with a video it toggles fullscreen as always; on an EMPTY player (nothing loaded) it plays the last video again.
-- Switch it off with idle_dblclick=no in script-opts/ap-resume.conf (AnimePlayer Settings has a checkbox). Read on every click.
local function idle_dblclick_enabled()
    local f = io.open(utils.join_path(cfg, 'script-opts/ap-resume.conf'), 'rb')
    if not f then return true end
    local txt = f:read('*a') or ''
    f:close()
    local v = txt:match('idle_dblclick%s*=%s*(%a+)')
    return not (v and (v:lower() == 'no' or v:lower() == 'false'))
end

-- Round 5: a double-click no longer goes fullscreen. The first click of a double-click already ran `cycle pause` (MBTN_LEFT), so the
-- double-click toggles it back: a double-click leaves pause / play exactly as it was. Fullscreen stays on F / Enter / the button.
mp.register_script_message('ap-dblclick', function()
    if (mp.get_property('path') == nil or mp.get_property_bool('idle-active', false) or mp.get_property_bool('eof-reached', false))
        and idle_dblclick_enabled() then
        resume_now()
    else
        mp.commandv('cycle', 'pause')
    end
end)

-- "Start from the beginning": a loaded video jumps back to 0:00; with nothing loaded the last video is opened from the start.
local function restart_now()
    if mp.get_property('path') ~= nil then
        mp.commandv('seek', '0', 'absolute+exact')
        mp.osd_message('Starting from the beginning', 2)
        return
    end
    local last = data.Last
    if last and (is_url(last) or utils.file_info(last)) then
        mp.commandv('loadfile', last)
        mp.osd_message('Starting from the beginning', 2)
    else
        mp.osd_message('Nothing to restart yet', 2)
    end
end

mp.register_script_message('ap-restart-now', restart_now)
mp.register_script_message('ap-resume-now', resume_now)
mp.register_script_message('ap-resume-forget', forget)

mp.register_event('file-loaded', function()
    if opts.autostart then mp.set_property_bool('pause', false) end
    do
        local raw0 = mp.get_property('path')
        if raw0 then
            data.LastOpened = key_of(raw0)
            save()
            -- every video continues where it was closed (switch off: auto_resume=no in script-opts/ap-resume.conf)
            local e0 = data.Entries[data.LastOpened]
            if opts.auto_resume and e0 and not pending_seek and (mp.get_property_number('time-pos') or 0) < e0.Pos - 5 then
                mp.commandv('seek', string.format('%.3f', e0.Pos), 'absolute+exact')
                mp.osd_message('Continuing at ' .. fmt(e0.Pos), 2)
            end
        end
    end
    if pending_seek then
        local ps = pending_seek
        pending_seek = nil
        if key_of(mp.get_property('path') or '') == ps.path then
            mp.commandv('seek', string.format('%.3f', ps.pos), 'absolute')
        end
    end
    if not opts.hint then return end
    local e = current_entry()
    local raw = mp.get_property('path')
    if not e or not raw then return end
    local pos0 = e.Pos
    mp.add_timeout(1.5, function()
        if mp.get_property('path') ~= raw then return end
        if (mp.get_property_number('time-pos') or 0) < pos0 - 5 then
            mp.osd_message('Saved position ' .. fmt(pos0) .. ' - press the Resume button', 4)
        end
    end)
end)

-- Smooth resume: the saved position is given to mpv as `start` BEFORE the file opens, so playback begins right there
-- (the file-loaded seek below stays as a fallback; it used to show the first frame at 0:00 and then jump).
mp.add_hook('on_load', 50, function()
    if not opts.auto_resume or pending_seek or skip_start_for then skip_start_for = nil return end
    local raw = mp.get_property('path')
    local e = raw and data.Entries[key_of(raw)]
    if e and e.Pos and e.Pos >= opts.min_pos then
        mp.set_property('file-local-options/start', string.format('%.3f', e.Pos))
    end
end)

mp.add_hook('on_unload', 50, snapshot)
mp.register_event('shutdown', snapshot)
mp.add_periodic_timer(opts.interval, snapshot)

load()
prune()
