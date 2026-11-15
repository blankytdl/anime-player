-- vsr_auto.lua  (AnimePlayer rewrite)
-- The build's version drove NVIDIA RTX Video Super Resolution (d3d11vpp scaling-mode=nvidia), which does nothing on an AMD RX 580.
-- This one is a GPU-shader upscaler that runs on any card, tuned for a Polaris GPU: Anime4K "Mode A (Fast)" (small / medium CNNs)
-- plus ewa_lanczossharp / mitchell scalers. The key (V), the script-binding name `toggle-vsr`, the `vsr_active` state and the power-saving
-- hand-off are unchanged, so every menu / button / script that talked to the old VSR keeps working. If the Anime4K files are missing
-- from <config>\shaders it still switches on the better scalers.
local mp = require 'mp'
local utils = require 'mp.utils'

local vsr_active = false
local power_locked = false
local was_active_before_lock = false
local saved = nil

local overlay = mp.create_osd_overlay("ass-events")
local timer = nil
local function show_osd(text)
    overlay.data = "{\\an9}{\\fs23}" .. text
    overlay:update()
    if timer then timer:kill() end
    timer = mp.add_timeout(4, function() overlay:remove() end)
end

local function update_status(is_active)
    mp.set_property("user-data/vsr_active", is_active and "yes" or "no")
    mp.commandv("script-message", "anime-state-broadcast", utils.format_json({ vsr_active = is_active }))
end

-- Anime4K "Mode A (Fast)": light enough for an RX 580 at 1080p -> 1440p / 4K
local CHAIN = {
    "Anime4K_Clamp_Highlights.glsl",
    "Anime4K_Restore_CNN_M.glsl",
    "Anime4K_Upscale_CNN_x2_M.glsl",
    "Anime4K_AutoDownscalePre_x2.glsl",
    "Anime4K_AutoDownscalePre_x4.glsl",
    "Anime4K_Upscale_CNN_x2_S.glsl",
}

local function existing_chain()
    local list = {}
    for _, name in ipairs(CHAIN) do
        local path = mp.command_native({ "expand-path", "~~/shaders/" .. name })
        if path and utils.file_info(path) then list[#list + 1] = "~~/shaders/" .. name end
    end
    -- the CNN upscaler itself must be present, otherwise the other files alone do nothing useful
    local has_up = false
    for _, p in ipairs(list) do if p:find("Upscale_CNN_x2_M", 1, true) then has_up = true end end
    return has_up and list or nil
end

local function set_state(enable, is_power_event)
    if enable then
        if power_locked and not is_power_event then
            show_osd("{\\c&H0000FF&}Locked: {\\c&HFFFFFF&}Power Saving Mode Active")
            return
        end
        saved = {
            shaders = mp.get_property_native("glsl-shaders"),
            scale = mp.get_property("scale"), cscale = mp.get_property("cscale"), dscale = mp.get_property("dscale"),
            deband = mp.get_property("deband"),
        }
        local v_h = mp.get_property_number("video-params/h")
        local d_h = mp.get_property_number("display-height") or mp.get_property_number("osd-height")
        local ratio = (v_h and d_h and v_h > 0) and d_h / v_h or 2.0

        mp.set_property("scale", "ewa_lanczossharp")
        mp.set_property("cscale", "ewa_lanczossharp")
        mp.set_property("dscale", "mitchell")

        local chain = existing_chain()
        if chain and ratio >= 1.2 then mp.set_property_native("glsl-shaders", chain) end   -- no CNN pass when nothing is upscaled

        if not power_locked and not is_power_event then
            local mode = (chain and ratio >= 1.2) and "Anime4K Fast + EWA scalers" or (chain and "EWA scalers (video already fills the screen)" or "EWA scalers (Anime4K files not found in shaders)")
            show_osd("{\\c&H00FF00&}GPU Upscaler: Active {\\c&HFFFFFF&}(" .. mode .. ")")
        end
        vsr_active = true
    else
        if saved then
            mp.set_property_native("glsl-shaders", saved.shaders or {})
            if saved.scale then mp.set_property("scale", saved.scale) end
            if saved.cscale then mp.set_property("cscale", saved.cscale) end
            if saved.dscale then mp.set_property("dscale", saved.dscale) end
            if saved.deband then mp.set_property("deband", saved.deband) end
            saved = nil
        end
        if not (is_power_event or power_locked) then show_osd("{\\c&H00FFFF&}GPU Upscaler: Disabled {\\c&HFFFFFF&}(Restored Config)") end
        vsr_active = false
    end
    update_status(vsr_active)
end

function user_toggle_vsr() set_state(not vsr_active, false) end

mp.register_script_message("anime-state-broadcast", function(json)
    local data = utils.parse_json(json)
    if not data or data.power_active == nil then return end
    if data.power_active ~= power_locked then
        power_locked = data.power_active
        if power_locked then
            was_active_before_lock = vsr_active
            if vsr_active then set_state(false, true) end
        elseif was_active_before_lock then
            was_active_before_lock = false
            set_state(true, true)
        end
    end
end)

mp.register_script_message("force-evaluate-profile", function() update_status(vsr_active) end)
mp.add_key_binding("V", "toggle-vsr", user_toggle_vsr)
