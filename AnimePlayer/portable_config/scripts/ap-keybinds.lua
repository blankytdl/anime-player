-- AnimePlayer: key binding editor inside the player (F9, or the keyboard button in the control bar).
-- Lists every binding of input.conf; click one (or its keyboard button) and press the new key - it is written to input.conf and works at once.
--   Esc while waiting = cancel.   Row buttons: rebind / unbind / delete.   Tools: add a binding, open mpv.net's Input Editor (Ctrl+i).
-- input.conf stays in plain "KEY command  # description" format, so mpv.net's own Input Editor keeps working with the same file.
local mp = require 'mp'
local utils = require 'mp.utils'

local SELF = mp.get_script_name()
local PATH = utils.join_path(mp.command_native({ 'expand-path', '~~/' }), 'input.conf')
local MENU_TYPE = 'ap_keybinds'

local lines, crlf = {}, false
local entries = {}

-- ---------- file ----------
local function load_file()
    lines, entries = {}, {}
    local f = io.open(PATH, 'rb')
    if not f then return end
    local s = f:read('*a') or ''
    f:close()
    s = s:gsub('^\239\187\191', '')
    crlf = s:find('\r\n', 1, true) ~= nil
    for line in (s .. '\n'):gmatch('(.-)\r?\n') do lines[#lines + 1] = line end
    if lines[#lines] == '' then lines[#lines] = nil end
    for i, line in ipairs(lines) do
        local t = line:gsub('\t', ' ')
        if t:match('%S') and not t:match('^%s*#') then
            local key, rest = t:match('^%s*(%S+)%s+(.-)%s*$')
            if key and rest and rest ~= '' then
                -- the description is the comment after the command (outside quotes)
                local cmd, comment, quote = rest, '', false
                for p = 1, #rest do
                    local c = rest:sub(p, p)
                    if c == '"' then quote = not quote
                    elseif c == '#' and not quote and (p == 1 or rest:sub(p - 1, p - 1):match('%s')) then
                        cmd, comment = rest:sub(1, p - 1):gsub('%s+$', ''), rest:sub(p + 1):gsub('^%s+', '')
                        break
                    end
                end
                comment = comment:gsub('^custom%-menu:%s*', ''):gsub('^menu:%s*', ''):gsub('^!%s*', '')
                -- menu separators ("File > -") are not bindings: keep them out of the list
                if not comment:match('>%s*%-%s*$') then
                    entries[#entries + 1] = { idx = i, key = key, cmd = cmd, title = comment ~= '' and comment or cmd }
                end
            end
        end
    end
end

local function save_file()
    local bak = PATH .. '.keyeditor.bak'
    if not utils.file_info(bak) then
        local src = io.open(PATH, 'rb')
        if src then local d = src:read('*a'); src:close(); local b = io.open(bak, 'wb'); if b then b:write(d); b:close() end end
    end
    local f = io.open(PATH, 'wb')
    if not f then mp.osd_message('Could not write input.conf', 4) return false end
    f:write(table.concat(lines, crlf and '\r\n' or '\n') .. (crlf and '\r\n' or '\n'))
    f:close()
    return true
end

-- ---------- menu ----------
local function menu_data()
    local items = {}
    for i, e in ipairs(entries) do
        items[i] = { title = e.title, hint = (e.key ~= '_' and e.key or ''), value = i }
    end
    return {
        type = MENU_TYPE,
        title = 'Key bindings (Type to Search)',
        tools = {
            { icon = 'add', title = 'Add a binding', value = 'script-message ap-kb-add' },
            { icon = 'open_in_new', title = "mpv.net Input Editor", value = 'script-message-to mpvnet show-input-editor' },
        },
        footnote = 'Click a row and press the new key (Esc = cancel). The row buttons: change the key, ADD ANOTHER KEY for the same action, unbind, delete. Saved to input.conf, applies at once.',
        callback = { SELF, 'ap-kb-event' },
        item_actions = {
            { name = 'rebind', icon = 'keyboard', label = 'Change key' },
            { name = 'extra', icon = 'content_copy', label = 'Add another key for this action' },
            { name = 'unbind', icon = 'backspace', label = 'Unbind (comment out)' },
            { name = 'delete', icon = 'delete', label = 'Delete line' },
        },
        items = items,
    }
end

local function open_menu()
    load_file()
    if #entries == 0 then
        mp.osd_message('Key bindings: no bindings found in\n' .. PATH, 6)
        return
    end
    mp.osd_message('Key bindings: ' .. #entries .. '  (click one, then press the new key)', 2)
    mp.commandv('script-message-to', 'uosc', 'open-menu', utils.format_json(menu_data()))
end

local function refresh_menu()
    mp.commandv('script-message-to', 'uosc', 'update-menu', utils.format_json(menu_data()))
end

-- ---------- key capture ----------
local BASE = {}
do
    local function add(k) BASE[#BASE + 1] = k end
    for c = ('a'):byte(), ('z'):byte() do add(string.char(c)) end
    for c = ('A'):byte(), ('Z'):byte() do add(string.char(c)) end
    for c = ('0'):byte(), ('9'):byte() do add(string.char(c)) end
    for ch in ('`-=[]\\;\',./!@$%^&*()_+{}|:"<>?~'):gmatch('.') do add(ch) end
    for _, k in ipairs({ 'SPACE', 'ENTER', 'TAB', 'BS', 'DEL', 'INS', 'HOME', 'END', 'PGUP', 'PGDWN', 'UP', 'DOWN', 'LEFT', 'RIGHT',
        'KP0', 'KP1', 'KP2', 'KP3', 'KP4', 'KP5', 'KP6', 'KP7', 'KP8', 'KP9', 'KP_ENTER', 'KP_DEC', 'KP_ADD', 'KP_SUBTRACT',
        'KP_MULTIPLY', 'KP_DIVIDE', 'MBTN_MID', 'MBTN_BACK', 'MBTN_FORWARD', 'MBTN_LEFT_DBL', 'MBTN_RIGHT', 'PAUSE', 'PRINT', 'MENU', 'GO_BACK', 'GO_FORWARD' }) do add(k) end
    for n = 1, 24 do add('F' .. n) end
end
local MODS = { '', 'Shift+', 'Ctrl+', 'Alt+', 'Ctrl+Shift+', 'Ctrl+Alt+', 'Alt+Shift+', 'Ctrl+Alt+Shift+' }

local capture_ids = {}
local function capture_stop()
    for _, id in ipairs(capture_ids) do mp.remove_key_binding(id) end
    capture_ids = {}
end

local function capture_start(prompt, cb)
    capture_stop()
    mp.commandv('script-message-to', 'uosc', 'close-menu')
    mp.osd_message(prompt .. '\nPress the new key  (Esc = cancel)', 30)
    local n = 0
    for _, base in ipairs(BASE) do
        for _, mod in ipairs(MODS) do
            -- a single printable character already carries its own Shift
            local single = #base == 1 and not base:match('%w')
            local upper = #base == 1 and base:match('%u')
            if not ((mod:find('Shift') and (single or upper))) and not (base:match('^MBTN') and mod ~= '') then
                local key = mod .. base
                n = n + 1
                local id = 'apkb_' .. n
                capture_ids[#capture_ids + 1] = id
                mp.add_forced_key_binding(key, id, function()
                    capture_stop()
                    mp.osd_message('', 0)
                    if key == 'ESC' then open_menu() return end
                    cb(key)
                end)
            end
        end
    end
    mp.add_forced_key_binding('ESC', 'apkb_esc', function()
        capture_stop(); mp.remove_key_binding('apkb_esc'); mp.osd_message('', 0); open_menu()
    end)
    capture_ids[#capture_ids + 1] = 'apkb_esc'
end

-- ---------- edits ----------
local function used_by(key, except)
    for _, e in ipairs(entries) do
        if e.key == key and e ~= except then return e end
    end
end

local function rebind(entry, newkey)
    local other = used_by(newkey, entry)
    local rest = lines[entry.idx]:gsub('\t', ' '):match('^%s*%S+(%s+.*)$') or ('  ' .. entry.cmd)
    local old = entry.key
    lines[entry.idx] = newkey .. rest
    if other then
        lines[other.idx] = '# [unbound by the key editor] ' .. lines[other.idx]
        mp.commandv('keybind', newkey, entry.cmd)
    else
        mp.commandv('keybind', newkey, entry.cmd)
    end
    if old ~= newkey then mp.commandv('keybind', old, 'ignore') end
    if save_file() then
        mp.osd_message(string.format('%s  ->  %s%s', entry.title, newkey, other and ('   (took the key from: ' .. other.title .. ')') or ''), 4)
    end
    open_menu()
end

-- the SAME action on a second (third ...) key: the original line stays, a new line with the same command is added right below it
local function add_extra(entry, key)
    load_file()
    local clash = used_by(key)
    if clash then lines[clash.idx] = '# [unbound by the key editor] ' .. lines[clash.idx] end
    local at = math.min(#lines, entry.idx) + 1
    table.insert(lines, at, string.format('%-15s %s   # %s (extra key)', key, entry.cmd, entry.title))
    mp.commandv('keybind', key, entry.cmd)
    if save_file() then
        mp.osd_message(string.format('%s  ->  also %s%s', entry.title, key, clash and ('   (took the key from: ' .. clash.title .. ')') or ''), 4)
    end
    open_menu()
end

local function unbind(entry)
    lines[entry.idx] = '# [unbound by the key editor] ' .. lines[entry.idx]
    mp.commandv('keybind', entry.key, 'ignore')
    if save_file() then mp.osd_message('Unbound ' .. entry.key .. '  (' .. entry.title .. ')', 3) end
    open_menu()
end

local function delete_line(entry)
    table.remove(lines, entry.idx)
    mp.commandv('keybind', entry.key, 'ignore')
    if save_file() then mp.osd_message('Deleted ' .. entry.key .. '  (' .. entry.title .. ')', 3) end
    open_menu()
end

local function on_event(json)
    local ev = utils.parse_json(json or '')
    if type(ev) ~= 'table' then return end
    if ev.type ~= 'activate' then return end
    local entry = entries[tonumber(ev.value) or 0]
    if not entry then return end
    if ev.action == 'unbind' then unbind(entry)
    elseif ev.action == 'extra' then capture_start(entry.title .. '  (extra key)', function(key) add_extra(entry, key) end)
    elseif ev.action == 'delete' then delete_line(entry)
    else capture_start(entry.title, function(key) rebind(entry, key) end) end
end

local function add_binding()
    local ok, input = pcall(require, 'mp.input')
    if not ok or not input or not input.get then
        mp.osd_message('Adding needs mpv 0.38+. Open input.conf with Ctrl+i (Input Editor) instead.', 5)
        return
    end
    mp.commandv('script-message-to', 'uosc', 'close-menu')
    input.get({
        prompt = 'Command for the new binding > ',
        submit = function(text)
            input.terminate()
            text = (text or ''):gsub('^%s+', ''):gsub('%s+$', '')
            if text == '' then open_menu() return end
            capture_start(text, function(key)
                load_file()
                local clash = used_by(key)
                if clash then lines[clash.idx] = '# [unbound by the key editor] ' .. lines[clash.idx] end
                lines[#lines + 1] = string.format('%-15s %s', key, text)
                mp.commandv('keybind', key, text)
                if save_file() then mp.osd_message('Added ' .. key .. '  ' .. text, 3) end
                open_menu()
            end)
        end,
    })
end

-- an error inside the editor is shown on screen instead of nothing happening
local function safe(fn)
    return function(...)
        local ok, err = pcall(fn, ...)
        if not ok then
            mp.osd_message('Key bindings error: ' .. tostring(err), 10)
            print('ap-keybinds error: ' .. tostring(err))
        end
    end
end
mp.register_script_message('ap-kb-event', safe(on_event))
mp.register_script_message('ap-keybinds-open', safe(open_menu))
mp.register_script_message('ap-kb-add', safe(add_binding))
