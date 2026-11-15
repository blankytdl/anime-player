-- Visual playback speed adjuster (OSD slider) + step/reset messages
local function bar(v)
    local lo, hi, w = 0.25, 4.0, 30
    local pos = math.floor((math.min(math.max(v, lo), hi) - lo) / (hi - lo) * w + 0.5)
    return string.rep("█", pos) .. "●" .. string.rep("░", w - pos)
end
local function show()
    local v = mp.get_property_number("speed", 1)
    mp.osd_message(string.format("Speed %.2fx\n0.25 %s 4.0", v, bar(v)), 1.5)
end
mp.register_script_message("speed-step", function(d)
    local v = mp.get_property_number("speed", 1) + tonumber(d)
    mp.set_property_number("speed", math.max(0.1, math.min(8, v))); show()
end)
-- "speed-reset" is handled by ap-speed.lua (it toggles between the default speed and the speed you had)
