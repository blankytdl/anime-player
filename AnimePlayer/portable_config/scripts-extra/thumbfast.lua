-- AnimePlayer: seek-bar preview (thumbfast-compatible replacement, build 34).
-- build.bat installs this file as scripts\thumbfast.lua (the name uosc talks to), replacing the build's thumbfast.
-- No second mpv process. Frames are cut with ffmpeg.exe and shown with overlay-add.
-- Build 34 fixes (long videos froze on one thumbnail until the mouse left the bar):
--   * hover frames are cut with a KEYFRAME seek (-noaccurate_seek): milliseconds even in a 2 h file, instead of decoding up to the exact frame
--   * a job that finishes after the mouse moved on is still shown (nearest frame) and the newest request is cut right after it
--   * an exact-frame job is cancelled as soon as the mouse moves; every job has a watchdog (it can never block the queue)
--   * the exact frame when the mouse rests (refine) never ran: its timer called a function that did not exist yet
--   * short videos / the last second: failed frames fall back to the nearest cached frame instead of nothing
-- Build 37 (hover preview was slow: every new position waited for its own ffmpeg, 0.3 s or more each):
--   * PREFETCH: shortly after a video starts, a coarse grid of frames (one every ~20-30 s, at most 64) is cut in the background, ONE small keyframe cut at a time
--     (a few MB read in total, not the whole file like the sprite pass). Hovering then shows the nearest cached frame AT ONCE and the exact frame swaps in after.
--   * the background cut is stopped the moment the mouse touches the bar, and it waits while the mouse is moving.
--   * a position that is not cached yet shows the nearest cached frame instead of the old, unrelated one.
--   Switch off: prefetch=no in thumbfast.conf.
-- The sprite pass (1.) is OFF by default (background=yes in thumbfast.conf): it reads the whole file and can make seeking / starting stutter.
--   1. optional SPRITE pass: one low-priority ffmpeg writes a rough frame every few seconds into one temp file.
--   2. hover: a rough (keyframe) frame follows the mouse; when the mouse rests 0.25 s the exact frame swaps in.
-- ffmpeg.exe is looked up again on every start and whenever it fails, so moving the folder or pinning the exe cannot leave a stale path.
-- Speaks thumbfast's protocol: script-messages thumb / clear / info, plus thumbfast-info and the user-data/thumbfast property.
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'


local opts = { max_height = 200, max_width = 200, overlay_id = 42, network = false, interval = 0, background = false, refine_delay = 0.2, job_timeout = 3, exact_timeout = 8, prefetch = true, prefetch_max = 64, prefetch_delay = 2 }
require('mp.options').read_options(opts, 'thumbfast')


local LOG = (mp.command_native({ 'expand-path', '~~/' }) or '.') .. 'thumbfast.log'
local log_lines = 0
local function dbg(...)
    local t = {}
    for _, v in ipairs({ ... }) do t[#t + 1] = tostring(v) end
    local line = table.concat(t, ' ')
    msg.info(line)
    if log_lines < 200 then
        log_lines = log_lines + 1
        local f = io.open(LOG, log_lines == 1 and 'wb' or 'ab')
        if f then f:write(os.date('%H:%M:%S '), line, '\n'); f:close() end
    end
end

local ffmpeg            -- resolved path (re-resolved when missing / failing)
local warned = false
local tw, th = 0, 0
local state = { path = nil, duration = 0 }
local cache = {}            -- key -> file   ('r:<bucket>' = rough keyframe frame, 'e:<second>' = exact frame)
local cache_t = {}          -- key -> time the frame stands for
local cache_order = {}
local failed = {}           -- keys ffmpeg could not cut (end of file, timeout): the nearest cached frame is shown instead
local shown                 -- key of what is on screen: 'sprite:<i>' or a cache key
local running, token, want, timed_out
local seq = 0
math.randomseed(os.time() + math.floor((os.clock() % 1) * 100000))
local UNIQ = tostring(os.time() % 100000) .. '-' .. tostring(math.random(1000, 9999))   -- two players never share temp files
local sprite = { file = nil, step = 0, count = 0, token = nil, finished = false }
local sprite_timer, refine_timer, watchdog
local refine                -- forward declaration: the timer below calls it (it used to call a nil global, so the exact frame never arrived)
refine_timer = mp.add_timeout(opts.refine_delay, function() if refine then refine() end end); refine_timer:kill()    -- ONE timer, reused
watchdog = mp.add_timeout(opts.job_timeout, function()
    if token and running then timed_out = running.key; mp.abort_async_command(token) end
end); watchdog:kill()
local slow_until = 0        -- after a timeout no exact (slow) frames are tried for a while
local latest                -- last hover request {t=, x=, y=}
local last_hover = -10      -- mp.get_time() of the last hover request (the background prefetch waits for the mouse to rest)
local bg = { list = {}, i = 1 }  -- prefetch plan: times to cut, in coarse-to-fine order
local bg_timer, bg_step
local last_put              -- {file, offset}: what is on screen, so it can keep following the mouse while a new frame is being cut

local function is_file(p) local fi = p and utils.file_info(p); return fi and fi.is_file end
local function tmpdir()
    local d = os.getenv('TEMP') or os.getenv('TMP')
    if d and d ~= '' then return d end
    return mp.command_native({ 'expand-path', '~~/cache/' }) or '.'
end

local function scan(dir, depth)
    if depth < 0 then return nil end
    local c = utils.join_path(dir, 'ffmpeg.exe')
    if is_file(c) then return c end
    local subs = utils.readdir(dir, 'dirs')
    if type(subs) ~= 'table' then return nil end
    table.sort(subs)
    for _, s in ipairs(subs) do
        local f = scan(utils.join_path(dir, s), depth - 1)
        if f then return f end
    end
    return nil
end

local searched_at = -1000
local function find_ffmpeg(force)
    -- a path that was found stays; a failed search (plain 'ffmpeg') is not repeated for 60 s, so no folder scan runs on every hover
    if ffmpeg and not force and ((ffmpeg ~= 'ffmpeg' and is_file(ffmpeg)) or (ffmpeg == 'ffmpeg' and os.time() - searched_at < 60)) then return ffmpeg end
    searched_at = os.time()
    ffmpeg = nil
    local exe_dir = mp.command_native({ 'expand-path', '~~exe_dir/' }) or ''
    local cfg = mp.command_native({ 'expand-path', '~~/' }) or ''
    for _, c in ipairs({ utils.join_path(exe_dir, 'ffmpeg.exe'), utils.join_path(exe_dir, 'ffmpeg/ffmpeg.exe'), utils.join_path(cfg, 'ffmpeg.exe') }) do
        if is_file(c) then ffmpeg = c; return c end
    end
    if exe_dir ~= '' then ffmpeg = scan(exe_dir, 2) end      -- ffmpeg-*/bin/ffmpeg.exe below the player folder
    if not ffmpeg then ffmpeg = 'ffmpeg' end                  -- last try: PATH
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

local ok_cache = false     -- computed ONCE per file (in publish), not on every mouse move
local function check_usable()
    local path = mp.get_property('path')
    if not path or path == '' then return false end
    if path:find('://', 1, true) and not opts.network then dbg('disabled: network path (set network=yes in thumbfast.conf)'); return false end
    if not has_video() then dbg('disabled: no video track'); return false end
    return true
end
local function usable() return ok_cache end

local function compute_size()
    local p = mp.get_property_native('video-params')
    if type(p) ~= 'table' or not p.w or not p.h or p.w == 0 or p.h == 0 then return false end
    local w, h = p.dw or p.w, p.dh or p.h
    local s = math.min(opts.max_width / w, opts.max_height / h)
    tw, th = math.max(2, math.floor(w * s / 2) * 2), math.max(2, math.floor(h * s / 2) * 2)
    return true
end

local function publish()
    ok_cache = check_usable()
    local ok = ok_cache and compute_size()
    local info = { width = ok and tw or 0, height = ok and th or 0, disabled = not ok, available = ok, overlay_id = opts.overlay_id, socket = '', thumbnail = '', spawn_first = false }
    local json = utils.format_json(info)
    pcall(mp.set_property_native, 'user-data/thumbfast', json)   -- thumbfast itself stores the JSON TEXT here, so uosc is written for exactly that
    mp.commandv('script-message', 'thumbfast-info', json)
    mp.commandv('script-message-to', 'uosc', 'thumbfast-info', json)
    dbg('info sent: available=' .. tostring(ok) .. ' size=' .. tw .. 'x' .. th .. ' ffmpeg=' .. tostring(ffmpeg))
end

local function clear_overlay()
    if shown ~= nil then mp.commandv('overlay-remove', tostring(opts.overlay_id)); shown = nil; last_put = nil end
end

local function put(file, offset, x, y)
    last_put = { file, offset }
    mp.commandv('overlay-add', tostring(opts.overlay_id), tostring(math.floor(x)), tostring(math.floor(y)), file, tostring(offset), 'bgra', tostring(tw), tostring(th), tostring(tw * 4))
end

local function forget_files()
    for _, f in pairs(cache) do os.remove(f) end
    cache, cache_t, cache_order, failed = {}, {}, {}, {}
    bg.list, bg.i = {}, 1
    if sprite.file then os.remove(sprite.file) end
    sprite = { file = nil, step = 0, count = 0, token = nil, finished = false }
end

-- ---------- layer 1: the sprite file ----------
local function sprite_frames_ready()
    if not sprite.file then return 0 end
    local fi = utils.file_info(sprite.file)
    if not fi or not fi.size then return 0 end
    return math.min(sprite.count, math.floor(fi.size / (tw * th * 4)))
end

local function show_sprite(t, x, y)
    if sprite.step <= 0 then return false end
    local ready = sprite_frames_ready()
    if ready <= 0 then return false end
    local i = math.floor(t / sprite.step + 0.5)
    if i < 0 then i = 0 end
    if i >= ready then return false end              -- that part is not written yet
    put(sprite.file, i * tw * th * 4, x, y)
    shown = 'sprite:' .. i
    return true
end

local function start_sprite()
    if not opts.background or sprite.file or tw == 0 or not usable() then return end
    local dur = mp.get_property_number('duration', 0) or 0
    if dur < 20 then return end
    local n = math.max(24, math.min(240, math.floor(dur / 6)))
    sprite.step, sprite.count = dur / n, n
    sprite.file = utils.join_path(tmpdir(), string.format('ap-sprite-%s.bgra', UNIQ))
    local args = { find_ffmpeg(false), '-v', 'error', '-nostdin', '-threads', '1', '-skip_frame', 'nokey', '-i', mp.get_property('path'),
                   '-an', '-sn', '-dn', '-filter_threads', '1', '-vf', string.format('fps=1/%.4f,scale=%d:%d', sprite.step, tw, th),
                   '-frames:v', tostring(n), '-pix_fmt', 'bgra', '-f', 'rawvideo', '-y', sprite.file }
    dbg('sprite pass: ' .. n .. ' frames, one every ' .. string.format('%.1f', sprite.step) .. ' s')
    local mine = sprite.file
    sprite.token = mp.command_native_async({ name = 'subprocess', playback_only = false, capture_stderr = true, args = args }, function(ok, res)
        if sprite.file ~= mine then return end       -- a newer file started meanwhile
        sprite.token, sprite.finished = nil, true
        dbg('sprite pass done: ok=' .. tostring(ok) .. ' status=' .. tostring(res and res.status) .. ' frames=' .. sprite_frames_ready() .. ' ' .. tostring(res and res.stderr or ''))
    end)
end

-- ---------- layer 2: frames cut on demand ----------
local function clampn(v, lo, hi) return math.max(lo, math.min(hi, v)) end
local function rough_key(t)
    local step = opts.interval > 0 and opts.interval or clampn((state.duration or 0) / 240, 1, 10)
    local b = math.floor(t / step) * step
    return 'r:' .. string.format('%.2f', b), b
end
local function exact_key(t) return 'e:' .. math.floor(t) end

local function show_key(key, x, y)
    local f = cache[key]
    if not f then return false end
    put(f, 0, x, y)
    shown = key
    return true
end

local function nearest_cached(t)
    local best, bd
    for k, ct in pairs(cache_t) do
        if cache[k] then
            local d = math.abs(ct - t)
            if not bd or d < bd then best, bd = k, d end
        end
    end
    return best
end

local start_job
local function launch(r)
    running = r
    seq = seq + 1
    local out = utils.join_path(tmpdir(), string.format('ap-thumb-%s-%d.bgra', UNIQ, seq))
    dbg('cutting', r.bg and 'prefetch' or (r.exact and 'exact' or 'rough'), 'frame at', string.format('%.1f', r.t), 'with', tostring(find_ffmpeg(false)))
    local args = { find_ffmpeg(false), '-v', 'error', '-nostdin', '-threads', r.bg and '1' or '2', '-ss', string.format('%.3f', r.t) }
    if not r.exact then args[#args + 1] = '-noaccurate_seek' end        -- keyframe seek: instant, even in a long file
    for _, a in ipairs({ '-i', state.path, '-an', '-sn', '-dn', '-frames:v', '1', '-vf', string.format('scale=%d:%d', tw, th), '-pix_fmt', 'bgra', '-f', 'rawvideo', '-y', out }) do args[#args + 1] = a end
    timed_out = nil
    watchdog:kill(); watchdog.timeout = r.exact and opts.exact_timeout or opts.job_timeout; watchdog:resume()
    token = mp.command_native_async({ name = 'subprocess', playback_only = false, capture_stderr = true, args = args }, function(ok, res)
        watchdog:kill()
        running, token = nil, nil
        if res and res.killed_by_us then       -- cancelled on purpose (newer request / mouse left / new file) or by the watchdog: not an error
            os.remove(out)
            if timed_out == r.key then
                failed[r.key] = true; timed_out = nil; slow_until = os.time() + 10; dbg('frame at', string.format('%.1f', r.t), 'timed out')
                if r.bg then bg.i = bg.i + 1 end
            end
            start_job(); return
        end
        local good = ok and res and res.status == 0 and (utils.file_info(out) or {}).size == tw * th * 4
        if not r.bg or not good then dbg('ffmpeg done: ok=' .. tostring(ok) .. ' status=' .. tostring(res and res.status) .. ' good=' .. tostring(good) .. ' ' .. tostring(res and res.stderr or '')) end
        if r.bg then bg.i = bg.i + 1 end
        if good then
            cache[r.key] = out; cache_t[r.key] = r.t
            if not r.bg then      -- prefetch frames are never evicted (they are the instant layer); hover frames are, oldest first
                cache_order[#cache_order + 1] = r.key
                if #cache_order > 120 then local old = table.remove(cache_order, 1); if cache[old] then os.remove(cache[old]); cache[old] = nil; cache_t[old] = nil end end
            end
            -- show it even when the mouse has moved on meanwhile (nearest frame beats a frozen old one); the newest request is cut next
            if want then
                show_key(r.key, want.x, want.y)
                if want.key == r.key then want = nil end
            end
        else
            os.remove(out)
            failed[r.key] = true               -- end of file / short video / broken seek: do not retry this frame, show the nearest one
            if not warned then
                warned = true
                msg.warn('thumbnail failed (ffmpeg: ' .. tostring(ffmpeg) .. '): ' .. tostring(res and res.stderr or 'could not start'))
            end
            find_ffmpeg(false)                 -- re-checks the path (and searches again only if it vanished)
            if want and want.key == r.key then
                local k = nearest_cached(r.t)
                if k then show_key(k, want.x, want.y) end
                want = nil
            end
        end
        start_job()
    end)
end

start_job = function()
    if running then return end
    if not want then if bg_step then bg_step() end; return end
    local r = want
    if cache[r.key] then show_key(r.key, r.x, r.y); want = nil; return end
    if r.exact and os.time() < slow_until then want = nil; return end
    if failed[r.key] then
        local k = nearest_cached(r.t)
        if k then show_key(k, r.x, r.y) end
        want = nil
        return
    end
    launch(r)
end

-- ---------- layer 3: background prefetch (the instant layer) ----------
-- A coarse grid of keyframe frames, cut one at a time while nobody hovers. Order: the middle first, then the quarters, then the eighths ...
-- so after a few seconds the whole video is already covered roughly, and every further frame only makes it finer.
local function build_bg_plan()
    bg.list, bg.i = {}, 1
    local dur = state.duration or 0
    if dur <= 0 then dur = mp.get_property_number('duration', 0) or 0; state.duration = dur end
    if dur < 20 then return end
    -- a power of two, so every level of the coarse-to-fine order is evenly spread over the whole video
    local want_n = clampn(math.floor(dur / 20), 8, math.max(8, opts.prefetch_max))
    local bits, size = 0, 1
    while size * 2 <= want_n do size = size * 2; bits = bits + 1 end
    for k = 0, size - 1 do
        local r, v = 0, k
        for _ = 1, bits do r = r * 2 + (v % 2); v = math.floor(v / 2) end      -- bit reversal = coarse-to-fine order
        bg.list[#bg.list + 1] = math.min((r + 0.5) * dur / size, dur - 1)
    end
end

bg_step = function()
    if running or want or not opts.prefetch or not ok_cache or tw == 0 or not state.path then return end
    if mp.get_time() - last_hover < 0.8 then       -- the mouse is (or was just) on the bar: its own request goes first
        if bg_timer then bg_timer:kill(); bg_timer:resume() end
        return
    end
    while bg.list[bg.i] do
        local key = 'bg:' .. bg.i
        if cache[key] or failed[key] then bg.i = bg.i + 1 else
            launch({ key = key, t = bg.list[bg.i], exact = false, bg = true })
            return
        end
    end
end
bg_timer = mp.add_timeout(0.9, function() start_job() end); bg_timer:kill()

local function start_prefetch()
    if not opts.prefetch or not usable() or tw == 0 then return end
    if not state.path then state.path = mp.get_property('path') end
    build_bg_plan()
    dbg('prefetch plan: ' .. #bg.list .. ' frames')
    start_job()
end

refine = function()
    if not latest or tw == 0 or not usable() then return end
    local k = exact_key(latest.t)
    if cache[k] then show_key(k, latest.x, latest.y); want = nil; return end
    if failed[k] then return end
    want = { key = k, t = latest.t, exact = true, x = latest.x, y = latest.y }
    start_job()
end

local seen = 0
mp.register_script_message('thumb', function(time, x, y)
    seen = seen + 1
    if seen <= 5 then dbg('thumb request #' .. seen .. ': time=' .. tostring(time) .. ' x=' .. tostring(x) .. ' y=' .. tostring(y) .. ' size=' .. tw .. 'x' .. th) end
    if tw == 0 and ok_cache then compute_size() end      -- info was lost (e.g. reload): recover
    if not usable() or tw == 0 then return end
    local t = tonumber(time); x = tonumber(x); y = tonumber(y)
    if not (t and x and y) then return end
    if not state.path then state.path = mp.get_property('path') end
    if state.duration == 0 then state.duration = mp.get_property_number('duration', 0) end
    if state.duration > 0 then t = math.max(0, math.min(t, state.duration - 0.5)) end
    latest = { t = t, x = x, y = y }
    last_hover = mp.get_time()
    if bg_timer then bg_timer:kill() end
    local ek = exact_key(t)
    -- 1. an exact frame we already have, 2. the rough sprite frame (instant), 3. a rough keyframe cut
    if cache[ek] then
        want = nil
        show_key(ek, x, y)
    elseif running and running.exact and running.key == ek then
        want = { key = ek, t = t, exact = true, x = x, y = y }       -- the exact frame for this very second is being cut: let it finish
    elseif show_sprite(t, x, y) then
        want = nil
    else
        local rk, b = rough_key(t)
        want = { key = rk, t = b, exact = false, x = x, y = y }
        if cache[rk] then show_key(rk, x, y); want = nil
        else
            local nk = nearest_cached(t)                    -- INSTANT: the closest frame we already have (prefetch grid / earlier hovers)
            if nk then show_key(nk, x, y)
            elseif shown and last_put then put(last_put[1], last_put[2], x, y) end      -- nothing cached yet: keep following the mouse with what is on screen
            if running and (running.exact or running.bg) and token then mp.abort_async_command(token) end   -- an exact cut for another second / a background cut is useless now
            start_job()
        end
    end
    -- mouse rests: swap the rough frame for the exact one
    refine_timer:kill(); refine_timer:resume()
end)
mp.register_script_message('clear', function()
    want = nil
    refine_timer:kill()
    if token and running and not running.bg then watchdog:kill(); mp.abort_async_command(token) end
    clear_overlay()
    last_hover = mp.get_time() - 0.5          -- the prefetch goes on 0.3 s after the mouse left
    start_job()
end)
mp.register_script_message('debug', function()
    local msgs = { 'thumbfast: ffmpeg=' .. tostring(find_ffmpeg(false)) .. (is_file(ffmpeg) and ' (found)' or ' (NOT FOUND)'), 'size=' .. tw .. 'x' .. th .. ' usable=' .. tostring(usable()),
                   'sprite frames ready: ' .. sprite_frames_ready() .. '/' .. sprite.count, 'log: ' .. LOG }
    mp.osd_message(table.concat(msgs, '\n'), 8)
    publish()
end)
mp.register_script_message('info', publish)
mp.register_script_message('thumbfast-info-request', publish)

mp.register_event('start-file', function()
    want = nil
    if sprite_timer then sprite_timer:kill() end
    refine_timer:kill(); watchdog:kill(); bg_timer:kill(); state.path = nil; state.duration = 0
    if token then mp.abort_async_command(token) end
    if sprite.token then mp.abort_async_command(sprite.token) end
    clear_overlay(); forget_files(); tw, th = 0, 0
end)
mp.register_event('file-loaded', function()
    find_ffmpeg(false); publish()
    -- let playback settle first, so the sprite pass never competes with the start of a video
    if sprite_timer then sprite_timer:kill() end
    sprite_timer = mp.add_timeout(math.max(3, opts.prefetch_delay), function() start_sprite(); start_prefetch() end)
end)
mp.observe_property('video-params', 'native', function() if compute_size() then publish() end end)
mp.register_event('shutdown', function()
    if sprite.token then mp.abort_async_command(sprite.token) end
    forget_files()
end)
