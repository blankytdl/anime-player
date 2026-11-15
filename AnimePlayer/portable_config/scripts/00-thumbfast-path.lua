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

-- Prefer mpv.net's actual frontend executable path. Unlike a build-time absolute path,
-- this is recalculated every launch and continues to work after the whole player folder moves.
local dirs = {}
local frontend = mp.get_property_native('user-data/frontend/process-path')
if type(frontend) == 'string' and frontend ~= '' then
    local frontend_dir = frontend:match('^(.*)[/\\\\][^/\\\\]+

if found then
    -- change-list 'append' adds ONE key=value atomically (no read-modify-write of the whole script-opts table, so it can never race with
    -- ap-responsive / uosc). A path with a comma is quoted with mpv's %length% syntax.
    local val = found:find('[,%%]') and ('%' .. #found .. '%' .. found) or found
    mp.commandv('change-list', 'script-opts', 'append', 'thumbfast-mpv_path=' .. val)
    msg.info('thumbfast will use ' .. found)
else
    msg.warn('no standalone mpv.exe next to the player: seek-bar previews need one (see mpv-exe\\PUT_MPV_EXE_HERE.txt)')
end
)
    if frontend_dir and frontend_dir ~= '' then
        dirs[#dirs + 1] = frontend_dir
    end
end

-- Fallback for hosts that do not expose the mpv.net frontend property.
local exe_dir = mp.command_native({ 'expand-path', '~~exe_dir/' })
if type(exe_dir) == 'string' and exe_dir ~= '' then
    local duplicate = false
    for _, existing in ipairs(dirs) do
        if existing:lower() == exe_dir:lower() then duplicate = true break end
    end
    if not duplicate then dirs[#dirs + 1] = exe_dir end
end

local found
for _, dir in ipairs(dirs) do
    for _, rel in ipairs({ 'mpv-thumb/mpv.exe', 'mpv.exe', 'mpv/mpv.exe' }) do
        local p = utils.join_path(dir, rel)
        if is_file(p) then found = p break end
    end
    if found then break end
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
