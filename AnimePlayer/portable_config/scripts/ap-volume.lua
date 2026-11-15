-- AnimePlayer: remembers volume + mute between files AND between sessions.
-- Stored in <config>/ap-volume.conf. Re-applied after every file load, so a per-file value saved by
-- watch-later / another script can never change the loudness behind your back.
-- Volume changes within 3 s of a file transition are side effects (watch-later restoring an old value), never your choice.
local file = mp.command_native({"expand-path", "~~/ap-volume.conf"})
local saved_vol, saved_mute = nil, nil
local block_until = 0

local function now() return mp.get_time() end

local function load()
    local f = io.open(file, "r")
    if not f then return end
    for line in f:lines() do
        local k, v = line:match("^(%w+)=(.+)$")
        if k == "volume" then saved_vol = tonumber(v)
        elseif k == "mute" then saved_mute = (v == "yes") end
    end
    f:close()
end

local function save()
    if saved_vol == nil then return end
    local f = io.open(file, "w")
    if not f then return end
    f:write(string.format("volume=%.1f\nmute=%s\n", saved_vol, saved_mute and "yes" or "no"))
    f:close()
end

local applying = false
local function apply()
    if saved_vol == nil then return end
    applying = true
    if math.abs((mp.get_property_number("volume", 100)) - saved_vol) > 0.05 then mp.set_property_number("volume", saved_vol) end
    if saved_mute ~= nil and mp.get_property_bool("mute", false) ~= saved_mute then mp.set_property_bool("mute", saved_mute) end
    mp.add_timeout(0.6, function() applying = false end)   -- ignore the echo of our own change
end

load()
apply()                                    -- before the first file starts

local function transition() block_until = now() + 3.0 end
mp.register_event("start-file", transition)
mp.register_event("end-file", transition)
mp.register_event("file-loaded", function()
    apply()
    mp.add_timeout(0.3, apply)
    mp.add_timeout(1.0, apply)
end)

local timer
local function later()
    if timer then timer:kill() end
    timer = mp.add_timeout(0.8, save)      -- one write after a volume drag, not hundreds
end

mp.observe_property("volume", "number", function(_, v)
    if v ~= nil and not applying and now() >= block_until then saved_vol = v; later() end
end)
mp.observe_property("mute", "bool", function(_, v)
    if v ~= nil and not applying and now() >= block_until then saved_mute = v; later() end
end)
mp.register_event("shutdown", save)
