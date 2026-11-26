-- AnimePlayer: compare shaders OFF with shaders ON (build 27).
--   script-message-to ap_compare split   : frozen frame. LEFT = shaders off, RIGHT = shaders on (live). Move the mouse (or LEFT/RIGHT keys)
--                                          to move the divider. Esc / Enter / right-click / unpausing / seeking ends it.
--   script-message-to ap_compare flip    : switches the shaders off / on right now, with a label on screen. Run it again to switch back.
-- Safety: the shader list is kept in `stash` while it is switched off and is put back on EVERY way out (stop, flip back, new file,
-- end of file, player shutdown, any error), so a compare can never leave the shaders off.
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

local OV_ID = 43                 -- overlay-add id (thumbfast uses 42)
local ELEMENTS = 'timeline,controls,volume,top_bar,idle_indicator,audio_indicator,buffering_indicator,pause_indicator'
local stash                     -- shader list while switched off (nil = shaders are in their normal state)
local flip_label                -- osd-overlay for the "SHADERS OFF" label
local split                     -- state of the split compare, nil when not active
local tmp = (os.getenv('TEMP') or os.getenv('TMP') or mp.command_native({ 'expand-path', '~~/cache/' }) or '.')

local function get_shaders() local s = mp.get_property_native('glsl-shaders'); return type(s) == 'table' and s or {} end
local function set_flag(v) pcall(mp.set_property, 'user-data/ap/compare-active', v and 'yes' or 'no') end

local function restore_shaders()
    if stash then
        local list = stash
        stash = nil
        -- if something else (the Anime Build Options) loaded shaders meanwhile, THEIR choice stays
        if #get_shaders() == 0 then pcall(mp.set_property_native, 'glsl-shaders', list) end
    end
end

local function show_label(text)
    if not text then if flip_label then flip_label:remove(); flip_label = nil end; return end
    flip_label = flip_label or mp.create_osd_overlay('ass-events')
    flip_label.res_x, flip_label.res_y = 1280, 720
    flip_label.data = '{\\an7\\pos(24,24)\\fs34\\bord3\\1c&HFFFFFF&\\3c&H000000&\\b1}' .. text
    flip_label:update()
end

-- ---------- flip ----------
local function flip()
    if split then return end
    if stash then
        restore_shaders(); show_label(nil); set_flag(false)
        mp.osd_message('SHADERS ON', 1.5)
        return
    end
    local sh = get_shaders()
    if #sh == 0 then mp.osd_message('No shaders are active - nothing to compare.\n(Turn the anime shaders on first.)', 3); return end
    stash = sh
    local ok = pcall(mp.set_property_native, 'glsl-shaders', {})
    if not ok then stash = nil; return end
    set_flag(true)
    show_label('SHADERS OFF  (original)')
end

-- ---------- split ----------
local function end_split(silent)
    local s = split
    if not s then return end
    split = nil
    if s.timer then s.timer:kill() end
    if s.mouse_timer then s.mouse_timer:kill() end
    pcall(mp.unobserve_property, s.on_mouse)
    pcall(mp.unobserve_property, s.on_pause)
    pcall(mp.unobserve_property, s.on_dims)
    for _, k in ipairs({ 'ap-cmp-esc', 'ap-cmp-enter', 'ap-cmp-rclick', 'ap-cmp-left', 'ap-cmp-right' }) do mp.remove_key_binding(k) end
    mp.commandv('overlay-remove', tostring(OV_ID))
    if s.ass then s.ass:remove() end
    restore_shaders()
    mp.commandv('script-message-to', 'uosc', 'disable-elements', mp.get_script_name(), '')
    if s.file then os.remove(s.file) end
    set_flag(false)
    if not s.was_paused then pcall(mp.set_property_bool, 'pause', false) end
    if not silent then mp.osd_message('Compare ended - shaders ON', 1.5) end
end

local function draw(s)
    local cw = math.max(2, math.min(s.w - 2, math.floor(s.w * s.pos)))
    mp.commandv('overlay-add', tostring(OV_ID), '0', '0', s.file, '0', 'bgra', tostring(cw), tostring(s.h), tostring(s.stride))
    local fs = math.max(18, math.floor(s.h / 28))
    s.ass.res_x, s.ass.res_y = s.w, s.h
    s.ass.data = string.format('{\\pos(%d,0)\\p1\\bord0\\1c&HFFFFFF&}m 0 0 l 3 0 l 3 %d l 0 %d{\\p0}\n', cw - 1, s.h, s.h)
        .. string.format('{\\an7\\pos(%d,%d)\\fs%d\\bord3\\1c&HFFFFFF&\\3c&H000000&\\b1}SHADERS OFF\n', 16, 16, fs)
        .. string.format('{\\an9\\pos(%d,%d)\\fs%d\\bord3\\1c&HFFFFFF&\\3c&H000000&\\b1}SHADERS ON\n', s.w - 16, 16, fs)
    s.ass:update()
end

local function fail(why)
    msg.warn('compare: ' .. why)
    end_split(true)
    mp.osd_message('Split compare is not possible here (' .. why .. ').\nUse "Flip" instead.', 4)
end

local function start_split()
    if split or stash then return end
    local sh = get_shaders()
    if #sh == 0 then mp.osd_message('No shaders are active - nothing to compare.\n(Turn the anime shaders on first.)', 3); return end
    if mp.get_property_native('osd-dimensions') == nil then return fail('no window size') end
    local s = { was_paused = mp.get_property_bool('pause', false), pos = 0.5, saved = sh }
    split = s
    set_flag(true)
    mp.commandv('script-message-to', 'uosc', 'close-menu')
    mp.commandv('script-message-to', 'uosc', 'disable-elements', mp.get_script_name(), ELEMENTS)
    pcall(mp.set_property_bool, 'pause', true)
    stash = sh
    pcall(mp.set_property_native, 'glsl-shaders', {})
    mp.osd_message('Preparing compare...', 2)
    -- 1) wait for the shader-less frame to be drawn, then capture the window
    s.timer = mp.add_timeout(0.6, function()
        if split ~= s then return end
        local shot = mp.command_native({ 'screenshot-raw', 'window', 'bgra' })
        if type(shot) ~= 'table' then shot = mp.command_native({ name = 'screenshot-raw', flags = 'window' }) end
        if type(shot) ~= 'table' or not shot.data or not shot.w or not shot.h then return fail('screenshot not available') end
        local data = shot.data
        if shot.format == 'bgr0' then data = data:gsub('(...).', '%1\255')              -- make the 4th byte opaque
        elseif shot.format ~= 'bgra' then return fail('unexpected format ' .. tostring(shot.format)) end
        local d = mp.get_property_native('osd-dimensions') or {}
        if d.w and (math.abs(d.w - shot.w) > 1 or math.abs(d.h - shot.h) > 1) then return fail('window scaling differs') end
        s.w, s.h, s.stride = shot.w, shot.h, shot.stride or shot.w * 4
        s.file = utils.join_path(tmp, string.format('ap-compare-%d.bgra', os.time() % 100000))
        local f = io.open(s.file, 'wb')
        if not f then return fail('cannot write temp file') end
        f:write(data); f:close()
        -- 2) shaders back on; the live (right) side shows them
        restore_shaders()
        s.timer = mp.add_timeout(0.6, function()
            if split ~= s then return end
            s.ass = mp.create_osd_overlay('ass-events')
            draw(s)
            mp.osd_message('Move the mouse to compare  -  Esc to finish', 3)
            local function move_to(p) s.pos = math.max(0.02, math.min(0.98, p)); draw(s) end
            s.on_mouse = function(_, m)
                if type(m) ~= 'table' or not m.x or s.mouse_timer or not m.hover then return end
                local x = m.x
                s.mouse_timer = mp.add_timeout(0.04, function() s.mouse_timer = nil; if split == s then move_to(x / s.w) end end)
            end
            s.on_pause = function(_, p) if p == false then end_split() end end        -- unpausing = back to normal
            s.on_dims = function(_, d) if split == s and type(d) == 'table' and d.w and (math.abs(d.w - s.w) > 1 or math.abs(d.h - s.h) > 1) then end_split() end end
            mp.observe_property('mouse-pos', 'native', s.on_mouse)
            mp.observe_property('pause', 'bool', s.on_pause)
            mp.observe_property('osd-dimensions', 'native', s.on_dims)
            mp.add_forced_key_binding('ESC', 'ap-cmp-esc', function() end_split() end)
            mp.add_forced_key_binding('ENTER', 'ap-cmp-enter', function() end_split() end)
            mp.add_forced_key_binding('MBTN_RIGHT', 'ap-cmp-rclick', function() end_split() end)
            mp.add_forced_key_binding('LEFT', 'ap-cmp-left', function() move_to(s.pos - 0.05) end, { repeatable = true })
            mp.add_forced_key_binding('RIGHT', 'ap-cmp-right', function() move_to(s.pos + 0.05) end, { repeatable = true })
        end)
    end)
end

mp.register_script_message('flip', flip)
mp.register_script_message('split', start_split)
mp.register_script_message('stop', function() end_split(); if stash then restore_shaders(); show_label(nil); set_flag(false) end end)
-- default keys (change them in input.conf if you like): Ctrl+Alt+c = split view, Ctrl+Alt+x = flip
mp.add_key_binding('Ctrl+Alt+c', 'split', start_split)
mp.add_key_binding('Ctrl+Alt+x', 'flip', flip)

-- every way out puts the shaders back
mp.register_event('end-file', function() end_split(true); if stash then restore_shaders(); show_label(nil); set_flag(false) end end)
mp.register_event('seek', function() if split and split.w then end_split() end end)
mp.register_event('shutdown', function() end_split(true); restore_shaders() end)
-- if the Anime Build Options (or a forced mode) load shaders again while they are switched off here, THEIR choice wins and the label goes away
mp.observe_property('glsl-shaders', 'native', function(_, v)
    if stash and not split and type(v) == 'table' and #v > 0 then
        stash = nil; show_label(nil); set_flag(false)
    end
end)
set_flag(false)
