-- AnimePlayer: playback speed carries over to the next file (and the next episode of the playlist / queue) for as long as the player is open.
-- It is NOT saved: closing the player and opening it again starts at the normal speed.
-- mpv resets `speed` for every file (reset-on-next-file in the anime build's mpv.conf) and some profiles / watch-later data set it too, so the
-- speed you chose is remembered here and put back every time something else changes it while a file is loading.
local mp = require 'mp'

local base = 1.0   -- the default playback speed is always 1x (the Reset speed button and every new session use it)
local wanted = base                                             -- what the user chose this session
local loading = false
local settle = nil

local function put_back()
    local cur = mp.get_property_number('speed', 1)
    if math.abs(cur - wanted) > 0.001 then mp.set_property_number('speed', wanted) end
end

local function begin_loading()
    loading = true
    if settle then settle:kill(); settle = nil end
end

local function end_loading(delay)
    if settle then settle:kill() end
    settle = mp.add_timeout(delay, function() loading = false; put_back() end)
end

mp.register_event('start-file', begin_loading)
mp.register_event('end-file', begin_loading)
mp.register_event('file-loaded', function() put_back(); end_loading(2.5) end)
mp.register_event('playback-restart', function() if loading then put_back() end end)

mp.observe_property('speed', 'number', function(_, v)
    if not v then return end
    if loading then
        -- something other than the user (the per-file reset, a profile) changed it: undo that
        if math.abs(v - wanted) > 0.001 then mp.add_timeout(0, put_back) end
    else
        wanted = v
    end
end)

-- "Reset speed" button / key: toggles. First press -> the default speed (and remembers the speed you had); pressing it again while at the
-- default speed -> back to the speed you had before.
local previous = nil
mp.register_script_message('speed-reset', function()
    local cur = mp.get_property_number('speed', 1)
    loading = false
    if math.abs(cur - base) > 0.001 then
        previous = cur
        wanted = base
        mp.set_property_number('speed', base)
        mp.osd_message(string.format('Speed %.2fx (default)  -  press again to go back to %.2fx', base, cur), 2)
    elseif previous then
        wanted = previous
        mp.set_property_number('speed', previous)
        mp.osd_message(string.format('Speed %.2fx (back)', previous), 1.5)
        previous = nil
    else
        mp.osd_message(string.format('Speed %.2fx (default)', base), 1.5)
    end
end)
