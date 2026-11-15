-- AnimePlayer: lets thumbfast (seek-bar preview) find a STANDALONE mpv.exe.
-- thumbfast starts a second mpv process. mpv.net embeds libmpv, so it needs a separate mpv.exe.
-- The complete runtime is copied to <player folder>\mpv-thumb\; this script finds it after folder moves.
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

local function is_file(path)
    local info = utils.file_info(path)
    return info ~= nil and info.is_file
end

local function add_directory(list, path)
    if type(path) ~= 'string' or path == '' then return end
    path = path:gsub("[/\\]+$", "")
    for _, existing in ipairs(list) do
        if existing:lower() == path:lower() then return end
    end
    list[#list + 1] = path
end

-- mpv.net 7+ exposes the actual frontend executable path. Resolve its directory at
-- runtime so moving the player folder does not leave a stale absolute path behind.
local search_dirs = {}
local frontend_path = mp.get_property_native('user-data/frontend/process-path')
if type(frontend_path) == 'string' and frontend_path ~= '' then
    local frontend_dir = frontend_path:match("^(.*)[/\\][^/\\]+$")
    add_directory(search_dirs, frontend_dir)
end

-- Fallback for hosts that do not expose the mpv.net frontend property.
local exe_dir = mp.command_native({ 'expand-path', '~~exe_dir/' })
add_directory(search_dirs, exe_dir)

local found
for _, dir in ipairs(search_dirs) do
    for _, relative_path in ipairs({ 'mpv-thumb/mpv.exe', 'mpv.exe', 'mpv/mpv.exe' }) do
        local candidate = utils.join_path(dir, relative_path)
        if is_file(candidate) then
            found = candidate
            break
        end
    end
    if found then break end
end

if found then
    -- change-list append adds one key=value atomically. Use mpv's length syntax for
    -- paths containing commas or percent signs so script-opts keeps the path intact.
    local value = found:find('[,%%]') and ('%' .. #found .. '%' .. found) or found
    mp.commandv('change-list', 'script-opts', 'append', 'thumbfast-mpv_path=' .. value)
    msg.info('thumbfast standalone mpv: ' .. found)
else
    msg.warn('No standalone mpv.exe found beside the player. Expected mpv-thumb\\mpv.exe.')
end
