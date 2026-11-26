-- AnimePlayer: user profiles (build 27).
-- A profile = your own NAME + a SNAPSHOT of the settings that are active right now (what the Anime Build Options did: shaders, scalers,
-- deband, HDR, ...) + optionally a base mpv preset + the kind of video it is for (Type: Any/Anime/Live action, Resolution: Any/SD/HD/FHD).
--   Anime Build Options > Profiles...  >  New profile from the CURRENT settings
-- When a video starts and Auto-apply is on, the most specific matching profile is applied (after the build's own detection).
-- After applying, every saved setting is read back from mpv and compared, so you see whether it REALLY applied.
-- "Check active settings" lists the shaders that are really loaded and warns about shader files that are missing.
-- Saved in ap-profiles.json next to mpv.conf.
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

local FILE = utils.join_path(mp.command_native({ 'expand-path', '~~/' }), 'ap-profiles.json')
local MENU_TYPE = 'ap_profiles'
local data = { auto = true, profiles = {} }
local view = { page = 'list' }       -- page: list | profile | preset | type | res
local active_name

-- the settings the Anime Build Options end up changing in mpv (only those this mpv knows are saved)
local SNAP = {
    'glsl-shaders', 'glsl-shader-opts',
    'scale', 'cscale', 'dscale', 'tscale', 'scale-antiring', 'cscale-antiring', 'dscale-antiring',
    'sigmoid-upscaling', 'correct-downscaling', 'linear-downscaling',
    'deband', 'deband-iterations', 'deband-threshold', 'deband-range', 'deband-grain', 'dither-depth',
    'interpolation', 'video-sync', 'hwdec',
    'target-trc', 'target-prim', 'tone-mapping', 'hdr-compute-peak', 'gamut-mapping-mode', 'target-colorspace-hint',
    'af', 'audio-channels', 'audio-normalize-downmix',
}

local function deep_equal(a, b)
    if type(a) ~= type(b) then return false end
    if type(a) ~= 'table' then return a == b end
    for k, v in pairs(a) do if not deep_equal(v, b[k]) then return false end end
    for k in pairs(b) do if a[k] == nil then return false end end
    return true
end

local function snapshot()
    local s, n = {}, 0
    for _, name in ipairs(SNAP) do
        local v = mp.get_property_native(name)
        if v ~= nil then s[name] = v; n = n + 1 end
    end
    return s, n
end

local function load()
    local f = io.open(FILE, 'rb'); if not f then return end
    local t = utils.parse_json(f:read('*a') or ''); f:close()
    if type(t) == 'table' and type(t.profiles) == 'table' then
        data.auto = t.auto ~= false
        data.profiles = {}
        for _, p in ipairs(t.profiles) do
            if type(p) == 'table' and type(p.name) == 'string' and p.name ~= '' then
                data.profiles[#data.profiles + 1] = { name = p.name, preset = tostring(p.preset or ''), type = p.type or 'any', res = p.res or 'any',
                                                      snap = type(p.snap) == 'table' and p.snap or nil, saved = p.saved, auto_shaders = p.auto_shaders == true }
            end
        end
    end
end
local function save()
    local tmp = FILE .. '.part'
    local f = io.open(tmp, 'wb'); if not f then msg.warn('cannot write ' .. tmp); return end
    f:write(utils.format_json({ auto = data.auto, profiles = data.profiles })); f:close()
    os.remove(FILE); os.rename(tmp, FILE)
end

local TYPES = { any = 'Any video', anime = 'Anime', live = 'Live action' }
local RESES = { any = 'Any resolution', sd = 'SD (up to 576p)', hd = 'HD (up to 800p)', fhd = 'Full HD and up' }

local function presets()
    local out = {}
    for _, p in ipairs(mp.get_property_native('profile-list') or {}) do
        if type(p) == 'table' and p.name and p.name ~= 'default' and not p.name:find('^protocol%.') and not p.name:find('^extension%.') then out[#out + 1] = p.name end
    end
    table.sort(out)
    return out
end

local function looks_like_anime()
    local p = (mp.get_property('path') or ''):lower()
    if p:find('%[subsplease%]') or p:find('%[erai%-raws%]') or p:find('%[horriblesubs%]') or p:find('%[judas%]') or p:find('%[ember%]')
        or p:find('anime') or p:find('%[%x%x%x%x%x%x%x%x%]') then return true end
    local lang = mp.get_property('current-tracks/audio/lang') or ''
    return lang == 'jpn' or lang == 'ja'
end
local function res_class()
    local h = mp.get_property_number('height', 0)
    if h <= 0 then return nil end
    if h <= 576 then return 'sd' elseif h <= 800 then return 'hd' else return 'fhd' end
end

local function usable_profile(p) return p.preset ~= '' or (p.snap and next(p.snap) ~= nil) end

local function best_match()
    local ty, rc = looks_like_anime() and 'anime' or 'live', res_class()
    local best, score
    for _, p in ipairs(data.profiles) do
        if usable_profile(p) and (p.type == 'any' or p.type == ty) and (p.res == 'any' or p.res == rc) then
            local s = (p.type ~= 'any' and 1 or 0) + (p.res ~= 'any' and 1 or 0)
            if not best or s > score then best, score = p, s end
        end
    end
    return best
end

local function base(path) return (tostring(path):gsub('\\', '/'):match('[^/]+$')) or tostring(path) end

-- apply: base preset first, then every saved setting; then READ BACK and compare
-- auto = applied by itself when a video starts. The Anime Build's own modes (Auto / FORCED ON / FORCED OFF, Fidelity, Anime4K) own the shaders:
-- an automatic apply leaves glsl-shaders alone unless the profile has "Auto-apply shaders" ON. "Use now" always applies everything.
local SHADER_KEYS = { ['glsl-shaders'] = true, ['glsl-shader-opts'] = true }
local function apply(p, quiet, auto)
    if not p or not usable_profile(p) then return end
    if mp.get_property('user-data/ap/compare-active') == 'yes' then if not quiet then mp.osd_message('Finish the shader compare first (Esc)', 2) end; return end
    if p.preset ~= '' then
        local ok, err = mp.commandv('apply-profile', p.preset)
        if not ok then msg.warn('apply-profile failed: ' .. p.preset .. ' (' .. tostring(err) .. ')') end
    end
    local changed, failed = 0, {}
    if p.snap then
        for name, want in pairs(p.snap) do
            local cur = mp.get_property_native(name)
            if auto and not p.auto_shaders and SHADER_KEYS[name] then cur = nil end
            if cur ~= nil and not deep_equal(cur, want) then
                local ok = pcall(mp.set_property_native, name, want)
                if ok then changed = changed + 1 else failed[#failed + 1] = name end
            end
        end
        -- read back: did mpv really take it?
        for name, want in pairs(p.snap) do
            local cur = mp.get_property_native(name)
            if auto and not p.auto_shaders and SHADER_KEYS[name] then cur = nil end
            if cur ~= nil and not deep_equal(cur, want) then
                local seen = false
                for _, f in ipairs(failed) do if f == name then seen = true end end
                if not seen then failed[#failed + 1] = name end
            end
        end
    end
    active_name = p.name
    if failed[1] then msg.warn('profile "' .. p.name .. '": not applied: ' .. table.concat(failed, ', ')) end
    if not quiet then
        local line = 'Profile: ' .. p.name .. (p.preset ~= '' and ('  [' .. p.preset .. ']') or '')
        if p.snap then line = line .. '\n' .. changed .. ' settings changed' .. (failed[1] and ('\nNOT applied: ' .. table.concat(failed, ', ')) or '  -  all applied') end
        mp.osd_message(line, 3)
    end
end

-- what is really active: shaders (with a missing-file check) and the main scalers
local function check_active()
    local lines = {}
    local sh = mp.get_property_native('glsl-shaders') or {}
    if #sh == 0 then lines[#lines + 1] = 'Shaders: none active (anime shaders are OFF)'
    else
        lines[#lines + 1] = 'Shaders active: ' .. #sh
        for _, s in ipairs(sh) do
            local path = mp.command_native({ 'expand-path', s }) or s
            local fi = utils.file_info(path)
            lines[#lines + 1] = (fi and fi.is_file and '  ok   ' or '  MISSING  ') .. base(s)
        end
    end
    lines[#lines + 1] = string.format('scale=%s  cscale=%s  dscale=%s', tostring(mp.get_property('scale')), tostring(mp.get_property('cscale')), tostring(mp.get_property('dscale')))
    lines[#lines + 1] = string.format('deband=%s  hwdec=%s  vo=%s', tostring(mp.get_property('deband')), tostring(mp.get_property('hwdec-current') or mp.get_property('hwdec')), tostring(mp.get_property('current-vo')))
    if active_name then lines[#lines + 1] = 'Profile in use: ' .. active_name end
    mp.osd_message(table.concat(lines, '\n'), 9)
    for _, l in ipairs(lines) do msg.info(l) end
end

local function describe(p)
    local lines = { 'Profile: ' .. p.name }
    if p.preset ~= '' then lines[#lines + 1] = 'Base preset: ' .. p.preset end
    if p.snap then
        local sh = p.snap['glsl-shaders']
        lines[#lines + 1] = (type(sh) == 'table' and #sh > 0) and ('Shaders: ' .. (function() local n = {}; for _, s in ipairs(sh) do n[#n + 1] = base(s) end; return table.concat(n, ', ') end)()) or 'Shaders: none'
        lines[#lines + 1] = string.format('scale=%s  cscale=%s  dscale=%s  deband=%s', tostring(p.snap.scale), tostring(p.snap.cscale), tostring(p.snap.dscale), tostring(p.snap.deband))
    else lines[#lines + 1] = 'No saved settings yet - use "Save current settings into this profile".' end
    mp.osd_message(table.concat(lines, '\n'), 8)
end

local function find(name) for i, p in ipairs(data.profiles) do if p.name == name then return p, i end end end
local function unique_name(base_name)
    local n, name = 1, base_name
    while find(name) do n = n + 1; name = base_name .. ' ' .. n end
    return name
end
local function count(t) local n = 0; for _ in pairs(t or {}) do n = n + 1 end; return n end

-- ---------- menu ----------
local function cmd(...) return { 'script-message-to', mp.get_script_name(), ... } end

local function build_menu()
    local items, title = {}, 'Profiles'
    if view.page == 'list' then
        items[#items + 1] = { title = 'New profile from the CURRENT settings...', value = cmd('new-from-current'), bold = true }
        items[#items + 1] = { title = 'Check active settings (shaders really loaded?)', value = cmd('check') }
        items[#items + 1] = { title = 'Compare shaders: split view (left OFF, right ON)', value = { 'script-message-to', 'ap_compare', 'split' } }
        items[#items + 1] = { title = 'Compare shaders: flip OFF / ON', value = { 'script-message-to', 'ap_compare', 'flip' } }
        items[#items + 1] = { title = 'Auto-apply on new videos: ' .. (data.auto and 'ON' or 'OFF'), value = cmd('toggle-auto') }
        for _, p in ipairs(data.profiles) do
            local mark = (p.name == active_name) and '  (in use)' or ''
            local what = p.snap and (count(p.snap) .. ' settings') or (p.preset ~= '' and p.preset or 'empty')
            items[#items + 1] = { title = p.name .. mark, hint = what .. ' | ' .. TYPES[p.type] .. ' | ' .. RESES[p.res], value = cmd('edit', p.name) }
        end
        if #data.profiles == 0 then items[#items + 1] = { title = 'No profiles yet. Set up the Anime Build Options the way you like, then create one here.', selectable = false, muted = true } end
    else
        local p = find(view.name)
        if not p then view = { page = 'list' }; return build_menu() end
        title = 'Profile: ' .. p.name
        if view.page == 'profile' then
            items[#items + 1] = { title = 'Use now', value = cmd('use', p.name) }
            items[#items + 1] = { title = 'Save current settings into this profile', hint = p.snap and (count(p.snap) .. ' saved') or 'nothing saved yet', value = cmd('resave') }
            items[#items + 1] = { title = 'Auto-apply shaders: ' .. (p.auto_shaders and 'ON' or 'OFF'), hint = p.auto_shaders and 'replaces the Anime Build shader choice' or 'Anime Build mode decides', value = cmd('toggle-shaders') }
            items[#items + 1] = { title = 'Show what it contains', value = cmd('describe') }
            items[#items + 1] = { title = 'For videos of type', hint = TYPES[p.type], value = cmd('page', 'type') }
            items[#items + 1] = { title = 'For resolution', hint = RESES[p.res], value = cmd('page', 'res') }
            items[#items + 1] = { title = 'Base preset (optional)', hint = p.preset ~= '' and p.preset or 'none', value = cmd('page', 'preset') }
            items[#items + 1] = { title = 'Rename...', value = cmd('rename') }
            items[#items + 1] = { title = 'Delete', value = cmd('delete') }
            items[#items + 1] = { title = 'Back', value = cmd('page', 'list') }
        else
            local list, key = {}, view.page
            if key == 'preset' then list[1] = { '', 'None' }; for _, n in ipairs(presets()) do list[#list + 1] = { n, n } end
            elseif key == 'type' then for _, k in ipairs({ 'any', 'anime', 'live' }) do list[#list + 1] = { k, TYPES[k] } end
            else for _, k in ipairs({ 'any', 'sd', 'hd', 'fhd' }) do list[#list + 1] = { k, RESES[k] } end end
            for _, e in ipairs(list) do
                items[#items + 1] = { title = e[2], hint = (p[key] == e[1]) and 'selected' or nil, value = cmd('set', key, e[1]) }
            end
            items[#items + 1] = { title = 'Back', value = cmd('page', 'profile') }
        end
    end
    return { type = MENU_TYPE, title = title, keep_open = true, items = items }
end

local function refresh() mp.commandv('script-message-to', 'uosc', 'update-menu', utils.format_json(build_menu())) end
local function reopen() mp.commandv('script-message-to', 'uosc', 'open-menu', utils.format_json(build_menu())) end
local function open() view = { page = 'list' }; reopen() end

local function ask(prompt, default, cb)
    local ok, input = pcall(require, 'mp.input')
    if ok and input and input.get then
        input.get({ prompt = prompt .. ' ', default_text = default or '', submit = function(t) input.terminate(); cb(t) end })
    else
        cb(nil)   -- no text input in this mpv: caller falls back to an automatic name
    end
end
local function clean(t) t = t and t:gsub('^%s+', ''):gsub('%s+$', '') or ''; return t end

mp.register_script_message('ap-profiles-open', open)
mp.register_script_message('open', open)
mp.register_script_message('check', check_active)
mp.register_script_message('toggle-auto', function() data.auto = not data.auto; save(); refresh() end)
mp.register_script_message('new-from-current', function()
    if mp.get_property('user-data/ap/compare-active') == 'yes' then mp.osd_message('Finish the shader compare first (Esc)', 2); return end
    local snap, n = snapshot()      -- taken NOW, before the text box opens
    ask('Profile name:', '', function(t)
        local name = unique_name(clean(t) ~= '' and clean(t) or 'Profile')
        data.profiles[#data.profiles + 1] = { name = name, preset = '', type = 'any', res = 'any', snap = snap, saved = os.date('%Y-%m-%d %H:%M') }
        save(); view = { page = 'profile', name = name }
        mp.osd_message('Saved ' .. n .. ' current settings as "' .. name .. '"', 3)
        reopen()
    end)
end)
mp.register_script_message('resave', function()
    local p = find(view.name); if not p then return end
    if mp.get_property('user-data/ap/compare-active') == 'yes' then mp.osd_message('Finish the shader compare first (Esc)', 2); return end
    local snap, n = snapshot()
    p.snap, p.saved = snap, os.date('%Y-%m-%d %H:%M'); save()
    mp.osd_message('Saved ' .. n .. ' current settings into "' .. p.name .. '"', 3)
    refresh()
end)
mp.register_script_message('toggle-shaders', function() local p = find(view.name); if not p then return end; p.auto_shaders = not p.auto_shaders; save(); refresh() end)
mp.register_script_message('describe', function() local p = find(view.name); if p then describe(p) end end)
mp.register_script_message('edit', function(name) view = { page = 'profile', name = name }; refresh() end)
mp.register_script_message('page', function(page) view.page = page; refresh() end)
mp.register_script_message('set', function(key, value)
    local p = find(view.name); if not p then return end
    p[key] = value; save(); view.page = 'profile'; refresh()
end)
mp.register_script_message('use', function(name) apply(find(name)); refresh() end)
mp.register_script_message('rename', function()
    local p = find(view.name); if not p then return end
    ask('New name:', p.name, function(t)
        t = clean(t)
        if t ~= '' and t ~= p.name and not find(t) then if active_name == p.name then active_name = t end; p.name = t; view.name = t; save() end
        reopen()
    end)
end)
mp.register_script_message('delete', function()
    local _, i = find(view.name); if i then table.remove(data.profiles, i); save() end
    view = { page = 'list' }; refresh()
end)

-- auto-apply: after the build's own detection (anime-detect / anime_profile_controller) has run for this file
local timer
mp.register_event('file-loaded', function()
    active_name = nil
    if timer then timer:kill() end
    if not data.auto or #data.profiles == 0 then return end
    timer = mp.add_timeout(0.6, function()
        local p = best_match()
        if p then apply(p, false, true) end
    end)
end)

-- diagnostics: are the menu entries in the build's controller script? (written to ap-menu-check.log; shows why an entry might be missing)
do
    local cfg = mp.command_native({ 'expand-path', '~~/' })
    local f = io.open(utils.join_path(cfg, 'scripts/anime_profile_controller.lua'), 'rb')
    local out = {}
    if f then
        local t = f:read('*a') or ''; f:close()
        for _, m in ipairs({ '[AnimePlayer: profiles menu v2]', '[AnimePlayer: compare menu v2]', '[AnimePlayer: anime menu stays open]' }) do
            out[#out + 1] = (t:find(m, 1, true) and 'present  ' or 'MISSING  ') .. m
        end
    else out[1] = 'scripts/anime_profile_controller.lua not found in ' .. cfg end
    local um = io.open(utils.join_path(cfg, 'scripts/uosc/main.lua'), 'rb')
    if um then
        local t = um:read('*a') or ''; um:close()
        for _, m in ipairs({ '[AP27: playlist key opens the Queue tab]', '[AP30: playlist tools]', '[AP7: shuffle in the playlist menu]', '[AnimePlayer: playlist tabs]' }) do
            out[#out + 1] = (t:find(m, 1, true) and 'present  ' or 'MISSING  ') .. m .. '  (uosc/main.lua)'
        end
    else out[#out + 1] = 'scripts/uosc/main.lua not found' end
    local lf = io.open(utils.join_path(cfg, 'ap-menu-check.log'), 'wb')
    if lf then lf:write(os.date('%Y-%m-%d %H:%M:%S\n'), table.concat(out, '\n'), '\n'); lf:close() end
    for _, l in ipairs(out) do msg.info(l) end
end

load()
