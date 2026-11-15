-- AnimePlayer: anime playback detection + watch log for AnimeLibrary.
-- Every time a video is opened / closed a line is appended to watched.log (tab separated):
--   unix-time <TAB> path <TAB> event <TAB> position <TAB> duration <TAB> anime(1/0)
--   event: start | stop (closed or switched away) | finished (EOF, or >= 85 % watched)
-- AnimeLibrary reads this file - also while you are offline - and uploads the progress to MyAnimeList
-- when the internet is back. Nothing here needs a network connection.
local mp = require 'mp'
local utils = require 'mp.utils'

local cfg = mp.command_native({ 'expand-path', '~~/' })
local LOG = utils.join_path(cfg, 'watched.log')
local THRESHOLD = 0.85

local function looks_like_anime()
    local path = (mp.get_property('path') or ''):lower()
    local name = path:match('([^/\\]+)$') or path
    if name:match('^%[[^%]]+%]') then return true end                  -- [SubsPlease] ...
    if name:match('%[%x%x%x%x%x%x%x%x%]') then return true end         -- [1A2B3C4D] CRC tag
    if name:match('%s%-%s%d%d?%d?%f[%D]') then return true end         -- "Title - 05"
    if path:find('anime', 1, true) or path:find('donghua', 1, true) then return true end
    local lang = mp.get_property('current-tracks/audio/lang') or ''
    if (lang == 'jpn' or lang == 'ja') and (name:match('[sS]%d+[eE]%d+') or name:match('%f[%a]ep?%d+')) then return true end
    return false
end

local function log(event, pos, dur, anime)
    local path = mp.get_property('path')
    if not path then return end
    local f = io.open(LOG, 'ab')
    if not f then return end
    f:write(string.format('%d\t%s\t%s\t%.1f\t%.1f\t%d\n', os.time(), path, event, pos or 0, dur or 0, anime and 1 or 0))
    f:close()
end

local anime = false
local finished_logged = false

-- [AP17] "finished" is written the moment 85 % is reached or the file reaches its end - not only when the file is closed - so the library
-- marks the video as watched while you are still in the player. Files that do not look like anime are logged too (flag 0): the library
-- uses them when they live in one of its folders.
local function maybe_finish()
    if finished_logged then return end
    local pos = mp.get_property_number('time-pos') or 0
    local dur = mp.get_property_number('duration') or 0
    if dur > 0 and pos >= dur * THRESHOLD then
        finished_logged = true
        log('finished', pos, dur, anime)
    end
end

mp.register_event('file-loaded', function()
    anime = looks_like_anime()
    finished_logged = false
    if anime then log('start', 0, mp.get_property_number('duration') or 0, true) end
end)

mp.observe_property('percent-pos', 'number', function(_, v) if v and v >= THRESHOLD * 100 then maybe_finish() end end)
mp.observe_property('eof-reached', 'bool', function(_, v)
    if v and not finished_logged then
        finished_logged = true
        log('finished', mp.get_property_number('time-pos') or 0, mp.get_property_number('duration') or 0, anime)
    end
end)

mp.add_hook('on_unload', 50, function()
    local pos = mp.get_property_number('time-pos') or 0
    local dur = mp.get_property_number('duration') or 0
    if not finished_logged and dur > 0 and pos >= dur * THRESHOLD then
        finished_logged = true
        log('finished', pos, dur, anime)
    elseif anime and not finished_logged then
        log('stop', pos, dur, true)
    end
    anime = false
end)
