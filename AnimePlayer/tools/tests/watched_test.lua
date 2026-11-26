-- ap-watched.lua (build 36): which finished videos are greyed out in the Playlist tab. Simulated mpv.
local events, observers, timers, props = {}, {}, {}, {}
local DURATION, PATH, WD = 1440, nil, 'C:/work'
local mp = {}
function mp.command_native(t) return '/tmp/wcfg/' end
function mp.register_event(n, f) events[n] = events[n] or {}; table.insert(events[n], f) end
function mp.observe_property(n, t, f) observers[n] = observers[n] or {}; table.insert(observers[n], f) end
function mp.register_script_message() end
function mp.add_timeout(t, f) timers[#timers + 1] = f; return {kill = function() end} end
function mp.osd_message() end
function mp.get_property(n) if n == 'path' then return PATH elseif n == 'working-directory' then return WD end end
function mp.get_property_number(n, d) if n == 'duration' then return DURATION end return d end
function mp.set_property_native(n, v) props[n] = v end
local utils = {}
function utils.join_path(a, b) if a:sub(-1) == '/' then return a .. b end return a .. '/' .. b end
function utils.file_info(p) local f = io.open(p, 'rb'); if f then local n = f:seek('end'); f:close(); return {is_file = true, size = n} end end
function utils.format_json(t) local o = {}; for _, v in ipairs(t) do o[#o + 1] = string.format('%q', v) end return '[' .. table.concat(o, ',') .. ']' end
function utils.parse_json(s) local t = {}; for v in s:gmatch('"([^"]*)"') do t[#t + 1] = v end return t end
package.loaded['mp'] = mp; package.loaded['mp.utils'] = utils
os.execute('mkdir -p /tmp/wcfg; rm -f /tmp/wcfg/watched-files.json')
local f = io.open('/tmp/wcfg/watched.log', 'wb')
f:write('1700000000\tC:\\Anime\\Old\\Ep01.mkv\tstart\t0.0\t1400.0\t1\n1700001400\tC:\\Anime\\Old\\Ep01.mkv\tfinished\t1250.0\t1400.0\t1\n1700002000\tC:\\Anime\\Old\\Ep02.mkv\tstop\t300.0\t1400.0\t1\n')
f:close()
local ok, err = pcall(dofile, (os.getenv('AP_ROOT') or '.') .. '/portable_config/scripts/ap-watched.lua')
if not ok then print('LOAD ERROR', err) os.exit(1) end

local function fire(name, v) for _, f in ipairs(observers[name] or {}) do f(name, v) end end
local function ev(name, e) for _, f in ipairs(events[name] or {}) do f(e or {}) end end
local function play(path, dur, from, to)   -- open, play from `from` to `to`
  PATH, DURATION = path, dur
  ev('start-file'); ev('file-loaded')
  fire('time-pos', from); fire('time-pos', to)
end
local function is(p) return (props['user-data/ap/watched'] or {})[(p:lower():gsub('\\', '/'))] == true end
local bad = 0
local function expect(label, cond) print((cond and '  ok   ' or '  FAIL ') .. label); if not cond then bad = bad + 1 end end

play('C:/v/a.mkv', 1440, 0, 1438); ev('end-file', {reason = 'eof'})
expect('plays to its end (eof)', is('C:/v/a.mkv'))
play('C:/v/b.mkv', 1440, 0, 1415); ev('end-file', {reason = 'eof'})
expect('ended by itself 25 s before the header length (the old rule missed this one)', is('C:/v/b.mkv'))
play('C:/v/c.mkv', 1440, 0, 1330); ev('end-file', {reason = 'stop'})
expect('Next pressed during the credits (92 %) counts as watched', is('C:/v/c.mkv'))
play('C:/v/d.mkv', 1440, 0, 600); ev('end-file', {reason = 'stop'})
expect('Next pressed at 40 % does NOT', not is('C:/v/d.mkv'))
play('C:/v/e.mkv', 1440, 1400, 1405); ev('end-file', {reason = 'stop'})
expect('resumed near the end and closed again does NOT', not is('C:/v/e.mkv'))
play('C:/v/f.mkv', 1440, 1400, 1438); ev('end-file', {reason = 'eof'})
expect('resumed near the end and played to the end DOES', is('C:/v/f.mkv'))
play('C:/v/g.mkv', 1440, 0, 1300); ev('end-file', {reason = 'quit'})
expect('player closed in the credits counts', is('C:/v/g.mkv'))
play('C:/v/h.mkv', 3, 0, 3); ev('end-file', {reason = 'eof'})
expect('a 3 s clip never counts', not is('C:/v/h.mkv'))
play('C:/v/i.mkv', 1440, 0, 1438); fire('eof-reached', true)
expect('last video of the list (keep-open, eof-reached) counts', is('C:/v/i.mkv'))
play('C:/v/i2.mkv', 1440, 0, 1); fire('eof-reached', true)
expect('a late eof-reached of the PREVIOUS file does not mark the new one', not is('C:/v/i2.mkv'))
PATH = 'rel/j.mkv'; WD = 'C:/work'
play('rel/j.mkv', 1440, 0, 1438); ev('end-file', {reason = 'eof'})
expect('opened as a relative path: the absolute spelling is greyed too', is('rel/j.mkv') and is('C:/work/rel/j.mkv'))
play('C:\\Mixed\\Case\\K.MKV', 1440, 0, 1438); ev('end-file', {reason = 'eof'})
expect('slashes / case do not matter', is('c:/mixed/case/k.mkv'))
for _, t in ipairs(timers) do t() end
expect('videos finished in the past (watched.log) are greyed after the start', is('C:/Anime/Old/Ep01.mkv'))
expect('...but not the one that was only stopped', not is('C:/Anime/Old/Ep02.mkv'))
print(bad == 0 and 'WATCHED: ALL OK' or ('WATCHED FAILURES: ' .. bad))
