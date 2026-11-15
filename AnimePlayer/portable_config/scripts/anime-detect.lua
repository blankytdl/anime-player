-- Anime-aware auto-detection + profile pipeline selection (SD / HD / FHD, anime / live-action)
local utils = require("mp.utils")
local lite = false

local function looks_like_anime(path, tags)
    local p = (path or ""):lower()
    if p:find("%[subsplease%]") or p:find("%[erai%-raws%]") or p:find("%[horriblesubs%]")
       or p:find("%[judas%]") or p:find("%[ember%]") or p:find("anime") or p:find("%[%x%x%x%x%x%x%x%x%]") then
        return true
    end
    local sid_lang = mp.get_property("current-tracks/audio/lang") or ""
    if sid_lang == "jpn" or sid_lang == "ja" then return true end
    return false
end

local function apply()
    if lite then return end
    local h = mp.get_property_number("height", 0)
    local path = mp.get_property("path", "")
    local anime = looks_like_anime(path)
    local prof
    if anime then
        if h > 0 and h <= 576 then prof = "anime-sd"
        elseif h <= 800 then prof = "anime-hd"
        else prof = "anime-fhd" end
    else
        prof = (h > 0 and h <= 576) and "live-sd" or "live-hd"
    end
    mp.commandv("apply-profile", prof)
    mp.osd_message(string.format("[%s] %dp", prof, h), 2)
end
mp.register_event("file-loaded", apply)

mp.register_script_message("toggle-lite", function()
    lite = not lite
    if lite then mp.commandv("apply-profile", "lite") else apply() end
    mp.osd_message("Lite mode: " .. (lite and "ON (RX580/i5 saver)" or "OFF"), 2)
end)

mp.register_script_message("reapply", function() lite = false; apply() end)
