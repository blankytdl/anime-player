-- AnimePlayer: stay fullscreen when the file changes (next / previous / queue / a new file opened while playing).
-- Remembers what YOU chose (Enter, Esc, double-click, F ...). A "fullscreen = no" that appears within ~2 s of a file
-- transition is treated as a side effect of the change (window re-layout), not as your choice, and is undone.
local want_fs = mp.get_property_bool("fullscreen", false)
local transition_until = 0
local restoring = false

local function now() return mp.get_time() end

mp.observe_property("fullscreen", "bool", function(_, v)
    if v == nil or restoring then return end
    if v == false and now() < transition_until and want_fs then
        -- dropped during a file change: put it back
        restoring = true
        mp.add_timeout(0.15, function()
            mp.set_property_bool("fullscreen", true)
            mp.add_timeout(0.5, function() restoring = false end)
        end)
        return
    end
    want_fs = v
end)

local function begin_transition() transition_until = now() + 2.0 end
mp.register_event("start-file", begin_transition)
mp.register_event("end-file", begin_transition)

mp.register_event("file-loaded", function()
    if want_fs and not mp.get_property_bool("fullscreen", false) then
        restoring = true
        mp.set_property_bool("fullscreen", true)
        mp.add_timeout(0.5, function() restoring = false end)
    end
end)
