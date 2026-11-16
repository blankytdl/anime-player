-- AnimePlayer: locate thumbfast's standalone mpv.exe anywhere inside the player folder.
-- Search starts beside the mpv.net frontend executable and only descends into child folders.
-- It never walks to a parent folder or scans outside this player directory.
local mp = require 'mp'
local utils = require 'mp.utils'
local msg = require 'mp.msg'

local function is_file(path)
    local info = utils.file_info(path)
    return info ~= nil and info.is_file
end

local function is_directory(path)
    if type(path) ~= 'string' or path == '' then return false end
    local info = utils.file_info(path)
    return info ~= nil and info.is_dir
end

local function path_key(path)
    return path:gsub("\\", "/"):gsub("/+$", ""):lower()
end

local function find_in_player_tree(root)
    if type(root) ~= 'string' or root == '' or not is_directory(root) then
        return nil
    end

    -- Keep the normal packaged location first for a fast startup.
    for _, relative_path in ipairs({
        'mpv-thumb/mpv.exe',
        'mpv.exe',
        'mpv/mpv.exe'
    }) do
        local candidate = utils.join_path(root, relative_path)
        if is_file(candidate) then
            return candidate
        end
    end

    local visited = {}
    local fallback
    local function visit(directory, depth)
        -- Protect against pathological junction loops while allowing deeply nested layouts.
        if depth > 128 then return nil end

        local key = path_key(directory)
        if visited[key] then return nil end
        visited[key] = true

        local candidate = utils.join_path(directory, 'mpv.exe')
        if is_file(candidate) then
            local leaf = directory:match('([^/\\]+)[/\\]*$')
            if leaf and leaf:lower() == 'mpv-thumb' then
                return candidate
            end
            if not fallback then fallback = candidate end
        end

        local children = utils.readdir(directory, 'dirs')
        if type(children) ~= 'table' then return nil end

        -- Visit any folder called mpv-thumb before unrelated folders.
        table.sort(children, function(a, b)
            local a_preferred = a:lower() == 'mpv-thumb'
            local b_preferred = b:lower() == 'mpv-thumb'
            if a_preferred ~= b_preferred then return a_preferred end
            return a:lower() < b:lower()
        end)

        for _, child in ipairs(children) do
            -- readdir returns directory-entry names, not absolute paths.
            -- Reject separators and dot entries so traversal stays beneath root.
            if child ~= '' and child ~= '.' and child ~= '..'
                and not child:find('[/\\]') then
                local found = visit(utils.join_path(directory, child), depth + 1)
                if found then return found end
            end
        end
        return nil
    end

    return visit(root, 0) or fallback
end

-- mpv.net 7+ exposes its frontend executable path. Use that directory as the
-- search boundary; fall back to mpv's executable directory only if unavailable.
local frontend_path = mp.get_property_native('user-data/frontend/process-path')
local search_root
if type(frontend_path) == 'string' and frontend_path ~= '' then
    search_root = frontend_path:match('^(.*)[/\\][^/\\]+$')
end

if not is_directory(search_root) then
    search_root = mp.command_native({ 'expand-path', '~~exe_dir/' })
end

local found = find_in_player_tree(search_root)
if found then
    -- change-list append adds one key=value atomically. Use mpv's length syntax for
    -- paths containing commas or percent signs so script-opts keeps the path intact.
    local value = found:find('[,%%]') and ('%' .. #found .. '%' .. found) or found
    mp.commandv('change-list', 'script-opts', 'append', 'thumbfast-mpv_path=' .. value)
    msg.info('thumbfast standalone mpv found inside player folder: ' .. found)
else
    msg.warn('No standalone mpv.exe found inside the player folder or its subfolders.')
end
