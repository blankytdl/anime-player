-- AnimePlayer: seek-bar preview (thumbfast-compatible replacement, build 26).
-- build.bat installs this file as scripts\thumbfast.lua (the name uosc talks to), replacing the build's thumbfast.
-- No second mpv process: a still frame is cut with ffmpeg (the same folder as ffprobe, which the player already needs) and shown with
-- overlay-add. ffmpeg.exe is searched recursively only below the player executable folder, never via PATH or outside that folder.
-- Speaks thumbfast's protocol: script-messages thumb / clear / info, thumbfast-info.
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

local opts = { max_height = 200, max_width = 200, overlay_id = 42, network = false, interval = 0 }
require('mp.options').read_options(opts, 'thumbfast')

local ffmpeg            -- resolved path (re-resolved when missing / failing)
local warned = false
local tw, th = 0, 0
local state = { path = nil, duration = 0 }
local cache = {}        -- bucket -> file
local cache_order = {}
local shown_bucket
local running, token, want
local seq = 0
math.randomseed(os.time() + math.floor((os.clock() % 1) * 100000))
local UNIQ = tostring(os.time() % 100000) .. '-' .. tostring(math.random(1000, 9999))   -- two players never share temp files

local function is_file(p) local fi = p and utils.file_info(p); return fi and fi.is_file end
local function tmpdir()
    local d = os.getenv('TEMP') or os.getenv('TMP')
    if d and d ~= '' then return d end
    return mp.command_native({ 'expand-path', '~~/cache/' }) or '.'
end

local function path_key(path)
    return path:gsub("\\", "/"):gsub("/+$", ""):lower()
end

-- Only visit child directories below exe_dir. No parent-directory or PATH lookup.
local function scan(dir, depth, visited)
    if depth > 128 then return nil end
    local key = path_key(dir)
    if visited[key] then return nil end
    visited[key] = true

    local candidate = utils.join_path(dir, 'ffmpeg.exe')
    if is_file(candidate) then return candidate end

    local subs = utils.readdir(dir, 'dirs')
    if type(subs) ~= 'table' then return nil end
    table.sort(subs, function(a, b)
        local ap = a:lower() == 'ffmpeg'
        local bp = b:lower() == 'ffmpeg'
        if ap ~= bp then return ap end
        return a:lower() < b:lower()
    end)
    for _, sub in ipairs(subs) do
        if sub ~= '' and sub ~= '.' and sub ~= '..' and not sub:find('[/\\]') then
            local found = scan(utils.join_path(dir, sub), depth + 1, visited)
            if found then return found end
        end
    end
    return nil
end

local function find_ffmpeg(force)
    if ffmpeg and not force and is_file(ffmpeg) then return ffmpeg end
    ffmpeg = nil
    local exe_dir = mp.command_native({ 'expand-path', '~~exe_dir/' }) or ''
    if exe_dir == '' then return nil end

    local direct = utils.join_path(exe_dir, 'ffmpeg.exe')
    if is_file(direct) then ffmpeg = direct; return direct end
    ffmpeg = scan(exe_dir, 0, {})
    return ffmpeg
end

local function has_video()
    local p = mp.get_property_native('video-params')
    if type(p) ~= 'table' or not p.w or not p.h then return false end
    local tracks = mp.get_property_native('track-list') or {}
    for _, t in ipairs(tracks) do
        if t.type == 'video' and t.selected and not t.albumart then return true end
    end
    return false
end

local function usable()
    local path = mp.get_property('path')
    if not path or path == '' then return false end
    if path:find('://', 1, true) and not opts.network then return false end
    return has_video()
end

local function compute_size()
    local p = mp.get_property_native('video-params')
    if type(p) ~= 'table' or not p.w or not p.h or p.w == 0 or p.h == 0 then return false end
    local w, h = p.dw or p.w, p.dh or p.h
    local s = math.min(opts.max_width / w, opts.max_height / h)
    tw, th = math.max(2, math.floor(w * s / 2) * 2), math.max(2, math.floor(h * s / 2) * 2)
    return true
end

local function publish()
    local ok = usable() and compute_size()
    local info = { width = ok and tw or 0, height = ok and th or 0, disabled = not ok, available = ok, overlay_id = opts.overlay_id, socket = '', thumbnail = '', spawn_first = false }
    local json = utils.format_json(info)
    mp.commandv('script-message', 'thumbfast-info', json)
    mp.commandv('script-message-to', 'uosc', 'thumbfast-info', json)
end

local function clear_overlay()
    if shown_bucket ~= nil then mp.commandv('overlay-remove', tostring(opts.overlay_id)); shown_bucket = nil end
end

local function forget_files()
    for _, f in pairs(cache) do os.remove(f) end
    cache, cache_order = {}, {}
end

local function show(bucket, x, y)
    local f = cache[bucket]
    if not f then return end
    mp.commandv('overlay-add', tostring(opts.overlay_id), tostring(math.floor(x)), tostring(math.floor(y)), f, '0', 'bgra', tostring(tw), tostring(th), tostring(tw * 4))
    shown_bucket = bucket
end

local function bucket_of(t)
    local step = opts.interval > 0 and opts.interval or math.max(1, (state.duration or 0) / 150)
    return math.floor(t / step) * step
end

local function start_job()
    if running or not want then return end
    local r = want
    if cache[r.bucket] then show(r.bucket, r.x, r.y); want = nil; return end

    local ffmpeg_path = find_ffmpeg(false)
    if not ffmpeg_path then
        if not warned then
            warned = true
            msg.warn('thumbnail preview: ffmpeg.exe was not found inside the player folder or its subfolders.')
        end
        want = nil
        return
    end

    running = true
    seq = seq + 1
    local out = utils.join_path(tmpdir(), string.format('ap-thumb-%s-%d.bgra', UNIQ, seq))
    local args = { ffmpeg_path, '-v', 'error', '-nostdin', '-ss', string.format('%.3f', r.bucket), '-i', state.path, '-an', '-sn', '-dn',
                   '-frames:v', '1', '-vf', string.format('scale=%d:%d', tw, th), '-pix_fmt', 'bgra', '-f', 'rawvideo', '-y', out }
    token = mp.command_native_async({ name = 'subprocess', playback_only = false, capture_stderr = true, args = args }, function(ok, res)
        running, token = false, nil
        if res and res.killed_by_us then start_job(); return end      -- cancelled on purpose (mouse left the bar / new file): not an error
        local good = ok and res and res.status == 0 and (utils.file_info(out) or {}).size == tw * th * 4
        if good then
            cache[r.bucket] = out
            cache_order[#cache_order + 1] = r.bucket
            if #cache_order > 60 then local old = table.remove(cache_order, 1); if cache[old] then os.remove(cache[old]); cache[old] = nil end end
            if want and want.bucket == r.bucket then show(r.bucket, want.x, want.y); want = nil end
        else
            os.remove(out)
            if not warned then
                warned = true
                msg.warn('thumbnail failed (ffmpeg: ' .. tostring(ffmpeg_path) .. '): ' .. tostring(res and res.stderr or 'could not start'))
            end
            find_ffmpeg(true)            -- the folder may have moved: look again next time
            if want and want.bucket == r.bucket then want = nil end
        end
        start_job()
    end)
end

mp.register_script_message('thumb', function(time, x, y)
    if not usable() or tw == 0 then return end
    local t = tonumber(time); x = tonumber(x); y = tonumber(y)
    if not (t and x and y) then return end
    state.path = mp.get_property('path')
    state.duration = mp.get_property_number('duration', 0)
    local dur = state.duration
    if dur > 0 then t = math.max(0, math.min(t, dur - 0.5)) end
    local b = bucket_of(t)
    want = { bucket = b, x = x, y = y }
    if cache[b] then show(b, x, y) else
        if shown_bucket ~= nil then show(shown_bucket, x, y) end   -- keep following the mouse with the last frame while the new one is cut
        start_job()
    end
end)
mp.register_script_message('clear', function()
    want = nil
    if token then mp.abort_async_command(token) end
    clear_overlay()
end)
mp.register_script_message('info', publish)
mp.register_script_message('thumbfast-info-request', publish)

mp.register_event('start-file', function()
    want = nil
    if token then mp.abort_async_command(token) end
    clear_overlay(); forget_files(); tw, th = 0, 0
end)
mp.register_event('file-loaded', function() find_ffmpeg(false); publish() end)
mp.observe_property('video-params', 'native', function() if compute_size() then publish() end end)
mp.register_event('shutdown', function() forget_files() end)
