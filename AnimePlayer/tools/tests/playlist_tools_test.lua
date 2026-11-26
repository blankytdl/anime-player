-- simulated mpv playlist + events to exercise ap-queue.lua (build 36: playlist tools only, no queue)
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
BOOL = {}
function mp.get_property_bool(n, d) if BOOL[n] ~= nil then return BOOL[n] end return d or false end
function mp.set_property_native(n, v) props[n] = v end
function mp.set_property(n, v) props[n] = v end
function mp.set_property_number() end
function mp.set_property_bool() end
function mp.command() end
PROBES = {}
function mp.command_native_async(t, cb) if t.args and t.args[1] ~= 'explorer.exe' then PROBES[#PROBES + 1] = {args = t.args, cb = cb} end end
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
    elseif a[3] == 'append-play' then table.insert(pl, entry(a[2])); playlist_changed(); if cur < 0 then cur = #pl - 1; load_current() end
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
os.execute('mkdir -p /tmp/qcfg/scripts; touch /tmp/qcfg/scripts/uosc; rm -f /tmp/qcfg/durations.json /tmp/qcfg/playlist-inbox.txt /tmp/qcfg/queue-inbox.txt')

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

local function osd_has(s) for _, l in ipairs(log) do if l:find(s, 1, true) then return true end end return false end
local bad = 0
local function expect(label, cond) print((cond and '  ok   ' or '  FAIL ') .. label); if not cond then bad = bad + 1 end end
local E = {'C:/s/E1.mkv','C:/s/E2.mkv','C:/s/E3.mkv'}

print('== the Queue is gone')
expect('queue-playing flag is "no"', props['user-data/ap/queue-playing'] == 'no')
log = {}; handlers['ap-queue-open'](); expect('an old Queue message says the Queue was removed', osd_has('Queue was removed'))
expect('old Queue messages are known (no silent drop)', handlers['ap-queue-add'] ~= nil and handlers['ap-queue-menu'] ~= nil and handlers['ap-queue-attach'] ~= nil)

print('== PLAY LATER: videos go to the END of the playlist, filtered and without duplicates')
setup(E, 0); log = {}
handlers['ap-playlist-add']('C:/q/X.mkv', 'C:/q/x.MKV', 'C:/q/cover.jpg', 'C:/s/E1.mkv', 'C:\\q\\X.mkv', 'C:/q/song.mp3', 'C:/q/loop.gif'); flush()
expect('E1 E2 E3 X song.mp3 loop.gif (current kept, jpg + duplicates skipped)', names() == '[E1.mkv] E2.mkv E3.mkv X.mkv song.mp3 loop.gif')
expect('message tells what was skipped', osd_has('Added 3 to the playlist') and osd_has('already in the playlist') and osd_has('not a video'))
log = {}; handlers['ap-playlist-add']('C:/q/X.mkv'); flush()
expect('adding the same video again changes nothing', names() == '[E1.mkv] E2.mkv E3.mkv X.mkv song.mp3 loop.gif' and osd_has('Nothing added'))

print('== PLAY LATER on an empty player: the first video starts, the rest follow it')
setup({'C:/s/E1.mkv'}, 0); pl = {}; cur = -1; playlist_changed(); flush()
BOOL['idle-active'] = true
handlers['ap-playlist-add']('C:/q/A.mkv', 'C:/q/B.mkv'); flush()
BOOL['idle-active'] = nil
expect('A plays, B waits', names() == '[A.mkv] B.mkv')

print('== PLAY LATER from drag & drop / Explorer (inbox files, new and old name)')
setup(E, 0)
local f = io.open('/tmp/qcfg/playlist-inbox.txt', 'wb'); f:write('C:/q/D1.mkv\r\nC:/q/D2.mp4\r\nC:/q/readme.txt\r\n'); f:close()
handlers['ap-playlist-inbox'](); flush()
expect('D1 D2 appended, txt skipped', names() == '[E1.mkv] E2.mkv E3.mkv D1.mkv D2.mp4')
f = io.open('/tmp/qcfg/queue-inbox.txt', 'wb'); f:write('C:/q/D3.mkv\r\n'); f:close()
handlers['ap-queue-inbox'](); flush()
expect('the inbox of an older player build still works', names() == '[E1.mkv] E2.mkv E3.mkv D1.mkv D2.mp4 D3.mkv')
expect('inbox files are removed after reading', io.open('/tmp/qcfg/playlist-inbox.txt', 'rb') == nil and io.open('/tmp/qcfg/queue-inbox.txt', 'rb') == nil)

print('== the last video sits on its final frame (keep-open): Play later starts the new one')
setup(E, 2); BOOL['eof-reached'] = true
handlers['ap-playlist-add']('C:/q/N.mkv'); flush(); BOOL['eof-reached'] = nil
expect('N is playing', names() == 'E1.mkv E2.mkv E3.mkv [N.mkv]')

print('== PLAYLIST cleaning (autoload, Open, drag): video / gif / audio only, every file once, the playing entry stays')
setup({'C:/s/E1.mkv','C:/s/song.mp3','C:/s/cover.jpg','C:/s/E1.mkv','C:/s/loop.gif','C:/s/LOOP.GIF','C:/s/E2.mkv','C:/s/sub.ass'}, 0); flush()
expect('E1 song.mp3 loop.gif E2', names() == '[E1.mkv] song.mp3 loop.gif E2.mkv')
setup({'C:/s/E1.mkv','C:/s/E2.mkv','C:/s/E1.mkv'}, 2); flush()
expect('the playing (later) E1 stays, the earlier copy goes', names() == 'E2.mkv [E1.mkv]')
setup({'C:/s/song.mp3','C:/s/a.mkv','C:/s/b.flac','C:/s/c.jpg'}, 0); flush()
expect('audio stays, picture goes', names() == '[song.mp3] a.mkv b.flac')

local function drain()   -- finish every reader that an earlier section left running
  for _ = 1, 50 do
    flush()
    if #PROBES == 0 then break end
    local p = table.remove(PROBES, 1); p.cb(true, {stdout = '60\n'})
  end
end
print('== DURATIONS never compete with a playing video')
BOOL.pause = true; drain(); BOOL.pause = false; BOOL['idle-active'] = false
local D = {'C:/d/D1.mkv','C:/d/D2.mkv','C:/d/D3.mkv'}
setup(D, 0); PROBES = {}; BOOL.pause = false; BOOL['idle-active'] = false; flush()
expect('video plays: nothing is read in the background', #PROBES == 0)
BOOL.pause = true; fire('pause', true); flush()
expect('paused: reading starts, ONE file at a time', #PROBES == 1)
local done_n = 0
while #PROBES > 0 and done_n < 10 do
  expect('never more than one reader at once (#' .. (done_n + 1) .. ')', #PROBES == 1)
  local p = table.remove(PROBES, 1); done_n = done_n + 1
  p.cb(true, {stdout = '1440.5\n'}); flush()
end
expect('all three were read', done_n == 3)
flush()
local map = props['user-data/ap/durations'] or {}
expect('durations are published for the rows', map['c:/d/d1.mkv'] == 1440.5 and map['c:/d/d3.mkv'] == 1440.5)
expect('the footer shows the total', (props['user-data/ap/playlist-footer'] or ''):find('Total 1:12:02') ~= nil)

print('== manual "Read all durations" while a video plays: one file at a time')
BOOL.pause = true; drain(); BOOL.pause = false
setup({'C:/z/M1.mkv','C:/z/M2.mkv','C:/z/M3.mkv'}, 0); PROBES = {}; BOOL.pause = false; flush()
handlers['ap-durations-scan'](); 
expect('manual: one at a time while playing', #PROBES == 1)

print('== sort / shuffle / clear / remove watched (no queue involved)')
setup({'C:/s/E3.mkv','C:/s/E1.mkv','C:/s/E2.mkv'}, 0); handlers['ap-playlist-sort']('name'); flush()
expect('sorted A-Z, playing entry still playing', names() == 'E1.mkv E2.mkv [E3.mkv]' or names() == 'E1.mkv E2.mkv [E3.mkv]')
setup(E, 0); fire('user-data/ap/watched', {['c:/s/e2.mkv'] = true}); handlers['ap-playlist-clear-watched'](); flush()
expect('watched E2 removed', names() == '[E1.mkv] E3.mkv')
setup(E, 1); handlers['ap-playlist-clear'](); flush()
expect('remove all keeps the playing one', names() == '[E2.mkv]')

print(bad == 0 and 'PLAYLIST TOOLS: ALL OK' or ('PLAYLIST TOOLS FAILURES: ' .. bad))
