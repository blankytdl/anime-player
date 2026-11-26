-- AnimePlayer: dragging the progress bar to the very end must go on to the next video (build 34).
-- uosc pauses the video while the bar is dragged and puts the old pause state back on release. A seek to the very end while paused leaves mpv
-- at "end of file" without moving on: after the release the video sits on its last frame and never starts the next one.
-- This script notices "end reached, not paused, still the same playlist entry" and does what mpv does at a normal end: next entry.
-- (The queue's staged video is simply the next playlist entry, so the queue / playlist order is respected exactly as at a normal end.)
-- The last video of the list is left alone (keep-open: it stays on its last frame as before). A video the user paused on purpose is never skipped.
local mp = require 'mp'
local msg = require 'mp.msg'

local DELAY = 0.5       -- mpv gets this long to move on by itself first (normal end of file: the entry has changed by then)
local timer
local function entry_id()
    for _, e in ipairs(mp.get_property_native('playlist') or {}) do if e.current then return e.id end end
    return nil
end
local function has_next()
    local pos, count = mp.get_property_number('playlist-pos', -1), mp.get_property_number('playlist-count', 0)
    return pos >= 0 and pos + 1 < count
end
local function at_end()
    if mp.get_property_bool('eof-reached', false) then return true end
    local dur, pos = mp.get_property_number('duration'), mp.get_property_number('time-pos')
    return dur ~= nil and pos ~= nil and dur > 1 and pos >= dur - 0.25
end

local function arm()
    local id = entry_id()
    if timer then timer:kill() end
    timer = mp.add_timeout(DELAY, function()
        if entry_id() ~= id then return end                     -- mpv has already moved on
        if mp.get_property_bool('pause', false) then return end -- paused on purpose / bar still held
        if not at_end() then return end                         -- the user seeked back
        if not has_next() then return end                       -- last video: stays on its last frame
        msg.info('end of file reached without moving on -> next video')
        mp.commandv('playlist-next', 'force')
    end)
end

mp.observe_property('eof-reached', 'bool', function(_, v) if v then arm() end end)
mp.observe_property('pause', 'bool', function(_, v) if v == false and at_end() then arm() end end)
mp.register_event('playback-restart', function() if at_end() then arm() end end)
