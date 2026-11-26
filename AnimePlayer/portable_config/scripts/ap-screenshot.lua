-- AnimePlayer: "Screenshot without subtitles" (build 37, rewritten).
--
-- Why the older version could look like "nothing happens" or still show text:
--   * it asked mpv for a shot but never checked that a file really appeared, so a failed / blocked write was silent;
--   * the destination was left to mpv's template and a stuck "busy" flag could swallow every later key press;
--   * there was no way to see what had happened.
-- What it does now:
--   1. mpv path (method=auto / mpv): soft subtitles are switched off, one frame is drawn, `screenshot-to-file ... video`
--      writes an explicit file (screenshot-directory, name = <video>_<time>_nosubs.<format>), and the subtitle state is
--      put back exactly as it was - also when the shot fails. The file is checked afterwards.
--   2. If mpv fails or writes no file (method=auto) the same frame is cut from the video file with ffmpeg.exe. ffmpeg never
--      draws soft subtitles. (Plain decoded frame: no shaders / mpv colour processing.)
--   3. Everything is written to ap-screenshot.log next to mpv.conf. The on-screen message says which way was used and where
--      the file is.
-- Hard (burned-in) subtitles are part of the picture itself: no player can remove them. When no subtitle track was active
-- the message says so, so text in the picture is recognised as burned-in.
-- Bound to: F6, Ctrl+e, the control-bar button, Video > Take Screenshot without subtitles.
-- Options (script-opts/ap-screenshot.conf):  method=auto|mpv|ffmpeg   wait=0.3 (seconds for the renderer to draw a frame without subtitles)
local mp = require 'mp'
local msg = require 'mp.msg'
local utils = require 'mp.utils'

local opts = { method = 'auto', wait = 0.3 }
require('mp.options').read_options(opts, 'ap-screenshot')

local PROPS = { 'sub-visibility', 'secondary-sub-visibility' }
local STUCK = 20                 -- a shot older than this is considered dead: the key works again
local busy_since = nil
local log_lines = 0

local function cfg_dir() return mp.command_native({ 'expand-path', '~~/' }) or '.' end
local LOG = utils.join_path(cfg_dir(), 'ap-screenshot.log')
local function dbg(...)
    local t = {}
    for _, v in ipairs({ ... }) do t[#t + 1] = tostring(v) end
    local line = table.concat(t, ' ')
    msg.info(line)
    if log_lines < 300 then
        log_lines = log_lines + 1
        local f = io.open(LOG, log_lines == 1 and 'wb' or 'ab')
        if f then f:write(os.date('%H:%M:%S '), line, '\n'); f:close() end
    end
end

local function is_file(p) local fi = p and utils.file_info(p); return fi and fi.is_file and (fi.size or 0) > 0 end
local function is_dir(p) local fi = p and utils.file_info(p); return fi and fi.is_dir end

-- ---------- where the file goes ----------
local function target()
    local dir = mp.get_property('screenshot-directory') or ''
    if dir ~= '' then dir = mp.command_native({ 'expand-path', dir }) or dir end
    if dir == '' or not is_dir(dir) then
        dbg('screenshot-directory "' .. dir .. '" is missing, using the desktop')
        dir = mp.command_native({ 'expand-path', '~~desktop/' }) or '.'
    end
    local fmt = (mp.get_property('screenshot-format') or 'png'):lower()
    local ext = (fmt == 'jpeg') and 'jpg' or fmt
    local name = mp.get_property('filename/no-ext') or 'video'
    name = name:gsub('[\\/:*?"<>|%c]', '_'):sub(1, 80)
    local stamp = (mp.get_property_osd('time-pos/full') or '0'):gsub('[^%d%.]', '-')
    local base = name .. '_' .. stamp .. '_nosubs'
    local out = utils.join_path(dir, base .. '.' .. ext)
    local n = 1
    while utils.file_info(out) and n < 100 do n = n + 1; out = utils.join_path(dir, base .. '_' .. n .. '.' .. ext) end
    return out, ext
end

-- ---------- ffmpeg fallback ----------
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
local function find_ffmpeg()
    local exe_dir = mp.command_native({ 'expand-path', '~~exe_dir/' }) or ''
    for _, c in ipairs({ utils.join_path(exe_dir, 'ffmpeg.exe'), utils.join_path(exe_dir, 'ffmpeg/ffmpeg.exe'), utils.join_path(cfg_dir(), 'ffmpeg.exe') }) do
        if is_file(c) then return c end
    end
    return (exe_dir ~= '' and scan(exe_dir, 2)) or 'ffmpeg'
end

-- ---------- the shot ----------
local function take()
    if busy_since and (mp.get_time() - busy_since) < STUCK then mp.osd_message('Screenshot already in progress'); return end
    if not mp.get_property_native('video-params') then mp.osd_message('No video to take a screenshot of'); return end
    busy_since = mp.get_time()

    local path = mp.get_property('path') or ''
    local pos = mp.get_property_number('time-pos', 0) or 0
    local out, ext = target()
    local sid = mp.get_property_native('sid')
    local had_sub = (sid ~= nil and sid ~= false and sid ~= 'no')
    local method = (opts.method == 'mpv' or opts.method == 'ffmpeg') and opts.method or 'auto'
    local on_disk = path ~= '' and not path:find('://', 1, true)
    dbg('--- shot: method=' .. method .. ' time=' .. string.format('%.3f', pos) .. ' sub-track-active=' .. tostring(had_sub) .. ' file=' .. out)

    local saved, restored = {}, false
    local function restore()
        if restored then return end
        restored = true
        for p, v in pairs(saved) do pcall(mp.set_property_native, p, v) end
    end
    local function done(ok, how, why)
        restore()
        busy_since = nil
        if ok then
            local text = 'Screenshot saved (no subtitles' .. (how == 'ffmpeg' and ', plain frame' or '') .. '):\n' .. (out:match('([^/\\]+)$') or out)
            if not had_sub then text = text .. '\nNo subtitle track was on - text in the picture is burned into the video.' end
            mp.osd_message(text, 4)
            dbg('saved via ' .. how .. ': ' .. out)
        else
            mp.osd_message('Screenshot failed: ' .. tostring(why) .. '\n(details: ap-screenshot.log)', 5)
            dbg('FAILED: ' .. tostring(why))
        end
    end

    local function via_ffmpeg(reason)
        restore()          -- ffmpeg does not need the subtitles off
        if not on_disk then done(false, 'ffmpeg', tostring(reason) .. '; network streams cannot be cut by ffmpeg'); return end
        local ff = find_ffmpeg()
        local args = { ff, '-v', 'error', '-nostdin', '-ss', string.format('%.3f', pos), '-i', path, '-an', '-sn', '-dn', '-frames:v', '1' }
        if ext == 'jpg' then args[#args + 1] = '-q:v'; args[#args + 1] = '2' end
        args[#args + 1] = '-y'; args[#args + 1] = out
        dbg('ffmpeg fallback (' .. tostring(reason) .. '): ' .. ff)
        local started = mp.command_native_async({ name = 'subprocess', playback_only = false, capture_stderr = true, args = args }, function(ok, res)
            if ok and res and res.status == 0 and is_file(out) then done(true, 'ffmpeg')
            else done(false, 'ffmpeg', 'ffmpeg ' .. tostring(res and ((res.error_string or '') ~= '' and res.error_string or res.stderr) or 'could not start') .. ' (' .. ff .. ')') end
        end)
        if not started then done(false, 'ffmpeg', 'could not start ffmpeg') end
    end

    local function via_mpv()
        for _, p in ipairs(PROPS) do
            local v = mp.get_property_native(p)
            if v ~= nil then
                saved[p] = v
                pcall(mp.set_property_native, p, false)
            end
        end
        dbg('subtitles off: sub-visibility=' .. tostring(mp.get_property_native('sub-visibility')) .. ' secondary=' .. tostring(mp.get_property_native('secondary-sub-visibility')))
        mp.add_timeout(tonumber(opts.wait) or 0.3, function()
            local started = mp.command_native_async({ name = 'screenshot-to-file', filename = out, flags = 'video' }, function(ok, _, err)
                if ok and is_file(out) then done(true, 'mpv')
                elseif method == 'auto' then via_ffmpeg('mpv: ' .. tostring(ok and 'no file was written' or err))
                else done(false, 'mpv', ok and 'mpv reported success but no file was written' or err) end
            end)
            if not started then
                if method == 'auto' then via_ffmpeg('mpv: command not started') else done(false, 'mpv', 'command not started') end
            end
        end)
    end

    if method == 'ffmpeg' then via_ffmpeg('method=ffmpeg') else via_mpv() end
end

mp.register_script_message('ap-shot-nosubs', take)
