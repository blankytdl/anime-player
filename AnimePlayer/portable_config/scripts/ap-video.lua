-- AnimePlayer: video helpers for the control-bar buttons
--   ap-autostretch   cycles the build's UltraWide Zoom modes (Fit / Fill / Crop), see below; ap-autostretch-menu opens them as a menu
--                    Both modes first remove black bars that are part of the picture, then
--                      Stretch: the picture fills the whole window (keepaspect=no; slightly distorts when the picture's shape
--                               differs from the window's),
--                      Fill:    zooms until the window is full, no distortion (panscan=1; trims the sides).
--                    The mode stays on for the following files (bars are detected again for each file).
--                    Bar detection: the build's own autocrop.lua (script-binding autocrop/toggle_crop, which also handles hwdec) when it
--                    is installed - so nothing is duplicated; otherwise a small cropdetect of my own.
--   ap-aspect-cycle  Original / 16:9 / 4:3 / 21:9 / 16:10 / 1:1
--   ap-rotate        0 / 90 / 180 / 270
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

-- crop = what the Crop button does after the black bars were cut:  fit = the whole picture stays INSIDE the window (nothing is cut off, default),
--                                                                    zoom = fill the window by zooming (the sides / top are cut off the window)
-- fill = what the Fill button does:  stretch = fills the window, nothing is cut off, the shape is stretched (default) | fit | zoom (old: cuts the sides)
local opts = { limit = 0.094, round = 2, min_gain = 0.02, settle = 3, max_wait = 12, autocrop_wait = 8, crop = 'fit', fill = 'stretch' }
require('mp.options').read_options(opts, 'ap-video')

local DET, CROP = 'apcd', 'apcrop'
local ORDER = { off = 'stretch', stretch = 'fill', fill = 'off' }

local mode = 'off'
local detect = true             -- forced zoom with black-bar detection (Crop button) or without (Fill button)
local saved_view = nil
local timer = nil
local gen = 0
local we_cropped = false        -- the crop was applied by this script (so only then it is removed again)
local crop_by_property = false  -- own fallback: crop set through video-crop (else through a vf filter)

local function label()
    if mode == 'off' then return 'off (original picture)' end
    if detect then return opts.crop == 'zoom' and 'Crop (bars removed, zoomed to fill - sides are cut off)' or 'Crop (bars removed, the whole picture stays inside the window)' end
    if opts.fill == 'zoom' then return 'Fill (zoomed - sides are cut off)' end
    if opts.fill == 'fit' then return 'Fill (fit - the whole picture stays inside the window)' end
    return 'Fill (stretched to the window - nothing is cut off)'
end

local function osd(t) mp.osd_message(t, 2.5) end

local function cfg_file(rel)
    local d = mp.command_native({ 'expand-path', '~~/' })
    return utils.join_path(d, rel)
end

local function have_autocrop()
    return utils.file_info(cfg_file('scripts/autocrop.lua')) ~= nil or utils.file_info(cfg_file('scripts/autocrop')) ~= nil
end

local function current_crop() return mp.get_property('video-crop') or '' end

local function has_vf(label)
    for _, f in ipairs(mp.get_property_native('vf') or {}) do
        if f.label == label then return true end
    end
    return false
end

local function stop_timer()
    if timer then timer:kill() timer = nil end
end

local function remove_own_filters()
    if has_vf(DET) then mp.commandv('vf', 'remove', '@' .. DET) end
    if has_vf(CROP) then mp.commandv('vf', 'remove', '@' .. CROP) end
    if crop_by_property then
        mp.set_property('video-crop', '')
        crop_by_property = false
    end
end

local function save_view()
    if not saved_view then
        saved_view = { keepaspect = mp.get_property_bool('keepaspect', true), panscan = mp.get_property_number('panscan', 0) }
    end
end

local function restore_view()
    if saved_view then
        mp.set_property_bool('keepaspect', saved_view.keepaspect)
        mp.set_property_number('panscan', saved_view.panscan)
        saved_view = nil
    end
end

-- (build 29) The forced zoom / crop must keep the picture inside the window: a manual zoom / pan / alignment left over from before, or a crop
-- rectangle that reaches outside the picture, would push the picture off the window. Both are put right whenever the forced mode is applied.
local function sanitize_view()
    for _, k in ipairs({ 'video-zoom', 'video-pan-x', 'video-pan-y', 'video-align-x', 'video-align-y' }) do
        local v = mp.get_property_number(k)
        if v and v ~= 0 then pcall(mp.set_property_number, k, 0) end
    end
    local un = mp.get_property('video-unscaled')
    if un and un ~= 'no' then pcall(mp.set_property, 'video-unscaled', 'no') end
    local c = mp.get_property('video-crop') or ''
    if c ~= '' then
        local w, h, x, y = c:match('^(%d+)x(%d+)%+(%d+)%+(%d+)$')
        local vp = mp.get_property_native('video-params') or {}
        local vw, vh = tonumber(vp.w), tonumber(vp.h)
        w, h, x, y = tonumber(w), tonumber(h), tonumber(x), tonumber(y)
        if not w or w <= 0 or h <= 0 or (vw and vh and (w > vw or h > vh)) then
            msg.warn('forced crop: crop ' .. c .. ' is outside the picture, removed')
            pcall(mp.set_property, 'video-crop', '')
            crop_by_property = false
        end
    end
end

local function apply_view()
    if mode ~= 'off' then sanitize_view() end
    if mode == 'stretch' then
        mp.set_property_number('panscan', 0)
        mp.set_property_bool('keepaspect', false)
    elseif mode == 'fill' then
        -- nothing may reach outside the window unless zoom was asked for explicitly in ap-video.conf
        local zoom = (detect and opts.crop == 'zoom') or (not detect and opts.fill == 'zoom')
        local stretch = (not detect) and opts.fill == 'stretch'
        mp.set_property_bool('keepaspect', not stretch)
        mp.set_property_number('panscan', zoom and 1 or 0)
    end
end

-- remove a crop that this script made
local function clear_crop()
    stop_timer()
    remove_own_filters()
    if we_cropped and current_crop() ~= '' and have_autocrop() then
        mp.commandv('script-binding', 'autocrop/toggle_crop') -- toggles: with a crop present it removes it
    end
    we_cropped = false
end

-- ---------- bar detection through the build's autocrop.lua ----------
local function crop_with_autocrop(my, done)
    if current_crop() ~= '' then done(false) return end           -- already cropped (autocrop auto mode / by hand): keep it, not ours
    mp.commandv('script-binding', 'autocrop/toggle_crop')         -- no crop yet: this starts the detection
    local waited = 0
    timer = mp.add_periodic_timer(0.5, function()
        if my ~= gen then stop_timer() return end
        waited = waited + 0.5
        if current_crop() ~= '' then stop_timer() done(true) return end
        if waited >= opts.autocrop_wait then stop_timer() done(false) end
    end)
end

-- ---------- fallback: own cropdetect ----------
local function read_meta()
    local m = mp.get_property_native('vf-metadata/' .. DET)
    if type(m) ~= 'table' then return nil end
    local w, h = tonumber(m['lavfi.cropdetect.w']), tonumber(m['lavfi.cropdetect.h'])
    local x, y = tonumber(m['lavfi.cropdetect.x']), tonumber(m['lavfi.cropdetect.y'])
    if w and h and x and y then return w, h, x, y end
    return nil
end

local function crop_with_own(my, done)
    local W, H = mp.get_property_number('width'), mp.get_property_number('height')
    if not W or not H then done(false) return end
    local ok = mp.commandv('vf', 'pre', string.format('@%s:cropdetect=limit=%.4f:round=%d:reset=0', DET, opts.limit, opts.round))
    if not ok then
        osd('Auto stretch: bar detection needs a copy-back decoder (hwdec=d3d11va-copy) or software decoding')
        done(false)
        return
    end
    local last, stable, waited = nil, 0, 0
    local function finish(w, h, x, y)
        stop_timer()
        if has_vf(DET) then mp.commandv('vf', 'remove', '@' .. DET) end
        if w >= W * (1 - opts.min_gain) and h >= H * (1 - opts.min_gain) then done(false) return end
        if mp.set_property('video-crop', string.format('%dx%d+%d+%d', w, h, x, y)) then
            crop_by_property = true
        else
            mp.commandv('vf', 'pre', string.format('@%s:crop=%d:%d:%d:%d', CROP, w, h, x, y)) -- mpv older than 0.39
        end
        done(true)
    end
    timer = mp.add_periodic_timer(0.5, function()
        if my ~= gen then stop_timer() return end
        if mp.get_property_bool('pause') then return end
        waited = waited + 0.5
        local w, h, x, y = read_meta()
        if w then
            local cur = w .. ':' .. h .. ':' .. x .. ':' .. y
            if cur == last then stable = stable + 1 else stable = 0; last = cur end
            if stable >= opts.settle then finish(w, h, x, y) return end
        end
        if waited >= opts.max_wait then
            if w then finish(w, h, x, y) else stop_timer() if has_vf(DET) then mp.commandv('vf', 'remove', '@' .. DET) end done(false) end
        end
    end)
end

-- ---------- the button ----------
local function engage()
    gen = gen + 1
    local my = gen
    stop_timer()
    if not mp.get_property_number('width') then
        osd('Forced zoom: no video picture here')
        return
    end
    if not detect then
        we_cropped = false
        apply_view()
        osd('Zoom: ' .. label())
        return
    end
    local function done(cropped)
        if my ~= gen then return end
        we_cropped = cropped
        apply_view()
        osd('Zoom: ' .. label() .. (cropped and '' or '  (no black bars found)'))
    end
    if have_autocrop() then crop_with_autocrop(my, done) else crop_with_own(my, done) end
end

-- Forced zoom (build 30): the picture is never pushed beyond the window. Crop = black bars cut away, then fitted inside the window; Fill = stretched to the window.
--   Fit  button = off (original picture)
--   Fill button = forced zoom
--   Crop button = forced zoom after the black bars that are part of the picture were cut away
-- The mode stays on for the following files.
local function zoom_set(which)
    if which == 'off' then
        gen = gen + 1
        mode = 'off'
        clear_crop()
        restore_view()
        osd('Zoom: off (original picture)')
        return
    end
    if mode == 'off' then save_view() end
    clear_crop()
    mode = 'fill'
    detect = (which == 'crop')
    apply_view()          -- fill the window right away; the bar detection (Crop) only trims further
    engage()
end

mp.register_script_message('ap-zoom-off', function() zoom_set('off') end)
mp.register_script_message('ap-zoom-fill', function() zoom_set('fill') end)
mp.register_script_message('ap-zoom-crop', function() zoom_set('crop') end)
mp.register_script_message('ap-zoom-forced', function() zoom_set(mode == 'fill' and 'off' or 'crop') end)   -- key Ctrl+Alt+S: toggle
mp.register_script_message('ap-autostretch', function() zoom_set(mode == 'fill' and 'off' or 'crop') end)

mp.register_script_message('ap-autostretch-menu', function()
    local json = utils.format_json({
        type = 'ap_zoom', title = 'Zoom (forced)',
        items = {
            { title = 'Crop - black bars cut away, whole picture stays in the window', icon = 'crop', active = (mode == 'fill' and detect) or nil, value = { 'script-message', 'ap-zoom-crop' } },
            { title = 'Fill - stretched to the window (nothing cut off)', icon = 'zoom_out_map', active = (mode == 'fill' and not detect) or nil, value = { 'script-message', 'ap-zoom-fill' } },
            { title = 'Fit - original picture', icon = 'fit_screen', active = (mode == 'off') or nil, value = { 'script-message', 'ap-zoom-off' } },
        },
    })
    mp.commandv('script-message-to', 'uosc', 'open-menu', json)
end)

mp.register_event('start-file', function()
    gen = gen + 1
    stop_timer()
    we_cropped = false      -- a new file starts without our crop (video-crop set by autocrop is file-local)
    crop_by_property = false
end)

mp.register_event('file-loaded', function()
    if mode ~= 'off' then mp.add_timeout(0.6, engage) end
end)

-- ---------- aspect ratio ----------
local ASPECTS = { { '-1', 'Original' }, { '16:9', '16:9' }, { '4:3', '4:3' }, { '21:9', '21:9' }, { '16:10', '16:10' }, { '1:1', '1:1' } }

local function ratio(s)
    local a, b = s:match('^(%d+):(%d+)$')
    if a then return tonumber(a) / tonumber(b) end
    return tonumber(s)
end

mp.register_script_message('ap-aspect-cycle', function()
    local cur = tonumber(mp.get_property('video-aspect-override')) or -1
    local idx = 1
    for i, a in ipairs(ASPECTS) do
        local r = ratio(a[1])
        if (r <= 0 and cur <= 0) or (r > 0 and cur > 0 and math.abs(r - cur) < 0.01) then idx = i break end
    end
    local nxt = ASPECTS[idx % #ASPECTS + 1]
    mp.set_property('video-aspect-override', nxt[1])
    osd('Aspect ratio: ' .. nxt[2])
end)

-- ---------- rotate ----------
mp.register_script_message('ap-rotate', function()
    local r = ((mp.get_property_number('video-rotate', 0) or 0) + 90) % 360
    mp.set_property_number('video-rotate', r)
    osd('Rotate: ' .. r .. ' deg')
end)
