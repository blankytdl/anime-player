-- AnimePlayer: lets thumbfast (seek-bar preview) find a STANDALONE mpv.exe.
-- thumbfast starts a second, tiny mpv process to render the previews. mpv.net only embeds libmpv, so there is
-- no mpv.exe to start -> "thumbfast: ERROR! install standalone mpv" and blank / flickering previews.
-- build.bat copies a real mpv.exe to <player folder>\mpv-thumb\mpv.exe; this script (named 00- so it loads
-- before thumbfast) tells thumbfast where it is.
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

local function is_file(p)
    local i = utils.file_info(p)
    return i ~= nil and i.is_file
end

local dir = mp.command_native({ 'expand-path', '~~exe_dir/' })
if type(dir) ~= 'string' or dir == '' then return end

local found
for _, rel in ipairs({ 'mpv-thumb/mpv.exe', 'mpv.exe', 'mpv/mpv.exe' }) do
    local p = utils.join_path(dir, rel)
    if is_file(p) then found = p break end
end

if found then
    -- change-list 'append' adds ONE key=value atomically (no read-modify-write of the whole script-opts table, so it can never race with
    -- ap-responsive / uosc). A path with a comma is quoted with mpv's %length% syntax.
    local val = found:find('[,%%]') and ('%' .. #found .. '%' .. found) or found
    mp.commandv('change-list', 'script-opts', 'append', 'thumbfast-mpv_path=' .. val)
    msg.info('thumbfast will use ' .. found)
else
    msg.warn('no standalone mpv.exe next to the player: seek-bar previews need one (see mpv-exe\\PUT_MPV_EXE_HERE.txt)')
end
