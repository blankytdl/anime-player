-- AnimePlayer: user profiles (build 26).
-- A profile = your own NAME + a PRESET (any mpv profile from mpv.conf: anime-hd, live-sd, lite, ... as listed by mpv itself)
--             + the kind of video it is for: Type (Any / Anime / Live action) and Resolution (Any / SD / HD / FHD).
-- When a video starts and Auto-apply is on, the most specific matching profile's preset is applied (after the build's own detection).
-- Menu: Anime Build Options > Profiles...   (or script-message ap-profiles-open). Saved in ap-profiles.json next to mpv.conf.
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

local FILE = utils.join_path(mp.command_native({ 'expand-path', '~~/' }), 'ap-profiles.json')
local MENU_TYPE = 'ap_profiles'
local data = { auto = true, profiles = {} }   -- profiles: { name=, preset=, type='any|anime|live', res='any|sd|hd|fhd' }
local view = { page = 'list' }                  -- page: list | profile | preset | type | res
local active_name

local function load()
    local f = io.open(FILE, 'rb'); if not f then return end
    local t = utils.parse_json(f:read('*a') or ''); f:close()
    if type(t) == 'table' and type(t.profiles) == 'table' then
        data.auto = t.auto ~= false
        data.profiles = {}
        for _, p in ipairs(t.profiles) do
            if type(p) == 'table' and type(p.name) == 'string' and p.name ~= '' then
                data.profiles[#data.profiles + 1] = { name = p.name, preset = tostring(p.preset or ''), type = p.type or 'any', res = p.res or 'any' }
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

local function best_match()
    local ty, rc = looks_like_anime() and 'anime' or 'live', res_class()
    local best, score
    for _, p in ipairs(data.profiles) do
        if p.preset ~= '' and (p.type == 'any' or p.type == ty) and (p.res == 'any' or p.res == rc) then
            local s = (p.type ~= 'any' and 1 or 0) + (p.res ~= 'any' and 1 or 0)
            if not best or s > score then best, score = p, s end
        end
    end
    return best
end

local function apply(p, quiet)
    if not p or p.preset == '' then return end
    local ok, err = mp.commandv('apply-profile', p.preset)
    if not ok then msg.warn('apply-profile failed: ' .. p.preset .. ' (' .. tostring(err) .. ')') end
    active_name = p.name
    if not quiet then mp.osd_message('Profile: ' .. p.name .. '  [' .. p.preset .. ']', 2) end
end

local function find(name) for i, p in ipairs(data.profiles) do if p.name == name then return p, i end end end
local function unique_name(base)
    local n, name = 1, base
    while find(name) do n = n + 1; name = base .. ' ' .. n end
    return name
end

-- ---------- menu ----------
local function cmd(...) return { 'script-message-to', mp.get_script_name(), ... } end

local function build_menu()
    local items, title = {}, 'Profiles'
    if view.page == 'list' then
        items[#items + 1] = { title = 'Auto-apply on new videos: ' .. (data.auto and 'ON' or 'OFF'), icon = 'autorenew', value = cmd('toggle-auto') }
        items[#items + 1] = { title = 'New profile...', icon = 'add', value = cmd('new') }
        for _, p in ipairs(data.profiles) do
            local mark = (p.name == active_name) and '  (in use)' or ''
            items[#items + 1] = { title = p.name .. mark, hint = (p.preset ~= '' and p.preset or 'no preset') .. ' | ' .. TYPES[p.type] .. ' | ' .. RESES[p.res],
                icon = 'bookmark', value = cmd('edit', p.name) }
        end
        if #data.profiles == 0 then items[#items + 1] = { title = 'No profiles yet. Create one, pick a preset and the kind of video.', icon = 'info', selectable = false, muted = true } end
    else
        local p = find(view.name)
        if not p then view = { page = 'list' }; return build_menu() end
        title = 'Profile: ' .. p.name
        if view.page == 'profile' then
            items[#items + 1] = { title = 'Use now', icon = 'play_arrow', value = cmd('use', p.name) }
            items[#items + 1] = { title = 'Preset', hint = p.preset ~= '' and p.preset or 'choose...', icon = 'tune', value = cmd('page', 'preset') }
            items[#items + 1] = { title = 'For videos of type', hint = TYPES[p.type], icon = 'movie', value = cmd('page', 'type') }
            items[#items + 1] = { title = 'For resolution', hint = RESES[p.res], icon = 'aspect_ratio', value = cmd('page', 'res') }
            items[#items + 1] = { title = 'Rename...', icon = 'edit', value = cmd('rename') }
            items[#items + 1] = { title = 'Delete', icon = 'delete', value = cmd('delete') }
            items[#items + 1] = { title = 'Back', icon = 'arrow_back', value = cmd('page', 'list') }
        else
            local list, key = {}, view.page
            if key == 'preset' then for _, n in ipairs(presets()) do list[#list + 1] = { n, n } end
            elseif key == 'type' then for _, k in ipairs({ 'any', 'anime', 'live' }) do list[#list + 1] = { k, TYPES[k] } end
            else for _, k in ipairs({ 'any', 'sd', 'hd', 'fhd' }) do list[#list + 1] = { k, RESES[k] } end end
            for _, e in ipairs(list) do
                items[#items + 1] = { title = e[2], icon = (p[key] == e[1]) and 'check' or 'radio_button_unchecked', value = cmd('set', key, e[1]) }
            end
            items[#items + 1] = { title = 'Back', icon = 'arrow_back', value = cmd('page', 'profile') }
        end
    end
    return { type = MENU_TYPE, title = title, keep_open = true, items = items }
end


local function refresh() mp.commandv('script-message-to', 'uosc', 'update-menu', utils.format_json(build_menu())) end
local function open() view = { page = 'list' }; mp.commandv('script-message-to', 'uosc', 'open-menu', utils.format_json(build_menu())) end

local function ask(prompt, default, cb)
    local ok, input = pcall(require, 'mp.input')
    if ok and input and input.get then
        input.get({ prompt = prompt .. ' ', default_text = default or '', submit = function(t) input.terminate(); cb(t) end })
    else
        cb(nil)   -- no text input in this mpv: caller falls back to an automatic name
    end
end

mp.register_script_message('ap-profiles-open', open)
mp.register_script_message('open', open)
mp.register_script_message('toggle-auto', function() data.auto = not data.auto; save(); refresh() end)
mp.register_script_message('new', function()
    ask('Profile name:', '', function(t)
        t = t and t:gsub('^%s+', ''):gsub('%s+$', '') or ''
        local name = unique_name(t ~= '' and t or 'Profile')
        data.profiles[#data.profiles + 1] = { name = name, preset = '', type = 'any', res = 'any' }
        save(); view = { page = 'profile', name = name }
        mp.commandv('script-message-to', 'uosc', 'open-menu', utils.format_json(build_menu()))
    end)
end)
mp.register_script_message('edit', function(name) view = { page = 'profile', name = name }; refresh() end)
mp.register_script_message('page', function(page) view.page = page; refresh() end)
mp.register_script_message('set', function(key, value)
    local p = find(view.name); if not p then return end
    p[key] = value; save(); view.page = 'profile'; refresh()
end)
mp.register_script_message('use', function(name) apply(find(name)) ; refresh() end)
mp.register_script_message('rename', function()
    local p = find(view.name); if not p then return end
    ask('New name:', p.name, function(t)
        t = t and t:gsub('^%s+', ''):gsub('%s+$', '') or ''
        if t ~= '' and t ~= p.name and not find(t) then if active_name == p.name then active_name = t end; p.name = t; view.name = t; save() end
        mp.commandv('script-message-to', 'uosc', 'open-menu', utils.format_json(build_menu()))
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
        if p then apply(p) end
    end)
end)

load()
