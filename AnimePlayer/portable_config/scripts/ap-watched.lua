-- AnimePlayer: remembers which files you have finished, so the Playlist and Queue tabs can grey them out (with a check mark).
-- A file counts as watched when it plays to the end, or when you leave it after 92 % of it. Saved in watched-files.json (kept between runs).
local mp = require 'mp'
local utils = require 'mp.utils'

local FILE = utils.join_path(mp.command_native({ 'expand-path', '~~/' }), 'watched-files.json')
local MAX = 3000
local order, set = {}, {}
-- keys of the published set are normalised (lower case, forward slashes) so a path written one way in the log and another way in the playlist still match
local function norm(p) return (p:lower():gsub('\\', '/')) end
local cur_path, last_pct = nil, 0

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
    if not path or path == '' or set[norm(path)] then return end
    set[norm(path)] = true
    order[#order + 1] = path
    while #order > MAX do set[norm(table.remove(order, 1))] = nil end
    save()
    publish()
end

mp.register_event('file-loaded', function()
    cur_path = mp.get_property('path')
    last_pct = 0
end)

mp.observe_property('percent-pos', 'number', function(_, v) if v then last_pct = v end end)

mp.observe_property('eof-reached', 'bool', function(_, v) if v and cur_path then mark(cur_path) end end)

mp.register_event('end-file', function(e)
    if not cur_path then return end
    if e.reason == 'eof' or last_pct >= 92 then mark(cur_path) end
end)

-- script-message ap-watched-clear  /  ap-watched-toggle (current file)
mp.register_script_message('ap-watched-clear', function()
    order, set = {}, {}
    save(); publish()
    mp.osd_message('Watched marks cleared', 2)
end)
