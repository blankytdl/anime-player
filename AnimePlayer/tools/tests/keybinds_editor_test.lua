local handlers, binds = {}, {}
local mp = {}
function mp.get_script_name() return 'ap_keybinds' end
function mp.command_native(t) return '/tmp/kbcfg/' end
function mp.register_script_message(n, f) handlers[n] = f end
function mp.commandv(...) local a = {...}; if a[1] == 'keybind' then print('keybind', a[2], a[3]) end end
function mp.osd_message(m) print('OSD', (m:gsub('\n', ' | '))) end
function mp.add_forced_key_binding(key, id, f) binds[key] = f; binds[id] = f end
function mp.remove_key_binding(id) end
local utils = {join_path = function(a, b) return a .. b end,
  file_info = function(p) local f = io.open(p, 'rb'); if f then f:close(); return {is_file = true} end end,
  format_json = function(t) return 'json' end,
  parse_json = function(s) local t = {}; for k, v in s:gmatch('"([%w_]+)":"([^"]*)"') do t[k] = v end; for k, v in s:gmatch('"([%w_]+)":(%d+)') do t[k] = tonumber(v) end; return t end}
package.loaded['mp'] = mp; package.loaded['mp.utils'] = utils
os.execute('mkdir -p /tmp/kbcfg; printf "# header\\nSPACE   cycle pause  #custom-menu: Play / Pause\\nCtrl+q   quit\\nF5  script-message-to mpvnet show-anime-settings  #custom-menu: AnimePlayer Settings...\\nx   add volume 5 ; show-text \\"#not a comment\\"\\n" > /tmp/kbcfg/input.conf')
dofile('/home/claude/out/AnimePlayer/portable_config/scripts/ap-keybinds.lua')
handlers['ap-keybinds-open']()
-- row 3 = F5 (rows: SPACE, Ctrl+q, F5, x): change F5 -> F10
handlers['ap-kb-event']('{"type":"activate","value":3}')
print('captured keys:', binds['F10'] ~= nil, binds['Ctrl+Shift+x'] ~= nil)
binds['F10']()
os.execute('cat /tmp/kbcfg/input.conf; echo ---')
-- take SPACE key for Ctrl+q row (conflict)
handlers['ap-kb-event']('{"type":"activate","value":2}')
binds['SPACE']()
os.execute('cat /tmp/kbcfg/input.conf; echo ---')
-- unbind row 4 then delete row 1
handlers['ap-kb-event']('{"type":"activate","value":4,"action":"unbind"}')
os.execute('cat /tmp/kbcfg/input.conf; ls /tmp/kbcfg')
