-- AnimePlayer: remembers which files you have finished, so the Playlist tab can grey them out (with a check mark).
-- (build 36) A file counts as watched when you watched it to the end - the same rule as the watch log of the library (85 % or more, or the last 3 s),
-- whether it ended by itself or you pressed Next during the credits. Opening it, resuming it near the end or seeking around does not count.
-- Saved in watched-files.json (kept between runs). Videos the library already knows as finished (watched.log) are greyed too.
local mp = require 'mp'
local utils = require 'mp.utils'

local FILE = utils.join_path(mp.command_native({ 'expand-path', '~~/' }), 'watched-files.json')
local MAX = 3000
local order, set = {}, {}
-- keys of the published set are normalised (lower case, forward slashes) so a path written one way in the log and another way in the playlist still match
local function norm(p) return (p:lower():gsub('\\', '/')) end
-- a file that was opened as "a.mkv" (relative) and is listed as "C:/x/a.mkv" (absolute) is the same file: both spellings are stored
local function abs_of(p)
    if p:find('://', 1, true) or p:match('^%a:[\\/]') or p:match('^[\\/][\\/]') then return p end
    local wd = mp.get_property('working-directory')
    if not wd or wd == '' then return p end
    return utils.join_path(wd, p)
end
local THRESHOLD = 0.85   -- same as ap-watch.lua (the library)
local cur_path = nil

local function publish() mp.set_property_native('user-data/ap/watched', set) end

local function save()
    local f = io.open(FILE, 'wb')
    if f then f:write(utils.format_json(order) or '[]'); f:close() end
end

do
    local f = io.open(FILE, 'rb')
    if f then
        local t = utils.parse_json(f:read('*a') or '')
        f:close()
        if type(t) == 'table' then
            for _, p in ipairs(t) do
                if type(p) == 'string' and not set[norm(p)] then order[#order + 1] = p; set[norm(p)] = true end
            end
        end
    end
    publish()
end

local function mark(path)
    if not path or path == '' then return end
    local abs = abs_of(path)
    if set[norm(path)] and set[norm(abs)] then return end
    set[norm(path)] = true
    set[norm(abs)] = true
    order[#order + 1] = abs
    while #order > MAX do set[norm(table.remove(order, 1))] = nil end
    save()
    publish()
end

-- (build 36) The old rule needed the player to be within 3 s of the end. A file whose length in the header is a little longer than the
-- real picture (very common) or an ending the viewer skipped with Next was never greyed although it was finished. Now: 85 % of the length
-- (like the library) or the last 3 s. A video that was OPENED at that position (resume near the end) and closed again does not count.
local last_pos, last_dur, first_pos = 0, 0, nil

local function reached_end()
    -- the position the player was at (time-pos is already gone at the very end, so the last seen value is used)
    local dur = last_dur
    return dur > 5 and (last_pos >= dur * THRESHOLD or last_pos >= dur - 3)
end
local function watched_through()
    -- Next / another file / closing: only when this run really started before the end part
    return reached_end() and (first_pos == nil or first_pos < last_dur * THRESHOLD)
end

mp.register_event('start-file', function() cur_path = nil; last_pos, last_dur, first_pos = 0, 0, nil end)
mp.register_event('file-loaded', function()
    cur_path = mp.get_property('path')
    last_pos, last_dur, first_pos = 0, mp.get_property_number('duration', 0) or 0, nil
end)

mp.observe_property('time-pos', 'number', function(_, v)
    if v and cur_path then
        last_pos = v
        if first_pos == nil then first_pos = v end
    end
end)
mp.observe_property('duration', 'number', function(_, v) if v and cur_path then last_dur = v end end)

-- last file of the playlist with keep-open: the player stops at the end instead of unloading. A late "eof-reached" event that belongs to the
-- PREVIOUS file is ignored because the position of the file that is playing now is not at its end.
mp.observe_property('eof-reached', 'bool', function(_, v)
    if v and cur_path and reached_end() then mark(cur_path) end
end)

mp.register_event('end-file', function(e)
    if not cur_path then return end
    if e.reason == 'eof' then
        if reached_end() then mark(cur_path) end        -- it ended by itself
    elseif e.reason == 'stop' or e.reason == 'quit' then
        if watched_through() then mark(cur_path) end    -- Next / another file / the player was closed during the credits
    end
    cur_path = nil
end)

-- script-message ap-watched-clear  /  ap-watched-toggle (current file)
mp.register_script_message('ap-watched-clear', function()
    order, set = {}, {}
    save(); publish()
    mp.osd_message('Watched marks cleared', 2)
end)

-- (build 36) videos that the watch log (watched.log, read by the library) already holds as finished are greyed too: they were finished before
-- this rule existed. One pass a few seconds after the start; a huge log (> 8 MB) is skipped.
mp.add_timeout(4, function()
    local log = utils.join_path(mp.command_native({ 'expand-path', '~~/' }), 'watched.log')
    local fi = utils.file_info(log)
    if not fi or not fi.is_file or (fi.size or 0) > 8 * 1024 * 1024 then return end
    local f = io.open(log, 'rb')
    if not f then return end
    local txt = f:read('*a') or ''
    f:close()
    local added = 0
    for line in txt:gmatch('[^\r\n]+') do
        local _, path, event = line:match('^(%d+)\t([^\t]+)\t(%a+)\t')
        if path and event == 'finished' and not set[norm(path)] then
            set[norm(path)] = true
            order[#order + 1] = path
            added = added + 1
        end
    end
    if added > 0 then
        while #order > MAX do set[norm(table.remove(order, 1))] = nil end
        save(); publish()
    end
end)
