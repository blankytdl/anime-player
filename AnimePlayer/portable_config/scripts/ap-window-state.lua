-- AnimePlayer: persist the Window > Toggle On Top state between launches.
-- The state file lives under portable_config so it follows the player when its folder moves.
local mp = require 'mp'
local msg = require 'mp.msg'

local state_path = mp.command_native({
    'expand-path',
    '~~/script-opts/ap-window-state.conf'
})

local function read_state()
    if type(state_path) ~= 'string' or state_path == '' then
        return false
    end

    local file = io.open(state_path, 'rb')
    if not file then
        return false -- First launch defaults to not pinned.
    end

    local contents = file:read('*a') or ''
    file:close()

    local value = contents:match('ontop%s*=%s*([%a]+)')
    if not value then return false end

    value = value:lower()
    return value == 'yes' or value == 'true' or value == 'on'
end

local last_saved
local function save_state(value)
    if type(value) ~= 'boolean' or last_saved == value then return end
    if type(state_path) ~= 'string' or state_path == '' then
        msg.warn('Could not resolve the portable pin-state file path.')
        return
    end

    local file, err = io.open(state_path, 'wb')
    if not file then
        msg.warn('Could not save the window pin state: ' .. tostring(err))
        return
    end

    local ok, write_err = file:write('ontop=', value and 'yes' or 'no', '\n')
    if ok then
        file:flush()
    end
    file:close()

    if not ok then
        msg.warn('Could not write the window pin state: ' .. tostring(write_err))
        return
    end

    last_saved = value
end

-- Restore the last state before observing changes. Missing/invalid state defaults to off.
local restored = read_state()
local ok, err = pcall(mp.set_property_native, 'ontop', restored)
if not ok then
    msg.warn('Could not restore the window pin state: ' .. tostring(err))
end

-- Save immediately, then save every toggle so the setting survives normal quit
-- and unexpected process termination after the toggle has been changed.
save_state(mp.get_property_native('ontop'))

-- (build 29) The window may not be ready when this script runs at start-up, so the pin is applied again shortly after the start and once the
-- first video is loaded - unless you have already toggled it yourself. Setting the same value again does not tell the window anything, so a pinned
-- state is switched off and on in one go to make sure the window really becomes topmost.
local user_toggled = false
local expected = restored
local ignore_until = 0      -- property events caused by reapply() itself are not user toggles
local function reapply()
    if user_toggled or not restored then return end
    ignore_until = mp.get_time() + 0.6
    pcall(mp.set_property_native, 'ontop', false)
    pcall(mp.set_property_native, 'ontop', true)
    expected = true
end
mp.add_timeout(0.7, reapply)
mp.add_timeout(2.5, reapply)
mp.register_event('file-loaded', function() reapply(); end)   -- only does something for the first loads; a later user toggle sets user_toggled

mp.observe_property('ontop', 'bool', function(_, value)
    if mp.get_time() < ignore_until then expected = value; return end
    if type(value) == 'boolean' and value ~= expected then user_toggled = true end
    expected = value
    if user_toggled then save_state(value) end
end)

mp.register_event('shutdown', function()
    save_state(mp.get_property_native('ontop'))
end)
