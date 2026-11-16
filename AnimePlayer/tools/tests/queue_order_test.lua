-- simulated mpv playlist + events to exercise ap-queue.lua
local handlers, events, observers, timers = {}, {}, {}, {}
local props = {}
local pl = {}          -- {id, filename}
local cur = -1         -- 0-based index, -1 none
local nextid = 1
local log = {}
local function entry(path) local e = {id = nextid, filename = path}; nextid = nextid + 1; return e end
local function native_playlist()
  local out = {}
  for i, e in ipairs(pl) do out[i] = {id = e.id, filename = e.filename, current = (i - 1 == cur) or nil} end
  return out
end
local function fire(name, ...) for _, f in ipairs(observers[name] or {}) do f(name, ...) end end
local function playlist_changed() fire('playlist', native_playlist()); fire('playlist-count', #pl) end
local mp = {}
function mp.get_script_name() return 'ap_queue' end
function mp.command_native(t) if t[1] == 'expand-path' then return '/tmp/qcfg/' end return {} end
function mp.register_script_message(n, f) handlers[n] = f end
function mp.register_event(n, f) events[n] = events[n] or {}; table.insert(events[n], f) end
function mp.observe_property(n, t, f) observers[n] = observers[n] or {}; table.insert(observers[n], f) end
function mp.unobserve_property() end
function mp.add_timeout(t, f) local tm = {f = f, alive = true}; function tm:kill() self.alive = false end; table.insert(timers, tm); return tm end
function mp.add_hook() end
TIME=0
function mp.get_time() return TIME end
function mp.osd_message(m) log[#log + 1] = 'OSD: ' .. m end
function mp.get_property_number(n, d)
  if n == 'playlist-count' then return #pl elseif n == 'playlist-pos' then return cur elseif n == 'osd-width' then return 1280 end
  return d
end
function mp.get_property(n) if n == 'path' then return pl[cur + 1] and pl[cur + 1].filename end return nil end
function mp.get_property_native(n) if n == 'playlist' then return native_playlist() end return props[n] end
function mp.get_property_bool() return false end
function mp.set_property_native(n, v) props[n] = v end
function mp.set_property(n, v) props[n] = v end
function mp.set_property_number() end
function mp.set_property_bool() end
function mp.command() end
function mp.command_native_async() end
local function load_current()
  for _, f in ipairs(events['end-file'] or {}) do f({reason = 'stop'}) end
  for _, f in ipairs(events['file-loaded'] or {}) do f({}) end
  fire('playlist-pos', cur)
end
function mp.commandv(...)
  local a = {...}
  local c = a[1]
  if c == 'loadfile' then
    if a[3] == 'append' then table.insert(pl, entry(a[2])); playlist_changed()
    elseif a[3] == 'replace' then pl = {entry(a[2])}; cur = 0; playlist_changed(); load_current() end
  elseif c == 'playlist-move' then
    local from, to = tonumber(a[2]), tonumber(a[3])
    local e = table.remove(pl, from + 1)
    local pos = (from < to) and (to - 1) or to
    table.insert(pl, pos + 1, e)
    -- keep the current entry current
    for i, x in ipairs(pl) do if x.__cur then cur = i - 1 end end
    playlist_changed()
  elseif c == 'playlist-remove' then
    local i = tonumber(a[2]); table.remove(pl, i + 1); if i < cur then cur = cur - 1 end; playlist_changed()
  elseif c == 'playlist-clear' then
    local keep = pl[cur + 1]; pl = {keep}; cur = 0; playlist_changed()
  elseif c == 'playlist-play-index' then
    cur = tonumber(a[2]); playlist_changed(); load_current()
  elseif c == 'playlist-shuffle' then
    -- reverse (deterministic "shuffle"), current entry follows
    local curid = pl[cur + 1].id
    local r = {}; for i = #pl, 1, -1 do r[#r + 1] = pl[i] end; pl = r
    for i, x in ipairs(pl) do if x.id == curid then cur = i - 1 end end
    playlist_changed()
  elseif c == 'script-message-to' then
    LAST_MENU = a[4]
  elseif c == 'keybind' then
  end
end
-- the playlist-move above must keep `cur` right: track by id
local real_move = mp.commandv
function mp.commandv(...)
  local a = {...}
  local curid = pl[cur + 1] and pl[cur + 1].id
  real_move(...)
  if a[1] == 'playlist-move' or a[1] == 'playlist-remove' then
    for i, x in ipairs(pl) do if x.id == curid then cur = i - 1 end end
  end
end
local utils = {}
function utils.join_path(a, b) return a .. b end
function utils.file_info(p) if p:match('^C:/') then return {is_file = true, size = 1, mtime = 1} end local f = io.open(p, 'rb'); if f then f:close(); return {is_file = true, size = 1, mtime = 1} end end
local function ser(v)
  if type(v)=='table' then
    if #v>0 then local o={} for _,x in ipairs(v) do o[#o+1]=ser(x) end return '['..table.concat(o,',')..']' end
    local keys={} for k in pairs(v) do keys[#keys+1]=k end table.sort(keys)
    local o={} for _,k in ipairs(keys) do o[#o+1]=string.format('%q:%s',k,ser(v[k])) end return '{'..table.concat(o,',')..'}'
  elseif type(v)=='string' then return string.format('%q',v) else return tostring(v) end
end
function utils.format_json(v) return ser(v) end
function utils.parse_json(str)
  if not str or str=='' then return nil end
  local t={}
  for k,v in str:gmatch('"([%w_]+)":"([^"]*)"') do t[k]=v end
  for k,v in str:gmatch('"([%w_]+)":(%d+)') do t[k]=tonumber(v) end
  return next(t) and t or nil
end
function utils.readdir() return {} end
function utils.split_path(p) local d,n = p:match('^(.*/)([^/]*)$'); return d or '', n or p end
package.loaded['mp'] = mp
package.loaded['mp.utils'] = utils
package.loaded['mp.options'] = {read_options = function() end}
package.loaded['mp.msg'] = {info = print, warn = print, debug = function() end, error = print}
os.execute('mkdir -p /tmp/qcfg/scripts; touch /tmp/qcfg/scripts/uosc; rm -f /tmp/qcfg/queue-list.json /tmp/qcfg/durations.json')

local ok, err = pcall(dofile, (os.getenv('AP_ROOT') or '.') .. '/portable_config/scripts/ap-queue.lua')
if not ok then print('LOAD ERROR', err) os.exit(1) end

LAST_MENU=nil
local function flush() for i = 1, 20 do local t = timers; timers = {}; for _, tm in ipairs(t) do if tm.alive then tm.f() end end end end
local function show(label)
  flush()
  local names = {}
  local hidden = props['user-data/ap/hidden'] or {}
  for i, e in ipairs(pl) do
    local n = e.filename:match('[^/]+$')
    if hidden[tostring(e.id)] then n = '(' .. n .. ')' end
    if i - 1 == cur then n = '[' .. n .. ']' end
    names[#names + 1] = n
  end
  print(string.format('%-34s %s', label, table.concat(names, ' ')))
end
local function play_index(i) cur = i; playlist_changed(); load_current(); flush() end
local function names()
  local t = {}
  for i, e in ipairs(pl) do local n = e.filename:match('[^/]+$'); if i-1==cur then n='['..n..']' end; t[#t+1]=n end
  return table.concat(t,' ')
end
local played = {}
local function advance(dir)   -- same as mpv playlist-next / playlist-prev / eof
  local ni = cur + dir
  if ni < 0 or ni >= #pl then return false end
  play_index(ni); played[#played+1] = pl[cur+1].filename:match('[^/]+$'); return true
end
local function setup(list, start)
  pl = {}; for _, p in ipairs(list) do pl[#pl+1] = entry(p) end
  cur = start; played = {}; playlist_changed(); load_current(); flush()
end
local function click(i) TIME=TIME+5; local j = '{"type":"activate","value":'..i..'}'; handlers['ap-queue-event'](j); handlers['ap-queue-event'](j); flush() end
local function run(title, list, start, queue_adds, click_row, steps)
  setup(list, start)
  for _, q in ipairs(queue_adds) do handlers['ap-queue-add'](q) end; flush()
  print('== '..title); print('  before        ', names())
  if click_row then click(click_row) ; print('  play queue row', names()) end
  local seq = {}
  for i = 1, steps do
    local before = pl[cur+1] and pl[cur+1].filename:match('[^/]+$')
    if not advance(1) then break end
    flush(); seq[#seq+1] = pl[cur+1].filename:match('[^/]+$')
  end
  print('  playing then  ', table.concat(seq, ' > ')); print('  after         ', names())
end
local E = {'C:/s/E1.mkv','C:/s/E2.mkv','C:/s/E3.mkv','C:/s/E4.mkv','C:/s/E5.mkv','C:/s/E6.mkv','C:/s/E7.mkv'}
run('A: other-folder queue items, started from Queue tab', E, 4, {'C:/q/X.mkv','C:/q/Y.mkv'}, 1, 4)
run('B: queue items that are EARLIER playlist entries', E, 4, {'C:/s/E1.mkv','C:/s/E2.mkv'}, 1, 5)
run('C: queue items that are LATER playlist entries', E, 1, {'C:/s/E5.mkv','C:/s/E6.mkv'}, 1, 5)
run('D: queue = same ep as current', E, 3, {'C:/s/E4.mkv','C:/q/X.mkv'}, 1, 4)
-- played queue videos stay (greyed) at the bottom of the Queue tab
setup(E, 4)
handlers['ap-queue-add']('C:/q/X.mkv'); handlers['ap-queue-add']('C:/q/Y.mkv'); flush()
click(1)
advance(1); flush(); advance(1); flush()
handlers['ap-queue-open'](); flush()
print('== played rows stay in the queue menu:')
print(string.sub(tostring(LAST_MENU),1,1500)); for t, h in tostring(LAST_MENU):gmatch([[\"title\":\"([^\"]*)\".-\"hint\":\"([^\"]*)\"]]) do print("  ", t, h) end
-- sorting / shuffling the playlist while a queue video plays must not change where the playlist goes on
for _, mode in ipairs({'reverse', 'shuffle'}) do
  setup(E, 2)
  handlers['ap-queue-add']('C:/q/X.mkv'); handlers['ap-queue-add']('C:/q/Y.mkv'); flush()
  click(1)
  if mode == 'shuffle' then handlers['ap-playlist-shuffle']() else handlers['ap-playlist-sort'](mode) end
  flush(); print('== '..mode..' while X plays:', names())
  local seq = {}
  for i = 1, 3 do if not advance(1) then break end; flush(); seq[#seq+1] = pl[cur+1].filename:match('[^/]+$') end
  print('   next ones:', table.concat(seq, ' > '))
end
-- Play later on an EMPTY player: first starts, the rest follow in order, then the player is idle (nothing is deleted, nothing replays)
pl = {}; cur = -1; playlist_changed(); flush()
handlers['ap-queue-add']('C:/q/P.mkv'); handlers['ap-queue-add']('C:/q/Q.mkv'); handlers['ap-queue-add']('C:/q/R.mkv'); flush()
print('== idle + 3 queued:', names())
local seq = {}
for i = 1, 4 do if not advance(1) then break end; flush(); seq[#seq+1] = pl[cur+1].filename:match('[^/]+$') end
print('   next ones:', table.concat(seq, ' > '))
-- replay a played row
local nf = 0
handlers['ap-queue-open'](); flush()
print('   replay row 1 of the played list:'); TIME = TIME + 5
handlers['ap-queue-event']('{"type":"activate","value":2}'); handlers['ap-queue-event']('{"type":"activate","value":2}'); flush()
print('   now:', names())
